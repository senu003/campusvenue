using CampusVenue.Api.Modules.Identity.Domain;

namespace CampusVenue.Api.Modules.Facilities.Domain;

/// <summary>
/// Represents a scheduled time block when a venue is occupied or unavailable due to
/// classes, exams, maintenance, or external/offline bookings.
/// </summary>
public class VenueCalendarBlock
{
    public Guid Id { get; set; }

    public Guid VenueId { get; set; }
    public Venue Venue { get; set; } = null!;

    /// <summary>
    /// Human-readable title or description of the block (e.g. "ME2030 Lecture", "Mid-Semester Examinations", "A/C Maintenance").
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Classification of block: Class, Exam, Maintenance, ExternalBooking, Other.
    /// </summary>
    public string BlockType { get; set; } = "Class";

    /// <summary>
    /// Start of blocked period in UTC.
    /// </summary>
    public DateTime StartUtc { get; set; }

    /// <summary>
    /// End of blocked period in UTC.
    /// </summary>
    public DateTime EndUtc { get; set; }

    /// <summary>
    /// Staff member who created or imported the block.
    /// </summary>
    public Guid CreatedByUserId { get; set; }
    public ApplicationUser CreatedByUser { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
