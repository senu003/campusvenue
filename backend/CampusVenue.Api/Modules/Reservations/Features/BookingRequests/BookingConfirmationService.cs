using System.Data;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Infrastructure.Email;
using CampusVenue.Api.Modules.Facilities.Features.Availability;
using CampusVenue.Api.Modules.Reservations.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace CampusVenue.Api.Modules.Reservations.Features.BookingRequests;

public enum BookingConfirmationStatus
{
    Success,
    NotFound,
    Forbidden,
    InvalidStatus,
    Conflict,
    Error
}

public record BookingConfirmationResult(
    BookingConfirmationStatus Status,
    VenueBookingRequest? BookingRequest = null,
    string? ErrorMessage = null
)
{
    public static BookingConfirmationResult SuccessResult(VenueBookingRequest request) =>
        new(BookingConfirmationStatus.Success, BookingRequest: request);

    public static BookingConfirmationResult NotFoundResult(string message) =>
        new(BookingConfirmationStatus.NotFound, ErrorMessage: message);

    public static BookingConfirmationResult ForbiddenResult(string message) =>
        new(BookingConfirmationStatus.Forbidden, ErrorMessage: message);

    public static BookingConfirmationResult InvalidStatusResult(string message) =>
        new(BookingConfirmationStatus.InvalidStatus, ErrorMessage: message);

    public static BookingConfirmationResult ConflictResult(string message) =>
        new(BookingConfirmationStatus.Conflict, ErrorMessage: message);

    public static BookingConfirmationResult ErrorResult(string message) =>
        new(BookingConfirmationStatus.Error, ErrorMessage: message);
}

/// <summary>
/// Domain service encapsulating booking confirmation rules, availability re-evaluation,
/// and the serializable transaction with PostgreSQL GiST exclusion constraint enforcement.
/// </summary>
public class BookingConfirmationService(
    CampusVenueDbContext dbContext,
    AvailabilityService availabilityService,
    IEmailSender emailSender,
    ILogger<BookingConfirmationService> logger)
{
    public async Task<BookingConfirmationResult> ConfirmAsync(
        Guid bookingRequestId,
        Guid confirmedByUserId,
        CancellationToken cancellationToken = default)
    {
        var bookingRequest = await dbContext.VenueBookingRequests
            .Include(r => r.Venue)
            .Include(r => r.Requester)
            .Include(r => r.ConfirmedReservation)
            .Include(r => r.Notifications)
            .FirstOrDefaultAsync(r => r.Id == bookingRequestId, cancellationToken);

        if (bookingRequest == null)
        {
            return BookingConfirmationResult.NotFoundResult($"Booking request with ID '{bookingRequestId}' was not found.");
        }

        // Idempotent retry: if already confirmed, return existing reservation without creating duplicates
        if (bookingRequest.Status == BookingRequestStatus.Confirmed)
        {
            return BookingConfirmationResult.SuccessResult(bookingRequest);
        }

        if (bookingRequest.Status != BookingRequestStatus.AwaitingConfirmation)
        {
            return BookingConfirmationResult.InvalidStatusResult(
                $"Cannot confirm booking request in '{bookingRequest.Status}' status. Must be in '{BookingRequestStatus.AwaitingConfirmation}' status.");
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            // Row lock on Venue to prevent concurrent calendar updates and confirmations from proceeding on stale data
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM venues WHERE id = {bookingRequest.VenueId} FOR UPDATE",
                cancellationToken);

            var venue = await dbContext.Venues
                .Include(v => v.Managers)
                    .ThenInclude(m => m.User)
                .FirstOrDefaultAsync(v => v.Id == bookingRequest.VenueId, cancellationToken);

            if (venue == null || !venue.IsActive)
            {
                return BookingConfirmationResult.InvalidStatusResult("Venue is not active or not found.");
            }

            var currentRequest = await dbContext.VenueBookingRequests
                .Include(r => r.Venue)
                .Include(r => r.Requester)
                .Include(r => r.ConfirmedReservation)
                .Include(r => r.Notifications)
                .FirstOrDefaultAsync(r => r.Id == bookingRequest.Id, cancellationToken);

            if (currentRequest == null)
            {
                return BookingConfirmationResult.NotFoundResult("Booking request not found.");
            }

            // Retry check inside transaction
            if (currentRequest.Status == BookingRequestStatus.Confirmed)
            {
                return BookingConfirmationResult.SuccessResult(currentRequest);
            }

            if (currentRequest.Status != BookingRequestStatus.AwaitingConfirmation)
            {
                return BookingConfirmationResult.InvalidStatusResult(
                    $"Cannot confirm booking request in '{currentRequest.Status}' status. Must be in '{BookingRequestStatus.AwaitingConfirmation}' status.");
            }

            // Availability Recheck: Re-evaluates venue availability through Facilities AvailabilityService
            // across full occupied interval [EventStart - Setup, EventEnd + Cleanup]
            var availRequest = new CheckAvailabilityRequest(
                EventStartUtc: currentRequest.EventStartUtc,
                EventEndUtc: currentRequest.EventEndUtc,
                SetupMinutes: currentRequest.SetupMinutes,
                CleanupMinutes: currentRequest.CleanupMinutes
            );

            var availResult = await availabilityService.CheckAvailabilityAsync(venue, availRequest, dbContext, cancellationToken);

            if (availResult.Status != AvailabilityStatus.Available)
            {
                // CRUCIAL INVARIANT: Leave request in AwaitingConfirmation when conflict is detected
                await transaction.RollbackAsync(cancellationToken);
                return BookingConfirmationResult.ConflictResult(
                    availResult.Reason ?? "The venue is no longer available for the requested time. The request remains awaiting confirmation.");
            }

            var occupiedStartUtc = DateTime.SpecifyKind(currentRequest.EventStartUtc.AddMinutes(-currentRequest.SetupMinutes), DateTimeKind.Utc);
            var occupiedEndUtc = DateTime.SpecifyKind(currentRequest.EventEndUtc.AddMinutes(currentRequest.CleanupMinutes), DateTimeKind.Utc);
            var occupiedRange = new NpgsqlRange<DateTime>(occupiedStartUtc, true, occupiedEndUtc, false);
            var reservationRef = $"RES-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

            var confirmedReservation = new ConfirmedReservation
            {
                Id = Guid.NewGuid(),
                BookingRequestId = currentRequest.Id,
                VenueId = venue.Id,
                ReservationReference = reservationRef,
                EventTitle = currentRequest.EventTitle,
                EventStartUtc = currentRequest.EventStartUtc,
                EventEndUtc = currentRequest.EventEndUtc,
                SetupMinutes = currentRequest.SetupMinutes,
                CleanupMinutes = currentRequest.CleanupMinutes,
                OccupiedStartUtc = occupiedStartUtc,
                OccupiedEndUtc = occupiedEndUtc,
                OccupiedRange = occupiedRange,
                ConfirmedByUserId = confirmedByUserId,
                ConfirmedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            };

            currentRequest.Status = BookingRequestStatus.Confirmed;
            currentRequest.UpdatedAtUtc = DateTime.UtcNow;

            var notifications = new List<BookingNotification>();

            var coordinatorNotification = new BookingNotification
            {
                Id = Guid.NewGuid(),
                BookingRequestId = currentRequest.Id,
                RecipientUserId = currentRequest.RequesterId,
                RecipientEmail = currentRequest.Requester.Email ?? string.Empty,
                NotificationType = "BookingConfirmed",
                Subject = $"[CampusVenue] Booking Confirmed: {currentRequest.EventTitle} ({reservationRef})",
                BodyHtml = $"""
                    <div style="font-family: sans-serif; line-height: 1.5;">
                        <h2>Booking Confirmed</h2>
                        <p>Your venue reservation for <strong>{venue.Name}</strong> ({venue.Code}) is officially confirmed!</p>
                        <ul>
                            <li><strong>Reservation Reference:</strong> {reservationRef}</li>
                            <li><strong>Event Title:</strong> {currentRequest.EventTitle}</li>
                            <li><strong>Event Interval:</strong> {currentRequest.EventStartUtc:yyyy-MM-dd HH:mm} to {currentRequest.EventEndUtc:yyyy-MM-dd HH:mm} UTC</li>
                            <li><strong>Occupied Interval (with buffers):</strong> {occupiedStartUtc:yyyy-MM-dd HH:mm} to {occupiedEndUtc:yyyy-MM-dd HH:mm} UTC</li>
                            <li><strong>Expected Attendance:</strong> {currentRequest.ExpectedAttendance}</li>
                        </ul>
                        <p>This venue is now officially reserved and blocked on the campus calendar.</p>
                    </div>
                    """,
                SentAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            };
            notifications.Add(coordinatorNotification);

            foreach (var manager in venue.Managers)
            {
                var managerEmail = manager.User?.Email;
                if (string.IsNullOrWhiteSpace(managerEmail)) continue;

                var managerNotification = new BookingNotification
                {
                    Id = Guid.NewGuid(),
                    BookingRequestId = currentRequest.Id,
                    RecipientUserId = manager.UserId,
                    RecipientEmail = managerEmail,
                    NotificationType = "BookingConfirmedManagerNotice",
                    Subject = $"[CampusVenue] Reservation Confirmed for {venue.Name} - {reservationRef}",
                    BodyHtml = $"""
                        <div style="font-family: sans-serif; line-height: 1.5;">
                            <h2>Reservation Confirmed</h2>
                            <p>Coordinator {currentRequest.Requester.Email} has finalized confirmation for <strong>{venue.Name}</strong> ({venue.Code}).</p>
                            <ul>
                                <li><strong>Reservation Reference:</strong> {reservationRef}</li>
                                <li><strong>Event Title:</strong> {currentRequest.EventTitle}</li>
                                <li><strong>Occupied Interval:</strong> {occupiedStartUtc:yyyy-MM-dd HH:mm} to {occupiedEndUtc:yyyy-MM-dd HH:mm} UTC</li>
                            </ul>
                        </div>
                        """,
                    SentAtUtc = DateTime.UtcNow,
                    CreatedAtUtc = DateTime.UtcNow
                };
                notifications.Add(managerNotification);
            }

            dbContext.ConfirmedReservations.Add(confirmedReservation);
            dbContext.BookingNotifications.AddRange(notifications);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.ExclusionViolation)
            {
                await transaction.RollbackAsync(cancellationToken);
                return BookingConfirmationResult.ConflictResult(
                    "Venue has been booked by a concurrent reservation. The requested slot is no longer available.");
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "40001")
            {
                await transaction.RollbackAsync(cancellationToken);
                return BookingConfirmationResult.ConflictResult(
                    "Calendar data was modified concurrently. The request remains awaiting confirmation.");
            }

            currentRequest.ConfirmedReservation = confirmedReservation;
            foreach (var n in notifications)
            {
                currentRequest.Notifications.Add(n);
            }

            foreach (var n in notifications)
            {
                try
                {
                    await emailSender.SendEmailAsync(n.RecipientEmail, n.Subject, n.BodyHtml);
                }
                catch (Exception emailEx)
                {
                    logger.LogWarning(emailEx, "Failed to send confirmation email to {RecipientEmail}", n.RecipientEmail);
                }
            }

            return BookingConfirmationResult.SuccessResult(currentRequest);
        });
    }
}
