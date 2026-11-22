using System.Security.Claims;
using CampusVenue.Api.Common;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Infrastructure.Email;
using CampusVenue.Api.Modules.Facilities.Features.Availability;
using CampusVenue.Api.Modules.Identity.Domain;
using CampusVenue.Api.Modules.Reservations.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CampusVenue.Api.Modules.Reservations.Features.BookingRequests;

public static class BookingRequestEndpoints
{
    public static IEndpointRouteBuilder MapBookingRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .RequireAuthorization();

        // 1. Submit Booking Request (Role: EventCoordinator or UniversityAdmin)
        group.MapPost("/venues/{venueId:guid}/booking-requests", async (
            Guid venueId,
            CreateBookingRequest request,
            CampusVenueDbContext dbContext,
            AvailabilityService availabilityService,
            IEmailSender emailSender,
            HttpContext httpContext) =>
        {
            var user = httpContext.User;
            var isCoordinator = user.IsInRole(CampusRoles.EventCoordinator);
            var isUniAdmin = user.IsInRole(CampusRoles.UniversityAdmin);

            if (!isCoordinator && !isUniAdmin)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: $"Only users with '{CampusRoles.EventCoordinator}' role can submit venue booking requests."
                );
            }

            var currentUserId = GetCurrentUserId(user);
            if (!currentUserId.HasValue)
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.EventTitle))
            {
                return Results.BadRequest(new { message = "Event title is required." });
            }

            if (request.EventEndUtc <= request.EventStartUtc)
            {
                return Results.BadRequest(new { message = "Event end time must be strictly after start time." });
            }

            if (request.ExpectedAttendance <= 0)
            {
                return Results.BadRequest(new { message = "Expected attendance must be greater than zero." });
            }

            var venue = await dbContext.Venues
                .Include(v => v.Managers)
                    .ThenInclude(m => m.User)
                .FirstOrDefaultAsync(v => v.Id == venueId);

            if (venue == null)
            {
                return Results.NotFound(new { message = $"Venue with ID '{venueId}' was not found." });
            }

            if (!venue.IsActive)
            {
                return Results.BadRequest(new { message = $"Venue '{venue.Name}' is not currently active for reservations." });
            }

            if (request.ExpectedAttendance > venue.Capacity)
            {
                return Results.BadRequest(new
                {
                    message = $"Expected attendance ({request.ExpectedAttendance}) exceeds maximum venue capacity ({venue.Capacity})."
                });
            }

            // Invariant: If no venue manager is configured, show clearly and prevent submission
            if (venue.Managers == null || venue.Managers.Count == 0)
            {
                return Results.BadRequest(new
                {
                    message = $"No venue manager is configured for '{venue.Name}'. Booking requests cannot be submitted until facility administration assigns a manager."
                });
            }

            // Idempotency check: if an active request with the same idempotency key exists, return it
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var existingByKey = await dbContext.VenueBookingRequests
                    .Include(r => r.Venue)
                    .Include(r => r.Requester)
                    .Include(r => r.ReviewedByUser)
                    .Include(r => r.Notifications)
                    .FirstOrDefaultAsync(r => r.IdempotencyKey == request.IdempotencyKey.Trim() && r.RequesterId == currentUserId.Value);

                if (existingByKey != null)
                {
                    return Results.Ok(MapToDto(existingByKey));
                }
            }

            // Server-side Availability Re-check
            var setupMinutes = request.SetupMinutes ?? venue.DefaultSetupMinutes;
            var cleanupMinutes = request.CleanupMinutes ?? venue.DefaultCleanupMinutes;

            var availRequest = new CheckAvailabilityRequest(
                EventStartUtc: request.EventStartUtc,
                EventEndUtc: request.EventEndUtc,
                SetupMinutes: setupMinutes,
                CleanupMinutes: cleanupMinutes
            );

            var availResult = await availabilityService.CheckAvailabilityAsync(venue, availRequest, dbContext);

            if (availResult.Status != AvailabilityStatus.Available)
            {
                // CRITICAL DIRECTIVE: If slot is unavailable or unverified, create NEITHER request NOR notification
                return Results.BadRequest(new
                {
                    message = availResult.Reason,
                    availabilityStatus = availResult.Status,
                    isBufferConflictOnly = availResult.ConflictDetails?.IsBufferConflictOnly ?? false,
                    conflictDetails = availResult.ConflictDetails
                });
            }

            var requester = await dbContext.Users.FindAsync(currentUserId.Value);
            var requesterEmail = requester?.Email ?? "coordinator@campus.internal";
            var requesterName = requester?.UserName;

            var bookingRequest = new VenueBookingRequest
            {
                Id = Guid.NewGuid(),
                VenueId = venueId,
                RequesterId = currentUserId.Value,
                EventTitle = request.EventTitle.Trim(),
                Description = request.Description?.Trim(),
                ExpectedAttendance = request.ExpectedAttendance,
                EventStartUtc = DateTime.SpecifyKind(request.EventStartUtc, DateTimeKind.Utc),
                EventEndUtc = DateTime.SpecifyKind(request.EventEndUtc, DateTimeKind.Utc),
                SetupMinutes = setupMinutes,
                CleanupMinutes = cleanupMinutes,
                Status = BookingRequestStatus.PendingReview,
                IdempotencyKey = request.IdempotencyKey?.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            var notifications = new List<BookingNotification>();

            foreach (var manager in venue.Managers)
            {
                var managerEmail = manager.User.Email ?? string.Empty;
                if (string.IsNullOrWhiteSpace(managerEmail)) continue;

                var subject = $"[CampusVenue] Action Required: New Booking Request for {venue.Name} - {bookingRequest.EventTitle}";
                var bodyHtml = $"""
                    <div style="font-family: sans-serif; line-height: 1.5;">
                        <h2>New Venue Booking Request</h2>
                        <p>A new booking request requires your review as the assigned manager for <strong>{venue.Name}</strong> ({venue.Code}).</p>
                        <ul>
                            <li><strong>Event Title:</strong> {bookingRequest.EventTitle}</li>
                            <li><strong>Requested By:</strong> {requesterEmail}</li>
                            <li><strong>Event Interval:</strong> {bookingRequest.EventStartUtc:yyyy-MM-dd HH:mm} to {bookingRequest.EventEndUtc:yyyy-MM-dd HH:mm} UTC</li>
                            <li><strong>Occupied Interval (with buffers):</strong> {availResult.OccupiedStartUtc:yyyy-MM-dd HH:mm} to {availResult.OccupiedEndUtc:yyyy-MM-dd HH:mm} UTC</li>
                            <li><strong>Expected Attendance:</strong> {bookingRequest.ExpectedAttendance} (Max Capacity: {venue.Capacity})</li>
                        </ul>
                        <p>Please log in to CampusVenue to review, approve with conditions, or reject this request.</p>
                    </div>
                    """;

                var notification = new BookingNotification
                {
                    Id = Guid.NewGuid(),
                    BookingRequestId = bookingRequest.Id,
                    RecipientUserId = manager.UserId,
                    RecipientEmail = managerEmail,
                    NotificationType = "ManagerReviewRequired",
                    Subject = subject,
                    BodyHtml = bodyHtml,
                    SentAtUtc = DateTime.UtcNow,
                    CreatedAtUtc = DateTime.UtcNow
                };

                notifications.Add(notification);
            }

            // ATOMIC SAVE: Request and notification records saved in one single database transaction
            var strategy = dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync();
                dbContext.VenueBookingRequests.Add(bookingRequest);
                dbContext.BookingNotifications.AddRange(notifications);
                await dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            });

            // Asynchronously dispatch email to local mailbox
            foreach (var n in notifications)
            {
                try
                {
                    await emailSender.SendEmailAsync(n.RecipientEmail, n.Subject, n.BodyHtml);
                }
                catch
                {
                    // Email logging already handled by DevMailboxService
                }
            }

            bookingRequest.Venue = venue;
            bookingRequest.Requester = requester!;
            bookingRequest.Notifications = notifications;

            var dto = MapToDto(bookingRequest);
            return Results.Created($"/api/booking-requests/{bookingRequest.Id}", dto);
        })
        .WithName("SubmitBookingRequest")
        .WithSummary("Submit a new booking request for an available venue")
        .Produces<BookingRequestDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // 2. My Booking Requests (Role: Authenticated Coordinator)
        group.MapGet("/booking-requests/my", async (
            CampusVenueDbContext dbContext,
            HttpContext httpContext) =>
        {
            var currentUserId = GetCurrentUserId(httpContext.User);
            if (!currentUserId.HasValue)
            {
                return Results.Unauthorized();
            }

            var requests = await dbContext.VenueBookingRequests
                .Include(r => r.Venue)
                .Include(r => r.Requester)
                .Include(r => r.ReviewedByUser)
                .Include(r => r.Notifications)
                .Where(r => r.RequesterId == currentUserId.Value)
                .OrderByDescending(r => r.CreatedAtUtc)
                .AsNoTracking()
                .ToListAsync();

            var dtos = requests.Select(MapToDto).ToList();
            return Results.Ok(dtos);
        })
        .WithName("GetMyBookingRequests")
        .WithSummary("Get booking requests submitted by current user")
        .Produces<List<BookingRequestDto>>(StatusCodes.Status200OK);

        // 3. Manager Review Queue (Role: FacilityAdmin or UniversityAdmin with MFA)
        group.MapGet("/booking-requests/review-queue", async (
            CampusVenueDbContext dbContext,
            HttpContext httpContext,
            IAuthorizationService authorizationService) =>
        {
            var user = httpContext.User;
            var isUniAdminResult = await authorizationService.AuthorizeAsync(user, AuthorizationPolicies.UniversityAdminWithMfa);
            var isUniAdmin = isUniAdminResult.Succeeded;
            var isFacilityAdmin = user.IsInRole(CampusRoles.FacilityAdmin);

            if (!isUniAdmin && !isFacilityAdmin)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "Only assigned facility managers or university administrators can view the review queue."
                );
            }

            var currentUserId = GetCurrentUserId(user);
            if (!currentUserId.HasValue)
            {
                return Results.Unauthorized();
            }

            var query = dbContext.VenueBookingRequests
                .Include(r => r.Venue)
                .Include(r => r.Requester)
                .Include(r => r.ReviewedByUser)
                .Include(r => r.Notifications)
                .AsNoTracking();

            if (!isUniAdmin)
            {
                // FacilityAdmin sees requests for venues they manage
                var managedVenueIds = await dbContext.VenueManagers
                    .Where(vm => vm.UserId == currentUserId.Value)
                    .Select(vm => vm.VenueId)
                    .ToListAsync();

                query = query.Where(r => managedVenueIds.Contains(r.VenueId));
            }

            var requests = await query
                .OrderByDescending(r => r.CreatedAtUtc)
                .ToListAsync();

            var dtos = requests.Select(MapToDto).ToList();
            return Results.Ok(dtos);
        })
        .WithName("GetBookingReviewQueue")
        .WithSummary("Get booking requests in the review queue for authorized managers")
        .Produces<List<BookingRequestDto>>(StatusCodes.Status200OK);

        // 4. Venue Booking Requests (Role: FacilityAdmin for venue or UniversityAdmin with MFA)
        group.MapGet("/venues/{venueId:guid}/booking-requests", async (
            Guid venueId,
            CampusVenueDbContext dbContext,
            HttpContext httpContext,
            IAuthorizationService authorizationService) =>
        {
            if (!await CanManageVenueAsync(httpContext.User, venueId, dbContext, authorizationService))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "You are not authorized to view booking requests for this venue."
                );
            }

            var requests = await dbContext.VenueBookingRequests
                .Include(r => r.Venue)
                .Include(r => r.Requester)
                .Include(r => r.ReviewedByUser)
                .Include(r => r.Notifications)
                .Where(r => r.VenueId == venueId)
                .OrderByDescending(r => r.CreatedAtUtc)
                .AsNoTracking()
                .ToListAsync();

            var dtos = requests.Select(MapToDto).ToList();
            return Results.Ok(dtos);
        })
        .WithName("GetVenueBookingRequests")
        .WithSummary("Get booking requests for a specific venue")
        .Produces<List<BookingRequestDto>>(StatusCodes.Status200OK);

        // 5. Review Booking Request (Role: FacilityAdmin for venue or UniversityAdmin with MFA)
        group.MapPost("/booking-requests/{id:guid}/review", async (
            Guid id,
            ReviewBookingRequest request,
            CampusVenueDbContext dbContext,
            IEmailSender emailSender,
            HttpContext httpContext,
            IAuthorizationService authorizationService) =>
        {
            var currentUserId = GetCurrentUserId(httpContext.User);
            if (!currentUserId.HasValue)
            {
                return Results.Unauthorized();
            }

            var bookingRequest = await dbContext.VenueBookingRequests
                .Include(r => r.Venue)
                .Include(r => r.Requester)
                .Include(r => r.Notifications)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (bookingRequest == null)
            {
                return Results.NotFound(new { message = $"Booking request with ID '{id}' was not found." });
            }

            if (!await CanManageVenueAsync(httpContext.User, bookingRequest.VenueId, dbContext, authorizationService))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "You are not authorized to review requests for this venue."
                );
            }

            // CRITICAL SEGREGATION OF DUTIES INVARIANT:
            // If the same person is both coordinator and manager, require another authorized reviewer for their own request.
            if (bookingRequest.RequesterId == currentUserId.Value)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Segregation of Duties Violation",
                    detail: "Segregation of duties violation: You cannot review or approve your own booking request. Another authorized venue manager or university administrator must review it."
                );
            }

            if (bookingRequest.Status != BookingRequestStatus.PendingReview)
            {
                return Results.BadRequest(new
                {
                    message = $"Cannot review request in '{bookingRequest.Status}' status. Only '{BookingRequestStatus.PendingReview}' requests can be reviewed."
                });
            }

            string notificationType;
            string emailSubject;
            string emailBodyHtml;

            var decision = request.Decision.Trim();
            var reviewerUser = await dbContext.Users.FindAsync(currentUserId.Value);
            var reviewerEmail = reviewerUser?.Email ?? "manager@campus.internal";

            if (string.Equals(decision, "Approve", StringComparison.OrdinalIgnoreCase))
            {
                bookingRequest.Status = BookingRequestStatus.AwaitingConfirmation;
                bookingRequest.ReviewNotes = request.Notes?.Trim();
                bookingRequest.Conditions = null;
                bookingRequest.ConditionsAccepted = null;

                notificationType = "RequestApproved";
                emailSubject = $"[CampusVenue] Booking Request Approved: {bookingRequest.EventTitle}";
                emailBodyHtml = $"""
                    <div style="font-family: sans-serif; line-height: 1.5;">
                        <h2>Booking Request Approved</h2>
                        <p>Your booking request for <strong>{bookingRequest.Venue.Name}</strong> ({bookingRequest.EventTitle}) has been approved by {reviewerEmail}.</p>
                        <p>Status has transitioned to <strong>Awaiting Confirmation</strong>.</p>
                        {(string.IsNullOrWhiteSpace(bookingRequest.ReviewNotes) ? "" : $"<p><strong>Reviewer Notes:</strong> {bookingRequest.ReviewNotes}</p>")}
                    </div>
                    """;
            }
            else if (string.Equals(decision, "ApproveWithConditions", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(request.Conditions))
                {
                    return Results.BadRequest(new { message = "Conditions must be specified when approving with conditions." });
                }

                bookingRequest.Status = BookingRequestStatus.ApprovedWithConditions;
                bookingRequest.Conditions = request.Conditions.Trim();
                bookingRequest.ConditionsAccepted = null;
                bookingRequest.ReviewNotes = request.Notes?.Trim();

                notificationType = "ConditionsProposed";
                emailSubject = $"[CampusVenue] Conditions Proposed for Booking: {bookingRequest.EventTitle}";
                emailBodyHtml = $"""
                    <div style="font-family: sans-serif; line-height: 1.5;">
                        <h2>Booking Request Approved with Conditions</h2>
                        <p>Your booking request for <strong>{bookingRequest.Venue.Name}</strong> ({bookingRequest.EventTitle}) was approved subject to the following conditions:</p>
                        <blockquote style="background: #f3f4f6; padding: 12px; border-left: 4px solid #f59e0b;">
                            {bookingRequest.Conditions}
                        </blockquote>
                        <p>Please log in to CampusVenue to accept or decline these conditions to proceed.</p>
                    </div>
                    """;
            }
            else if (string.Equals(decision, "Reject", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(request.Notes))
                {
                    return Results.BadRequest(new { message = "A reason (notes) is mandatory when rejecting a booking request." });
                }

                bookingRequest.Status = BookingRequestStatus.Rejected;
                bookingRequest.ReviewNotes = request.Notes.Trim();
                bookingRequest.Conditions = null;
                bookingRequest.ConditionsAccepted = null;

                notificationType = "RequestRejected";
                emailSubject = $"[CampusVenue] Booking Request Rejected: {bookingRequest.EventTitle}";
                emailBodyHtml = $"""
                    <div style="font-family: sans-serif; line-height: 1.5;">
                        <h2>Booking Request Rejected</h2>
                        <p>Your booking request for <strong>{bookingRequest.Venue.Name}</strong> ({bookingRequest.EventTitle}) was rejected by {reviewerEmail}.</p>
                        <p><strong>Reason:</strong> {bookingRequest.ReviewNotes}</p>
                    </div>
                    """;
            }
            else
            {
                return Results.BadRequest(new { message = "Invalid decision. Must be 'Approve', 'ApproveWithConditions', or 'Reject'." });
            }

            bookingRequest.ReviewedByUserId = currentUserId.Value;
            bookingRequest.ReviewedAtUtc = DateTime.UtcNow;
            bookingRequest.UpdatedAtUtc = DateTime.UtcNow;

            var notification = new BookingNotification
            {
                Id = Guid.NewGuid(),
                BookingRequestId = bookingRequest.Id,
                RecipientUserId = bookingRequest.RequesterId,
                RecipientEmail = bookingRequest.Requester.Email ?? string.Empty,
                NotificationType = notificationType,
                Subject = emailSubject,
                BodyHtml = emailBodyHtml,
                SentAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            };

            var strategy = dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync();
                dbContext.BookingNotifications.Add(notification);
                await dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            });

            try
            {
                await emailSender.SendEmailAsync(notification.RecipientEmail, notification.Subject, notification.BodyHtml);
            }
            catch
            {
                // Caught and logged
            }

            bookingRequest.ReviewedByUser = reviewerUser;
            bookingRequest.Notifications.Add(notification);

            return Results.Ok(MapToDto(bookingRequest));
        })
        .WithName("ReviewBookingRequest")
        .WithSummary("Review a pending booking request (Approve, ApproveWithConditions, Reject)")
        .Produces<BookingRequestDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // 6. Respond to Conditions (Role: Coordinator who requested)
        group.MapPost("/booking-requests/{id:guid}/respond-conditions", async (
            Guid id,
            RespondConditionsRequest request,
            CampusVenueDbContext dbContext,
            IEmailSender emailSender,
            HttpContext httpContext) =>
        {
            var currentUserId = GetCurrentUserId(httpContext.User);
            if (!currentUserId.HasValue)
            {
                return Results.Unauthorized();
            }

            var bookingRequest = await dbContext.VenueBookingRequests
                .Include(r => r.Venue)
                .Include(r => r.Requester)
                .Include(r => r.ReviewedByUser)
                .Include(r => r.Notifications)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (bookingRequest == null)
            {
                return Results.NotFound(new { message = $"Booking request with ID '{id}' was not found." });
            }

            if (bookingRequest.RequesterId != currentUserId.Value && !httpContext.User.IsInRole(CampusRoles.UniversityAdmin))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "Only the coordinator who submitted this request can respond to conditions."
                );
            }

            if (bookingRequest.Status != BookingRequestStatus.ApprovedWithConditions)
            {
                return Results.BadRequest(new
                {
                    message = $"Cannot respond to conditions when request is in '{bookingRequest.Status}' status. Must be '{BookingRequestStatus.ApprovedWithConditions}'."
                });
            }

            string notificationType;
            string emailSubject;
            string emailBodyHtml;

            if (request.Accept)
            {
                // Accepted: transitions to AwaitingConfirmation
                bookingRequest.Status = BookingRequestStatus.AwaitingConfirmation;
                bookingRequest.ConditionsAccepted = true;
                bookingRequest.UpdatedAtUtc = DateTime.UtcNow;

                notificationType = "ConditionsAccepted";
                emailSubject = $"[CampusVenue] Conditions Accepted: {bookingRequest.EventTitle}";
                emailBodyHtml = $"""
                    <div style="font-family: sans-serif; line-height: 1.5;">
                        <h2>Approval Conditions Accepted</h2>
                        <p>Coordinator {bookingRequest.Requester.Email} has accepted the approval conditions for <strong>{bookingRequest.EventTitle}</strong> at <strong>{bookingRequest.Venue.Name}</strong>.</p>
                        <p>Request is now <strong>Awaiting Confirmation</strong>.</p>
                    </div>
                    """;
            }
            else
            {
                // Declined: transitions to Cancelled
                bookingRequest.Status = BookingRequestStatus.Cancelled;
                bookingRequest.ConditionsAccepted = false;
                bookingRequest.UpdatedAtUtc = DateTime.UtcNow;

                notificationType = "ConditionsDeclined";
                emailSubject = $"[CampusVenue] Conditions Declined - Request Cancelled: {bookingRequest.EventTitle}";
                emailBodyHtml = $"""
                    <div style="font-family: sans-serif; line-height: 1.5;">
                        <h2>Approval Conditions Declined</h2>
                        <p>Coordinator {bookingRequest.Requester.Email} has declined the approval conditions for <strong>{bookingRequest.EventTitle}</strong> at <strong>{bookingRequest.Venue.Name}</strong>.</p>
                        <p>The booking request has been cancelled.</p>
                    </div>
                    """;
            }

            var notificationList = new List<BookingNotification>();

            if (bookingRequest.ReviewedByUserId.HasValue)
            {
                var reviewer = await dbContext.Users.FindAsync(bookingRequest.ReviewedByUserId.Value);
                if (reviewer != null && !string.IsNullOrWhiteSpace(reviewer.Email))
                {
                    var notification = new BookingNotification
                    {
                        Id = Guid.NewGuid(),
                        BookingRequestId = bookingRequest.Id,
                        RecipientUserId = reviewer.Id,
                        RecipientEmail = reviewer.Email,
                        NotificationType = notificationType,
                        Subject = emailSubject,
                        BodyHtml = emailBodyHtml,
                        SentAtUtc = DateTime.UtcNow,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    notificationList.Add(notification);
                }
            }

            var strategy = dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync();
                if (notificationList.Count > 0)
                {
                    dbContext.BookingNotifications.AddRange(notificationList);
                }
                await dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            });

            foreach (var n in notificationList)
            {
                try
                {
                    await emailSender.SendEmailAsync(n.RecipientEmail, n.Subject, n.BodyHtml);
                }
                catch
                {
                    // Email logging handled
                }
            }

            foreach (var n in notificationList)
            {
                bookingRequest.Notifications.Add(n);
            }

            return Results.Ok(MapToDto(bookingRequest));
        })
        .WithName("RespondToConditions")
        .WithSummary("Accept or decline conditional approval conditions for a booking request")
        .Produces<BookingRequestDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static BookingRequestDto MapToDto(VenueBookingRequest r)
    {
        return new BookingRequestDto(
            r.Id,
            r.VenueId,
            r.Venue?.Name ?? string.Empty,
            r.Venue?.Code ?? string.Empty,
            r.RequesterId,
            r.Requester?.Email ?? string.Empty,
            r.Requester?.UserName,
            r.EventTitle,
            r.Description,
            r.ExpectedAttendance,
            r.EventStartUtc,
            r.EventEndUtc,
            r.SetupMinutes,
            r.CleanupMinutes,
            r.Status,
            r.ReviewNotes,
            r.Conditions,
            r.ConditionsAccepted,
            r.ReviewedByUserId,
            r.ReviewedByUser?.Email,
            r.ReviewedAtUtc,
            r.IdempotencyKey,
            r.CreatedAtUtc,
            r.UpdatedAtUtc,
            r.Notifications?.Select(n => new BookingNotificationDto(
                n.Id,
                n.BookingRequestId,
                n.RecipientUserId,
                n.RecipientEmail,
                n.NotificationType,
                n.Subject,
                n.SentAtUtc,
                n.CreatedAtUtc
            )).ToList() ?? []
        );
    }

    private static async Task<bool> CanManageVenueAsync(
        ClaimsPrincipal user,
        Guid venueId,
        CampusVenueDbContext dbContext,
        IAuthorizationService authorizationService)
    {
        var authResult = await authorizationService.AuthorizeAsync(user, AuthorizationPolicies.UniversityAdminWithMfa);
        if (authResult.Succeeded)
        {
            return true;
        }

        if (user.IsInRole(CampusRoles.FacilityAdmin))
        {
            var currentUserId = GetCurrentUserId(user);
            if (currentUserId.HasValue)
            {
                return await dbContext.VenueManagers
                    .AnyAsync(vm => vm.VenueId == venueId && vm.UserId == currentUserId.Value);
            }
        }

        return false;
    }

    private static Guid? GetCurrentUserId(ClaimsPrincipal user)
    {
        var idStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idStr, out var id) ? id : null;
    }
}
