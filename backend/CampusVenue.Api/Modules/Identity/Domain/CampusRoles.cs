namespace CampusVenue.Api.Modules.Identity.Domain;

/// <summary>
/// Institutional authorization roles for CampusVenue.
/// Mapped to ASP.NET Core Identity roles stored in the PostgreSQL 'roles' table.
/// </summary>
public static class CampusRoles
{
    public const string UniversityAdmin = "UniversityAdmin";
    public const string FacilityAdmin = "FacilityAdmin";
    public const string EventCoordinator = "EventCoordinator";
    public const string Participant = "Participant";

    public static readonly IReadOnlyList<string> All =
    [
        UniversityAdmin,
        FacilityAdmin,
        EventCoordinator,
        Participant
    ];

    public static string GetDescription(string role) => role switch
    {
        UniversityAdmin => "Institutional administrator with university-wide authority over spaces, users, and platform policies.",
        FacilityAdmin => "Facility manager with administrative control over designated campus buildings and spaces.",
        EventCoordinator => "Event coordinator authorized to reserve venues and organize approved university events.",
        Participant => "Standard university attendee with self-service registration and event participation access.",
        _ => "Standard institutional role."
    };
}
