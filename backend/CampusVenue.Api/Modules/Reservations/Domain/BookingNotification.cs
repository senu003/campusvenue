using CampusVenue.Api.Modules.Identity.Domain;

namespace CampusVenue.Api.Modules.Reservations.Domain;

/// <summary>
/// Record of an email or in-app notification generated for booking request submission and review workflows.
/// Stored atomically with request state mutations.
/// </summary>
public class BookingNotification
{
    public Guid Id { get; set; }

    public Guid BookingRequestId { get; set; }
    public VenueBookingRequest BookingRequest { get; set; } = null!;

    public Guid RecipientUserId { get; set; }
    public ApplicationUser RecipientUser { get; set; } = null!;

    public string RecipientEmail { get; set; } = string.Empty;

    /// <summary>
    /// Type of notification (e.g. ManagerReviewRequired, ConditionsProposed, RequestApproved, RequestRejected, ConditionsAccepted, ConditionsDeclined).
    /// </summary>
    public string NotificationType { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;

    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
