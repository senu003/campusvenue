using CampusVenue.Api.Modules.Facilities.Domain;
using CampusVenue.Api.Modules.Identity.Domain;

namespace CampusVenue.Api.Modules.Reservations.Domain;

/// <summary>
/// Represents a formal booking request submitted by an Event Coordinator for a university venue.
/// Tracks review status, approval conditions, and assigned reviewer details.
/// </summary>
public class VenueBookingRequest
{
    public Guid Id { get; set; }

    public Guid VenueId { get; set; }
    public Venue Venue { get; set; } = null!;

    public Guid RequesterId { get; set; }
    public ApplicationUser Requester { get; set; } = null!;

    public string EventTitle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ExpectedAttendance { get; set; }

    public DateTime EventStartUtc { get; set; }
    public DateTime EventEndUtc { get; set; }

    public int SetupMinutes { get; set; }
    public int CleanupMinutes { get; set; }

    /// <summary>
    /// Current request state machine status (e.g., PendingReview, ApprovedWithConditions, AwaitingConfirmation, Rejected, Cancelled).
    /// </summary>
    public string Status { get; set; } = BookingRequestStatus.PendingReview;

    /// <summary>
    /// Notes or feedback provided by the facility reviewer (e.g., rejection explanation).
    /// </summary>
    public string? ReviewNotes { get; set; }

    /// <summary>
    /// Specific conditions imposed by the venue custodian upon conditional approval.
    /// </summary>
    public string? Conditions { get; set; }

    /// <summary>
    /// Indicates whether the coordinator has accepted (true), declined (false), or not yet responded (null) to conditions.
    /// </summary>
    public bool? ConditionsAccepted { get; set; }

    public Guid? ReviewedByUserId { get; set; }
    public ApplicationUser? ReviewedByUser { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    /// <summary>
    /// Client-supplied or generated idempotency token ensuring repeated submissions do not produce duplicate requests.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ConfirmedReservation? ConfirmedReservation { get; set; }

    public ICollection<BookingNotification> Notifications { get; set; } = new List<BookingNotification>();
}
