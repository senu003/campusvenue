namespace CampusVenue.Api.Modules.Identity.Features.Auth;

/// <summary>
/// Data contracts for Identity authentication, registration, confirmation, and development inbox requests and responses.
/// </summary>
public record LoginRequest(string Email, string Password, bool RememberMe = false);

public record UserDto(Guid Id, string Email);

public record LoginResponse(string Message, UserDto? User, string AntiforgeryToken, bool RequiresTwoFactor = false);

public record CurrentUserResponse(Guid Id, string Email, bool IsAuthenticated, bool TwoFactorEnabled = false);

public record LogoutResponse(string Message, string AntiforgeryToken);

public record AntiforgeryTokenResponse(string RequestToken);

public record RegisterRequest(string Email, string Password, string ConfirmPassword);

public record RegisterResponse(string Message);

public record ConfirmEmailRequest(Guid UserId, string Token);

public record ConfirmEmailResponse(bool Succeeded, string Message);

public record DevEmailMessage(Guid Id, string To, string Subject, string BodyHtml, DateTime SentAtUtc);
