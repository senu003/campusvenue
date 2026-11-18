using Microsoft.AspNetCore.Identity;

namespace CampusVenue.Api.Modules.Identity.Domain;

/// <summary>
/// Domain entity representing a university user account within the Identity module.
/// Uses Guid as primary key according to CampusVenue architectural standards.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
