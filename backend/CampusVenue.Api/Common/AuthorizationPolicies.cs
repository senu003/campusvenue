namespace CampusVenue.Api.Common;

/// <summary>
/// Centralized authorization policy names for CampusVenue.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>
    /// Requires an authenticated user with the UniversityAdmin role and a session that completed MFA.
    /// </summary>
    public const string UniversityAdminWithMfa = "UniversityAdminWithMfa";
}
