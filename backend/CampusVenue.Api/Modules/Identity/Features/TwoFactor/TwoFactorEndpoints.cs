using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CampusVenue.Api.Modules.Identity.Domain;
using CampusVenue.Api.Modules.Identity.Features.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using QRCoder;

namespace CampusVenue.Api.Modules.Identity.Features.TwoFactor;

/// <summary>
/// Vertical slice exposing two-factor authentication (MFA) enrollment, status, verification, challenge, and recovery codes endpoints.
/// </summary>
public static class TwoFactorEndpoints
{
    public static IEndpointRouteBuilder MapTwoFactorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth/2fa")
            .WithTags("Two-Factor Authentication");

        // 1. Two-Factor Status Endpoint
        group.MapGet("/status", async (
            UserManager<ApplicationUser> userManager,
            HttpContext context) =>
        {
            SetNoCacheHeaders(context);

            var userId = GetCurrentUserId(context);
            if (userId == null)
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByIdAsync(userId.Value.ToString());
            if (user == null)
            {
                return Results.Unauthorized();
            }

            var isTwoFactorEnabled = await userManager.GetTwoFactorEnabledAsync(user);
            var key = await userManager.GetAuthenticatorKeyAsync(user);
            var hasAuthenticator = !string.IsNullOrEmpty(key);
            var recoveryCodesLeft = isTwoFactorEnabled ? await userManager.CountRecoveryCodesAsync(user) : 0;

            return Results.Ok(new TwoFactorStatusResponse(
                isTwoFactorEnabled,
                hasAuthenticator,
                recoveryCodesLeft));
        })
        .WithName("GetTwoFactorStatus")
        .WithSummary("Retrieve current two-factor authentication enrollment status and recovery code count")
        .RequireAuthorization()
        .Produces<TwoFactorStatusResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        // 2. Two-Factor Setup Key & Local QR Code Endpoint
        group.MapGet("/setup", async (
            UserManager<ApplicationUser> userManager,
            UrlEncoder urlEncoder,
            HttpContext context) =>
        {
            SetNoCacheHeaders(context);

            var userId = GetCurrentUserId(context);
            if (userId == null)
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByIdAsync(userId.Value.ToString());
            if (user == null)
            {
                return Results.Unauthorized();
            }

            if (await userManager.GetTwoFactorEnabledAsync(user))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Two-Factor Already Enabled",
                    detail: "Two-factor authentication is already enabled for this account. Use My Account controls to manage settings.");
            }

            var unformattedKey = await userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(unformattedKey))
            {
                await userManager.ResetAuthenticatorKeyAsync(user);
                unformattedKey = await userManager.GetAuthenticatorKeyAsync(user);
            }

            var email = user.Email ?? user.UserName ?? "user@campusvenue.internal";
            var formattedKey = FormatKey(unformattedKey!);
            var authenticatorUri = GenerateAuthenticatorUri(urlEncoder, email, unformattedKey!);

            // Generate QR code SVG completely locally using QRCoder
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(authenticatorUri, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new SvgQRCode(qrCodeData);
            var qrCodeSvg = qrCode.GetGraphic(20);
            var qrCodeDataUrl = $"data:image/svg+xml;utf8,{Uri.EscapeDataString(qrCodeSvg)}";

            return Results.Ok(new TwoFactorSetupResponse(
                formattedKey,
                unformattedKey!,
                authenticatorUri,
                qrCodeSvg,
                qrCodeDataUrl));
        })
        .WithName("GetTwoFactorSetup")
        .WithSummary("Retrieve local QR code and manual setup key for authenticator enrollment")
        .RequireAuthorization()
        .Produces<TwoFactorSetupResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // 3. Enable Two-Factor Authentication Endpoint
        group.MapPost("/enable", async (
            EnableTwoFactorRequest request,
            UserManager<ApplicationUser> userManager,
            IAntiforgery antiforgery,
            HttpContext context,
            ILogger<ApplicationUser> logger) =>
        {
            SetNoCacheHeaders(context);

            if (!await TryValidateAntiforgeryAsync(antiforgery, context))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Antiforgery Validation Failed",
                    detail: "The required CSRF header or cookie was missing or invalid.");
            }

            var userId = GetCurrentUserId(context);
            if (userId == null)
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByIdAsync(userId.Value.ToString());
            if (user == null)
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "Current password is required to enable two-factor authentication.");
            }

            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "Verification code is required.");
            }

            // Verify current password
            var passwordValid = await userManager.CheckPasswordAsync(user, request.CurrentPassword);
            if (!passwordValid)
            {
                logger.LogWarning("Failed two-factor enablement: incorrect current password for user: {UserId}", user.Id);
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Credentials",
                    detail: "The current password provided is incorrect.");
            }

            // Verify TOTP authenticator code
            var cleanCode = request.Code.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
            var is2FaTokenValid = await userManager.VerifyTwoFactorTokenAsync(
                user, userManager.Options.Tokens.AuthenticatorTokenProvider, cleanCode);

            if (!is2FaTokenValid)
            {
                logger.LogWarning("Failed two-factor enablement: invalid authenticator code for user: {UserId}", user.Id);
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Verification Code",
                    detail: "The authenticator verification code is invalid.");
            }

            // Enable two-factor and generate 10 single-use recovery codes
            await userManager.SetTwoFactorEnabledAsync(user, true);
            var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

            logger.LogInformation("Two-factor authentication successfully enabled for user: {UserId}", user.Id);

            return Results.Ok(new EnableTwoFactorResponse(
                "Two-factor authentication has been successfully enabled.",
                recoveryCodes?.ToList() ?? []));
        })
        .WithName("EnableTwoFactor")
        .WithSummary("Verify current password and authenticator code to enable MFA and generate recovery codes")
        .RequireAuthorization()
        .RequireRateLimiting("AuthLimiter")
        .Produces<EnableTwoFactorResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // 4. Disable Two-Factor Authentication Endpoint
        group.MapPost("/disable", async (
            DisableTwoFactorRequest request,
            UserManager<ApplicationUser> userManager,
            IAntiforgery antiforgery,
            HttpContext context,
            ILogger<ApplicationUser> logger) =>
        {
            SetNoCacheHeaders(context);

            if (!await TryValidateAntiforgeryAsync(antiforgery, context))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Antiforgery Validation Failed",
                    detail: "The required CSRF header or cookie was missing or invalid.");
            }

            var userId = GetCurrentUserId(context);
            if (userId == null)
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByIdAsync(userId.Value.ToString());
            if (user == null)
            {
                return Results.Unauthorized();
            }

            if (!await userManager.GetTwoFactorEnabledAsync(user))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Two-Factor Not Enabled",
                    detail: "Two-factor authentication is not currently enabled for this account.");
            }

            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "Current password is required to disable two-factor authentication.");
            }

            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "Verification code or recovery code is required.");
            }

            // Verify current password
            var passwordValid = await userManager.CheckPasswordAsync(user, request.CurrentPassword);
            if (!passwordValid)
            {
                logger.LogWarning("Failed two-factor disablement: incorrect current password for user: {UserId}", user.Id);
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Credentials",
                    detail: "The current password provided is incorrect.");
            }

            // Verify active second factor (authenticator code OR valid recovery code)
            var cleanCode = request.Code.Trim().Replace(" ", string.Empty);
            var isTokenValid = await userManager.VerifyTwoFactorTokenAsync(
                user, userManager.Options.Tokens.AuthenticatorTokenProvider, cleanCode.Replace("-", string.Empty));

            if (!isTokenValid)
            {
                var redeemResult = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, cleanCode);
                isTokenValid = redeemResult.Succeeded;
            }

            if (!isTokenValid)
            {
                logger.LogWarning("Failed two-factor disablement: invalid second factor for user: {UserId}", user.Id);
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Verification Code",
                    detail: "The verification code or recovery code is invalid.");
            }

            // Disable two-factor and reset authenticator key
            await userManager.SetTwoFactorEnabledAsync(user, false);
            await userManager.ResetAuthenticatorKeyAsync(user);

            logger.LogInformation("Two-factor authentication successfully disabled for user: {UserId}", user.Id);

            return Results.Ok(new DisableTwoFactorResponse("Two-factor authentication has been disabled."));
        })
        .WithName("DisableTwoFactor")
        .WithSummary("Verify current password and active second factor to disable MFA")
        .RequireAuthorization()
        .RequireRateLimiting("AuthLimiter")
        .Produces<DisableTwoFactorResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // 5. Regenerate Recovery Codes Endpoint
        group.MapPost("/regenerate-recovery-codes", async (
            RegenerateRecoveryCodesRequest request,
            UserManager<ApplicationUser> userManager,
            IAntiforgery antiforgery,
            HttpContext context,
            ILogger<ApplicationUser> logger) =>
        {
            SetNoCacheHeaders(context);

            if (!await TryValidateAntiforgeryAsync(antiforgery, context))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Antiforgery Validation Failed",
                    detail: "The required CSRF header or cookie was missing or invalid.");
            }

            var userId = GetCurrentUserId(context);
            if (userId == null)
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByIdAsync(userId.Value.ToString());
            if (user == null)
            {
                return Results.Unauthorized();
            }

            if (!await userManager.GetTwoFactorEnabledAsync(user))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Two-Factor Not Enabled",
                    detail: "Two-factor authentication is not currently enabled for this account.");
            }

            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "Current password is required to regenerate recovery codes.");
            }

            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "Verification code or recovery code is required.");
            }

            // Verify current password
            var passwordValid = await userManager.CheckPasswordAsync(user, request.CurrentPassword);
            if (!passwordValid)
            {
                logger.LogWarning("Failed recovery codes regeneration: incorrect current password for user: {UserId}", user.Id);
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Credentials",
                    detail: "The current password provided is incorrect.");
            }

            // Verify active second factor (authenticator code OR valid recovery code)
            var cleanCode = request.Code.Trim().Replace(" ", string.Empty);
            var isTokenValid = await userManager.VerifyTwoFactorTokenAsync(
                user, userManager.Options.Tokens.AuthenticatorTokenProvider, cleanCode.Replace("-", string.Empty));

            if (!isTokenValid)
            {
                var redeemResult = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, cleanCode);
                isTokenValid = redeemResult.Succeeded;
            }

            if (!isTokenValid)
            {
                logger.LogWarning("Failed recovery codes regeneration: invalid second factor for user: {UserId}", user.Id);
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Verification Code",
                    detail: "The verification code or recovery code is invalid.");
            }

            // Generate 10 new recovery codes, invalidating previous set
            var newCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

            logger.LogInformation("Recovery codes successfully regenerated for user: {UserId}", user.Id);

            return Results.Ok(new RegenerateRecoveryCodesResponse(
                "New recovery codes generated. All previous recovery codes are now invalid.",
                newCodes?.ToList() ?? []));
        })
        .WithName("RegenerateRecoveryCodes")
        .WithSummary("Verify current password and active second factor to regenerate recovery codes and invalidate prior set")
        .RequireAuthorization()
        .RequireRateLimiting("AuthLimiter")
        .Produces<RegenerateRecoveryCodesResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // 6. Sign-In Two-Factor Challenge Endpoint
        group.MapPost("/challenge", async (
            TwoFactorChallengeRequest request,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IAntiforgery antiforgery,
            HttpContext context,
            ILogger<ApplicationUser> logger) =>
        {
            SetNoCacheHeaders(context);

            if (!await TryValidateAntiforgeryAsync(antiforgery, context))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Antiforgery Validation Failed",
                    detail: "The required CSRF header or cookie was missing or invalid.");
            }

            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "Verification code is required.");
            }

            var twoFactorUser = await signInManager.GetTwoFactorAuthenticationUserAsync();
            if (twoFactorUser == null)
            {
                logger.LogWarning("Two-factor challenge failed: no pending challenge cookie found.");
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Two-Factor Challenge Expired",
                    detail: "The two-factor authentication challenge has expired or is invalid. Please sign in again.");
            }

            SignInResult result;

            if (request.IsRecoveryCode)
            {
                var cleanCode = request.Code.Trim().Replace(" ", string.Empty);
                result = await signInManager.TwoFactorRecoveryCodeSignInAsync(cleanCode);
            }
            else
            {
                var cleanCode = request.Code.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
                result = await signInManager.TwoFactorAuthenticatorSignInAsync(
                    cleanCode,
                    isPersistent: request.RememberMe,
                    rememberClient: false);
            }

            if (result.Succeeded)
            {
                var principal = await signInManager.CreateUserPrincipalAsync(twoFactorUser);
                if (principal.Identity is ClaimsIdentity identity && !principal.HasClaim("amr", "mfa"))
                {
                    identity.AddClaim(new Claim("amr", "mfa"));
                }
                context.User = principal;
                var refreshedTokens = antiforgery.GetAndStoreTokens(context);

                logger.LogInformation("User successfully authenticated via two-factor challenge: {UserId}", twoFactorUser.Id);

                return Results.Ok(new LoginResponse(
                    "Sign-in successful",
                    new UserDto(twoFactorUser.Id, twoFactorUser.Email!),
                    refreshedTokens.RequestToken!));
            }

            if (result.IsLockedOut)
            {
                logger.LogWarning("Account locked out during two-factor challenge: {UserId}", twoFactorUser.Id);
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Account Locked Out",
                    detail: "This account is temporarily locked out due to multiple failed verification attempts. Please try again later.");
            }

            logger.LogWarning("Invalid two-factor verification attempt for user: {UserId}", twoFactorUser.Id);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Verification Code",
                detail: request.IsRecoveryCode
                    ? "The recovery code is invalid or has already been used."
                    : "The authenticator verification code is invalid.");
        })
        .WithName("TwoFactorChallenge")
        .WithSummary("Verify pending two-factor challenge with authenticator or recovery code to issue session cookie")
        .RequireRateLimiting("AuthLimiter")
        .Produces<LoginResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static Guid? GetCurrentUserId(HttpContext context)
    {
        var claim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out var userId))
        {
            return userId;
        }
        return null;
    }

    private static void SetNoCacheHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }

    private static async Task<bool> TryValidateAntiforgeryAsync(IAntiforgery antiforgery, HttpContext context)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static string FormatKey(string unformattedKey)
    {
        var result = new System.Text.StringBuilder();
        int currentPosition = 0;
        while (currentPosition + 4 < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }
        if (currentPosition < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition));
        }

        return result.ToString().ToLowerInvariant();
    }

    private static string GenerateAuthenticatorUri(UrlEncoder urlEncoder, string email, string unformattedKey)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
            urlEncoder.Encode("CampusVenue"),
            urlEncoder.Encode(email),
            unformattedKey);
    }
}
