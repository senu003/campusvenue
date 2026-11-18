using Microsoft.AspNetCore.Identity;

namespace CampusVenue.Api.Modules.Identity.Domain;

/// <summary>
/// Domain entity representing an authorization role within the Identity module.
/// </summary>
public class ApplicationRole : IdentityRole<Guid>
{
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
