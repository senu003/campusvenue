namespace CampusVenue.Api.Modules.Identity.Features.TwoFactor;

/// <summary>
/// Data contracts for Two-Factor Authentication (MFA) enrollment, verification, status, challenge, and recovery codes.
/// </summary>
public record TwoFactorStatusResponse(
    bool IsTwoFactorEnabled,
    bool HasAuthenticator,
    int RecoveryCodesLeft);

public record TwoFactorSetupResponse(
    string ManualEntryKey,
    string UnformattedKey,
    string AuthenticatorUri,
    string QrCodeSvg,
    string QrCodeDataUrl);

public record EnableTwoFactorRequest(
    string CurrentPassword,
    string Code);

public record EnableTwoFactorResponse(
    string Message,
    IReadOnlyList<string> RecoveryCodes);

public record DisableTwoFactorRequest(
    string CurrentPassword,
    string Code);

public record DisableTwoFactorResponse(
    string Message);

public record RegenerateRecoveryCodesRequest(
    string CurrentPassword,
    string Code);

public record RegenerateRecoveryCodesResponse(
    string Message,
    IReadOnlyList<string> RecoveryCodes);

public record TwoFactorChallengeRequest(
    string Code,
    bool IsRecoveryCode = false,
    bool RememberMe = false);
