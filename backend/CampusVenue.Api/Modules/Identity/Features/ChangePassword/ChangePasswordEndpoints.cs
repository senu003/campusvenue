using System.Security.Claims;
using CampusVenue.Api.Modules.Identity.Domain;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace CampusVenue.Api.Modules.Identity.Features.ChangePassword;

/// <summary>
/// Vertical slice exposing secure signed-in password change endpoint.
/// </summary>
public static class ChangePasswordEndpoints
{
    public static IEndpointRouteBuilder MapChangePasswordEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth")
            .WithTags("Authentication");

        group.MapPost("/change-password", async (
            ChangePasswordRequest request,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
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
            if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
                string.IsNullOrWhiteSpace(request.NewPassword) ||
                string.IsNullOrWhiteSpace(request.ConfirmPassword))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "Current password, new password, and confirmation password are required.");
            }

            if (request.NewPassword != request.ConfirmPassword)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "New password and confirmation password do not match.");
            }

            // Derive account from authenticated session
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user == null)
            {
                return Results.Unauthorized();
            }

            // Validate current password and update new password
            var changeResult = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (!changeResult.Succeeded)
            {
                var passwordMismatch = changeResult.Errors.Any(e =>
                    e.Code.Equals("PasswordMismatch", StringComparison.OrdinalIgnoreCase));

                if (passwordMismatch)
                {
                    logger.LogWarning("Failed password change attempt: incorrect current password for user ID {UserId}", user.Id);
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Password Change Failed",
                        detail: "The current password provided is incorrect.");
                }

                var policyErrors = string.Join(" ", changeResult.Errors.Select(e => e.Description));
                logger.LogWarning("Password change rejected due to policy errors for user ID {UserId}", user.Id);
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Password Policy Error",
                    detail: policyErrors);
            }

            // Sign out the current browser session
            await signInManager.SignOutAsync();
            context.User = new ClaimsPrincipal(new ClaimsIdentity());

            // Refresh antiforgery token for anonymous state
            var refreshedTokens = antiforgery.GetAndStoreTokens(context);

            logger.LogInformation("Password successfully changed and session terminated for user ID: {UserId}", user.Id);

            return Results.Ok(new ChangePasswordResponse(
                "Password has been successfully changed. Please sign in with your new password.",
                refreshedTokens.RequestToken!));
        })
        .WithName("ChangePassword")
        .WithSummary("Change password for authenticated user and invalidate session")
        .RequireAuthorization()
        .RequireRateLimiting("AuthLimiter")
        .Produces<ChangePasswordResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
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
