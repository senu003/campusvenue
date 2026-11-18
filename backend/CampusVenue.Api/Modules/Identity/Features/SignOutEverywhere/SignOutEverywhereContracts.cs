namespace CampusVenue.Api.Modules.Identity.Features.SignOutEverywhere;

/// <summary>
/// Response returned upon revoking all active sessions and signing out.
/// </summary>
public sealed record SignOutEverywhereResponse(
    string Message,
    string? AntiforgeryToken = null);
