using CampusVenue.Api.Modules.Identity.Domain;

namespace CampusVenue.Api.Modules.Facilities.Domain;

/// <summary>
/// Scopes a facility staff member (FacilityAdmin) to manage calendar blocks and settings for a specific venue.
/// </summary>
public class VenueManager
{
    public Guid VenueId { get; set; }
    public Venue Venue { get; set; } = null!;

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
}
