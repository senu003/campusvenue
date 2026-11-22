namespace CampusVenue.Api.Modules.Facilities.Domain;

/// <summary>
/// Represents a university venue (auditorium, hall, lecture room, seminar room, or lab)
/// that can be booked for academic, administrative, or student events.
/// </summary>
public class Venue
{
    public Guid Id { get; set; }

    /// <summary>
    /// Human-readable venue name (e.g., "Civil Auditorium (J.G. Hall)", "Faculty of Business Room 101").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Unique institutional code identifier (e.g., "JG-HALL", "FOB-101").
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Type of venue: Central (campus-wide) or Departmental (locally managed).
    /// </summary>
    public string Type { get; set; } = "Central";

    /// <summary>
    /// Institutional administrative unit managing this venue (e.g., "General Administration / Student Welfare").
    /// </summary>
    public string ManagingUnit { get; set; } = string.Empty;

    /// <summary>
    /// Maximum seating / attendee capacity.
    /// </summary>
    public int Capacity { get; set; }

    /// <summary>
    /// Daily operating hours start time in local university time (e.g. 08:00).
    /// </summary>
    public TimeOnly OperatingHoursStart { get; set; } = new(8, 0);

    /// <summary>
    /// Daily operating hours end time in local university time (e.g. 20:00).
    /// </summary>
    public TimeOnly OperatingHoursEnd { get; set; } = new(20, 0);

    /// <summary>
    /// Minimum advance notice required in hours prior to the event (e.g., 72 hours for central, 24 for department).
    /// </summary>
    public int MinNoticeHours { get; set; } = 72;

    /// <summary>
    /// Default required setup buffer before event start in minutes (e.g., 45 minutes).
    /// </summary>
    public int DefaultSetupMinutes { get; set; } = 45;

    /// <summary>
    /// Default required cleanup/teardown buffer after event end in minutes (e.g., 30 minutes).
    /// </summary>
    public int DefaultCleanupMinutes { get; set; } = 30;

    /// <summary>
    /// Maximum allowable booking duration for a single reservation in hours (e.g., 8 hours).
    /// </summary>
    public int MaxBookingHours { get; set; } = 8;

    /// <summary>
    /// Indicates whether student events in this venue require explicit Student Welfare division approval.
    /// </summary>
    public bool RequiresEventApproval { get; set; } = true;

    /// <summary>
    /// General venue-use rules, policies, noise restrictions, and cleanup guidelines displayed to coordinators.
    /// </summary>
    public string? GeneralRules { get; set; }

    /// <summary>
    /// Whether this venue is active and open for booking inquiries.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation collections
    public ICollection<VenueManager> Managers { get; set; } = new List<VenueManager>();
    public ICollection<VenueCalendarCoverage> CalendarCoverages { get; set; } = new List<VenueCalendarCoverage>();
    public ICollection<VenueCalendarBlock> CalendarBlocks { get; set; } = new List<VenueCalendarBlock>();
}
