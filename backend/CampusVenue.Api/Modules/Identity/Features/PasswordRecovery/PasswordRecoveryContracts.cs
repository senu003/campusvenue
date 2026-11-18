namespace CampusVenue.Api.Modules.Identity.Features.PasswordRecovery;

/// <summary>
/// Data contracts for password recovery request and reset operations.
/// </summary>
public record ForgotPasswordRequest(string Email);

public record ForgotPasswordResponse(string Message);

public record ResetPasswordRequest(Guid UserId, string Token, string NewPassword, string ConfirmPassword);

public record ResetPasswordResponse(string Message);
