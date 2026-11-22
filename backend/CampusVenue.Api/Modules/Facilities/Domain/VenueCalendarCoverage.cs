using CampusVenue.Api.Modules.Identity.Domain;

namespace CampusVenue.Api.Modules.Facilities.Domain;

/// <summary>
/// Records a verified period of calendar coverage for a venue.
/// A booking inquiry is only 'Available' or 'Unavailable' if an active, non-expired coverage
/// covers the requested date range. If coverage is missing, expired, or insufficient,
/// availability is 'Availability unverified'.
/// </summary>
public class VenueCalendarCoverage
{
    public Guid Id { get; set; }

    public Guid VenueId { get; set; }
    public Venue Venue { get; set; } = null!;

    /// <summary>
    /// Name of external timetable or booking system verified (e.g. "CITeS RBS Staff System (book.uom.lk)",
    /// "Undergraduate Timetable Portal (uomba.net)", "Department Master Schedule").
    /// </summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>
    /// Version, term, or reference identifier of the verified source (e.g. "Term 2 2026 / Academic Week 1-14").
    /// </summary>
    public string? SourceReference { get; set; }

    /// <summary>
    /// Start of verified coverage range in UTC.
    /// </summary>
    public DateTime CoversFromUtc { get; set; }

    /// <summary>
    /// End of verified coverage range in UTC.
    /// </summary>
    public DateTime CoversToUtc { get; set; }

    /// <summary>
    /// Staff member who performed the calendar audit / verification.
    /// </summary>
    public Guid VerifiedByUserId { get; set; }
    public ApplicationUser VerifiedByUser { get; set; } = null!;

    /// <summary>
    /// Timestamp when verification was conducted.
    /// </summary>
    public DateTime VerifiedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Operational notes or caveats regarding the verified calendar source.
    /// </summary>
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
