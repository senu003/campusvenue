namespace CampusVenue.Api.Modules.Reservations.Domain;

/// <summary>
/// Lifecycle statuses for a venue booking request.
/// </summary>
public static class BookingRequestStatus
{
    /// <summary>
    /// Initial state: Request submitted by coordinator, waiting for facility manager review.
    /// </summary>
    public const string PendingReview = "PendingReview";

    /// <summary>
    /// Facility manager approved the request subject to specific conditions.
    /// Requires coordinator review and explicit acceptance/declination.
    /// </summary>
    public const string ApprovedWithConditions = "ApprovedWithConditions";

    /// <summary>
    /// Request approved by facility manager without conditions, or conditions accepted by coordinator.
    /// Transitions into booking confirmation queue.
    /// </summary>
    public const string AwaitingConfirmation = "AwaitingConfirmation";

    /// <summary>
    /// Request confirmed by event coordinator. Confirmed reservation record created and calendar officially blocked.
    /// </summary>
    public const string Confirmed = "Confirmed";

    /// <summary>
    /// Request rejected by facility manager with a mandatory reason.
    /// </summary>
    public const string Rejected = "Rejected";

    /// <summary>
    /// Request cancelled by coordinator (e.g. after declining proposed conditions).
    /// </summary>
    public const string Cancelled = "Cancelled";
}
