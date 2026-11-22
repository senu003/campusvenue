namespace CampusVenue.Api.Modules.Reservations.Features.BookingRequests;

public record BookingRequestDto(
    Guid Id,
    Guid VenueId,
    string VenueName,
    string VenueCode,
    Guid RequesterId,
    string RequesterEmail,
    string? RequesterName,
    string EventTitle,
    string? Description,
    int ExpectedAttendance,
    DateTime EventStartUtc,
    DateTime EventEndUtc,
    int SetupMinutes,
    int CleanupMinutes,
    string Status,
    string? ReviewNotes,
    string? Conditions,
    bool? ConditionsAccepted,
    Guid? ReviewedByUserId,
    string? ReviewedByEmail,
    DateTime? ReviewedAtUtc,
    string? IdempotencyKey,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<BookingNotificationDto> Notifications,
    string? ReservationReference = null,
    Guid? ConfirmedReservationId = null,
    DateTime? ConfirmedAtUtc = null
);

public record BookingNotificationDto(
    Guid Id,
    Guid BookingRequestId,
    Guid RecipientUserId,
    string RecipientEmail,
    string NotificationType,
    string Subject,
    DateTime SentAtUtc,
    DateTime CreatedAtUtc
);

public record CreateBookingRequest(
    string EventTitle,
    string? Description,
    int ExpectedAttendance,
    DateTime EventStartUtc,
    DateTime EventEndUtc,
    int? SetupMinutes,
    int? CleanupMinutes,
    string? IdempotencyKey
);

public record ReviewBookingRequest(
    string Decision, // "Approve", "ApproveWithConditions", "Reject"
    string? Notes,
    string? Conditions
);

public record RespondConditionsRequest(
    bool Accept
);
