using System.ComponentModel.DataAnnotations;
using CampusVenue.Api.Infrastructure.Email;
using CampusVenue.Api.Modules.Identity.Domain;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace CampusVenue.Api.Modules.Identity.Features.PasswordRecovery;

/// <summary>
/// Vertical slice exposing secure password recovery request and reset endpoints.
/// </summary>
public static class PasswordRecoveryEndpoints
{
    private static readonly ApplicationUser DummyUser = new() { Id = Guid.Empty, UserName = "recovery_dummy@campusvenue.internal" };
    private static readonly PasswordHasher<ApplicationUser> DummyHasher = new();
    private static readonly string DummyHash = DummyHasher.HashPassword(DummyUser, "CampusVenueSecureDummy2026!");
    private static readonly EmailAddressAttribute EmailValidator = new();

    public const string GenericForgotPasswordMessage = "If this email address is registered and eligible, a password reset link has been sent. Please check your inbox.";

    public static IEndpointRouteBuilder MapPasswordRecoveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth")
            .WithTags("Authentication");

        // 1. Forgot Password Endpoint
        group.MapPost("/forgot-password", async (
            ForgotPasswordRequest request,
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            IAntiforgery antiforgery,
            IConfiguration configuration,
            HttpContext context,
            ILogger<ApplicationUser> logger) =>
        {
            SetNoCacheHeaders(context);

            // Antiforgery validation
            if (!await TryValidateAntiforgeryAsync(antiforgery, context))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Antiforgery Validation Failed",
                    detail: "The required CSRF header or cookie was missing or invalid.");
            }

            // Input validation
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "Email address is required.");
            }

            var trimmedEmail = request.Email.Trim();
            if (!EmailValidator.IsValid(trimmedEmail))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "A valid university email address is required.");
            }

            var user = await userManager.FindByEmailAsync(trimmedEmail);

            // Generic reset response: if user does not exist, return generic message to prevent account enumeration
            if (user == null)
            {
                DummyHasher.VerifyHashedPassword(DummyUser, DummyHash, "DummyPasswordAttempt123!");
                logger.LogInformation("Password recovery requested for candidate email address.");
                return Results.Ok(new ForgotPasswordResponse(GenericForgotPasswordMessage));
            }

            // Send reset message only for eligible confirmed accounts; return generic message if unconfirmed
            if (!await userManager.IsEmailConfirmedAsync(user))
            {
                DummyHasher.VerifyHashedPassword(DummyUser, DummyHash, "DummyPasswordAttempt123!");
                logger.LogInformation("Password recovery requested for unconfirmed account: {UserId}", user.Id);
                return Results.Ok(new ForgotPasswordResponse(GenericForgotPasswordMessage));
            }

            // Generate expiring password reset token via Identity
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var frontendUrl = configuration["FrontendUrl"] ?? "http://localhost:3000";
            // Use URL fragment for token to keep it out of the initial HTTP request sent to the server
            var resetUrl = $"{frontendUrl.TrimEnd('/')}/?tab=reset-password#userId={user.Id}&token={Uri.EscapeDataString(token)}";

            var emailHtml = $"""
                <div style="font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 8px; background: #ffffff;">
                    <div style="margin-bottom: 20px;">
                        <span style="font-size: 20px; font-weight: bold; color: #0284c7;">CampusVenue</span>
                    </div>
                    <h2 style="color: #0f172a; margin-top: 0; font-size: 18px;">Reset your password</h2>
                    <p style="color: #334155; line-height: 1.5; font-size: 14px;">
                        We received a request to reset the password for your CampusVenue university account. Click the button below to choose a new password:
                    </p>
                    <div style="margin: 28px 0;">
                        <a href="{resetUrl}" style="background-color: #0284c7; color: #ffffff; padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: bold; font-size: 14px; display: inline-block;">
                            Reset Password
                        </a>
                    </div>
                    <p style="color: #64748b; font-size: 13px; line-height: 1.4;">
                        If the button above does not work, copy and paste this link into your browser:
                    </p>
                    <p style="font-size: 12px; word-break: break-all; color: #0284c7; background: #f1f5f9; padding: 8px 12px; border-radius: 4px;">
                        <a href="{resetUrl}" style="color: #0284c7; text-decoration: underline;">{resetUrl}</a>
                    </p>
                    <p style="color: #64748b; font-size: 13px; line-height: 1.4; margin-top: 20px;">
                        For your security, this password reset link will expire soon and can only be used once. If you did not request a password reset, you can safely ignore this email; your account and password remain unchanged.
                    </p>
                    <hr style="border: none; border-top: 1px solid #e2e8f0; margin: 24px 0;" />
                    <p style="color: #94a3b8; font-size: 11px; margin: 0;">
                        CampusVenue University Venue Reservation &amp; Event Management Platform.
                    </p>
                </div>
                """;

            await emailSender.SendEmailAsync(user.Email!, "Reset your CampusVenue password", emailHtml);

            // Log security event without logging sensitive reset link or token
            logger.LogInformation("Password reset token generated and email dispatched for user ID: {UserId}", user.Id);

            return Results.Ok(new ForgotPasswordResponse(GenericForgotPasswordMessage));
        })
        .WithName("ForgotPassword")
        .WithSummary("Request a password reset link for a verified account")
        .RequireRateLimiting("AuthLimiter")
        .Produces<ForgotPasswordResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // 2. Reset Password Endpoint
        group.MapPost("/reset-password", async (
            ResetPasswordRequest request,
            UserManager<ApplicationUser> userManager,
            IAntiforgery antiforgery,
            HttpContext context,
            ILogger<ApplicationUser> logger) =>
        {
            SetNoCacheHeaders(context);

            // Antiforgery validation
            if (!await TryValidateAntiforgeryAsync(antiforgery, context))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Antiforgery Validation Failed",
                    detail: "The required CSRF header or cookie was missing or invalid.");
            }

            // Input validation
            if (request.UserId == Guid.Empty || string.IsNullOrWhiteSpace(request.Token))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Password Reset Failed",
                    detail: "Invalid or missing reset token parameters.");
            }

            if (string.IsNullOrWhiteSpace(request.NewPassword) || string.IsNullOrWhiteSpace(request.ConfirmPassword))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "New password and confirmation password are required.");
            }

            if (request.NewPassword != request.ConfirmPassword)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "Password and confirmation password do not match.");
            }

            var user = await userManager.FindByIdAsync(request.UserId.ToString());
            if (user == null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Password Reset Failed",
                    detail: "Invalid or expired password reset token.");
            }

            var resetResult = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
            if (!resetResult.Succeeded)
            {
                // Disclose password policy errors specifically so user can satisfy policy
                var passwordErrors = resetResult.Errors
                    .Where(e => e.Code.StartsWith("Password", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (passwordErrors.Count > 0)
                {
                    var policyDetails = string.Join(" ", passwordErrors.Select(e => e.Description));
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Password Policy Error",
                        detail: policyDetails);
                }

                logger.LogWarning("Password reset token verification failed for user ID: {UserId}", user.Id);
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Password Reset Failed",
                    detail: "Invalid or expired password reset token.");
            }

            // Reset failed access counters and any lockout upon successful reset
            await userManager.ResetAccessFailedCountAsync(user);
            if (await userManager.IsLockedOutAsync(user))
            {
                await userManager.SetLockoutEndDateAsync(user, null);
            }

            logger.LogInformation("Password reset successfully completed for user ID: {UserId}", user.Id);

            return Results.Ok(new ResetPasswordResponse("Password has been successfully reset. You may now sign in with your new password."));
        })
        .WithName("ResetPassword")
        .WithSummary("Reset account password using a valid reset token")
        .RequireRateLimiting("AuthLimiter")
        .Produces<ResetPasswordResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return endpoints;
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
}
