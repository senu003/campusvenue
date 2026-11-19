namespace CampusVenue.Api.Modules.Identity.Domain;

/// <summary>
/// Immutable audit record capturing administrative role assignments and revocations.
/// Saved within the same PostgreSQL transaction as role and security stamp modifications.
/// </summary>
public class RoleAssignmentAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ActorId { get; set; }
    public ApplicationUser? Actor { get; set; }

    public Guid TargetUserId { get; set; }
    public ApplicationUser? TargetUser { get; set; }

    public List<string> PreviousRoles { get; set; } = [];
    public List<string> NewRoles { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
