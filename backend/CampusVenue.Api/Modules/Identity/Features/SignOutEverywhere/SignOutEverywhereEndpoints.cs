using System.Security.Claims;
using CampusVenue.Api.Modules.Identity.Domain;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace CampusVenue.Api.Modules.Identity.Features.SignOutEverywhere;

/// <summary>
/// Vertical slice exposing endpoint to revoke all active sessions across all devices.
/// </summary>
public static class SignOutEverywhereEndpoints
{
    public static IEndpointRouteBuilder MapSignOutEverywhereEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth")
            .WithTags("Authentication");

        group.MapPost("/sign-out-everywhere", async (
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

            // Derive account strictly from authenticated session
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

            // Revoke all sessions by rotating security stamp
            var updateStampResult = await userManager.UpdateSecurityStampAsync(user);
            if (!updateStampResult.Succeeded)
            {
                var errors = string.Join(" ", updateStampResult.Errors.Select(e => e.Description));
                logger.LogError("Failed to update security stamp for user ID {UserId}: {Errors}", user.Id, errors);
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Session Revocation Failed",
                    detail: "Unable to revoke active sessions at this time. Please try again later.");
            }

            // After update succeeds, sign out current browser
            await signInManager.SignOutAsync();
            context.User = new ClaimsPrincipal(new ClaimsIdentity());

            // Refresh antiforgery token for anonymous state
            var refreshedTokens = antiforgery.GetAndStoreTokens(context);

            logger.LogInformation("Security stamp rotated and current session terminated for user ID: {UserId}", user.Id);

            return Results.Ok(new SignOutEverywhereResponse(
                "You have been signed out from all devices successfully.",
                refreshedTokens.RequestToken));
        })
        .WithName("SignOutEverywhere")
        .WithSummary("Revoke all sessions across all devices for the authenticated user and sign out")
        .RequireAuthorization()
        .RequireRateLimiting("AuthLimiter")
        .Produces<SignOutEverywhereResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

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
