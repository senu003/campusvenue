namespace CampusVenue.Api.Modules.Identity.Features.ChangePassword;

/// <summary>
/// Data contracts for signed-in password change requests and responses.
/// </summary>
public record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword);

public record ChangePasswordResponse(
    string Message,
    string AntiforgeryToken);
