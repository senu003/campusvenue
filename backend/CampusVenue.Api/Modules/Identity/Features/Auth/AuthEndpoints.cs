using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CampusVenue.Api.Infrastructure.Email;
using CampusVenue.Api.Modules.Identity.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CampusVenue.Api.Modules.Identity.Features.Auth;

/// <summary>
/// Vertical slice exposing secure sign-in, registration, email confirmation, sign-out, session verification, and CSRF token endpoints.
/// </summary>
public static class AuthEndpoints
{
    private static readonly ApplicationUser DummyUser = new() { Id = Guid.Empty, UserName = "timing_dummy@campusvenue.internal" };
    private static readonly PasswordHasher<ApplicationUser> DummyHasher = new();
    private static readonly string DummyHash = DummyHasher.HashPassword(DummyUser, "CampusVenueSecureDummy2026!");
    private static readonly EmailAddressAttribute EmailValidator = new();

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth")
            .WithTags("Authentication");

        // 1. Antiforgery Token Issuance Endpoint
        group.MapGet("/antiforgery", (IAntiforgery antiforgery, HttpContext context) =>
        {
            SetNoCacheHeaders(context);
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new AntiforgeryTokenResponse(tokens.RequestToken!));
        })
        .WithName("GetAntiforgeryToken")
        .WithSummary("Retrieve a CSRF request verification token for SPA requests")
        .Produces<AntiforgeryTokenResponse>(StatusCodes.Status200OK);

        // 2. Sign-In Endpoint
        group.MapPost("/login", async (
            LoginRequest request,
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
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "Email and password are required.");
            }

            var user = await userManager.FindByEmailAsync(request.Email.Trim());

            // Constant-time defense: perform dummy password verification if user does not exist
            if (user == null)
            {
                DummyHasher.VerifyHashedPassword(DummyUser, DummyHash, request.Password);
                return InvalidCredentialsResult();
            }

            // Check if account is locked out
            if (await userManager.IsLockedOutAsync(user))
            {
                logger.LogWarning("Sign-in attempt for locked out account: {UserId}", user.Id);
                return InvalidCredentialsResult();
            }

            // Verify password and handle failure counts
            var passwordValid = await userManager.CheckPasswordAsync(user, request.Password);
            if (!passwordValid)
            {
                await userManager.AccessFailedAsync(user);
                logger.LogWarning("Failed sign-in attempt for account: {UserId}", user.Id);
                return InvalidCredentialsResult();
            }

            // Require email confirmation before allowing sign-in
            if (!await userManager.IsEmailConfirmedAsync(user))
            {
                logger.LogWarning("Sign-in rejected: email not confirmed for account: {UserId}", user.Id);
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Email Not Confirmed",
                    detail: "Please confirm your email address before signing in.");
            }

            // Reset lockout counters upon successful verification
            await userManager.ResetAccessFailedCountAsync(user);

            // If user has two-factor authentication enabled, initiate pending two-factor challenge
            if (await userManager.GetTwoFactorEnabledAsync(user))
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.Name, user.Id.ToString())
                };
                var identity = new ClaimsIdentity(claims, IdentityConstants.TwoFactorUserIdScheme);
                await context.SignInAsync(
                    IdentityConstants.TwoFactorUserIdScheme,
                    new ClaimsPrincipal(identity),
                    new AuthenticationProperties
                    {
                        IsPersistent = request.RememberMe,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
                    });

                var challengeTokens = antiforgery.GetAndStoreTokens(context);
                logger.LogInformation("Two-factor authentication challenge initiated for account: {UserId}", user.Id);

                return Results.Ok(new LoginResponse(
                    "Two-factor verification required",
                    null,
                    challengeTokens.RequestToken!,
                    RequiresTwoFactor: true));
            }

            // Issue same-origin HttpOnly cookie session
            await signInManager.SignInAsync(user, isPersistent: request.RememberMe);
            var principal = await signInManager.CreateUserPrincipalAsync(user);
            context.User = principal;

            // Refresh antiforgery token for new authenticated session state
            var refreshedTokens = antiforgery.GetAndStoreTokens(context);

            logger.LogInformation("User successfully authenticated: {UserId}", user.Id);

            return Results.Ok(new LoginResponse(
                "Sign-in successful",
                new UserDto(user.Id, user.Email!),
                refreshedTokens.RequestToken!));
        })
        .WithName("SignIn")
        .WithSummary("Authenticate with credentials and establish a secure session cookie")
        .RequireRateLimiting("AuthLimiter")
        .Produces<LoginResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // 3. Current User Endpoint
        group.MapGet("/me", async (
            HttpContext context,
            UserManager<ApplicationUser> userManager) =>
        {
            SetNoCacheHeaders(context);

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

            var roles = await userManager.GetRolesAsync(user);
            if (roles.Count == 0)
            {
                roles = [CampusRoles.Participant];
            }

            var mfaCompleted = context.User.HasClaim("amr", "mfa");

            return Results.Ok(new CurrentUserResponse(
                user.Id,
                user.Email ?? string.Empty,
                true,
                user.TwoFactorEnabled,
                roles.ToList(),
                mfaCompleted));
        })
        .WithName("GetCurrentUser")
        .WithSummary("Get authenticated user information from active session cookie")
        .RequireAuthorization()
        .Produces<CurrentUserResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        // 4. Sign-Out Endpoint
        group.MapPost("/logout", async (
            SignInManager<ApplicationUser> signInManager,
            IAntiforgery antiforgery,
            HttpContext context,
            ILogger<ApplicationUser> logger) =>
        {
            SetNoCacheHeaders(context);

            // Explicitly validate antiforgery token on logout
            if (!await TryValidateAntiforgeryAsync(antiforgery, context))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Antiforgery Validation Failed",
                    detail: "The required CSRF header or cookie was missing or invalid.");
            }

            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            await signInManager.SignOutAsync();
            await context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
            context.User = new ClaimsPrincipal(new ClaimsIdentity());

            if (!string.IsNullOrEmpty(userId))
            {
                logger.LogInformation("User signed out: {UserId}", userId);
            }

            // Issue fresh token pair for anonymous state
            var refreshedTokens = antiforgery.GetAndStoreTokens(context);

            return Results.Ok(new LogoutResponse("Sign-out successful", refreshedTokens.RequestToken!));
        })
        .WithName("SignOut")
        .WithSummary("Invalidate active session cookie and refresh antiforgery token")
        .Produces<LogoutResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // 5. Self-Service Registration Endpoint
        group.MapPost("/register", async (
            RegisterRequest request,
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
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "Email and password are required.");
            }

            var trimmedEmail = request.Email.Trim();
            if (!EmailValidator.IsValid(trimmedEmail))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "A valid university email address is required.");
            }

            if (request.Password != request.ConfirmPassword)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "Password and confirmation password do not match.");
            }

            var existingUser = await userManager.FindByEmailAsync(trimmedEmail);

            // Generic registration response for duplicate accounts (prevent user enumeration)
            if (existingUser != null)
            {
                // Constant-time dummy work to mitigate timing enumeration
                DummyHasher.HashPassword(DummyUser, request.Password);
                logger.LogInformation("Registration request processed for candidate email.");
                return Results.Ok(new RegisterResponse("If this email address is eligible, a confirmation link has been sent. Please check your inbox."));
            }

            // Create new ApplicationUser without any staff or administrator roles/claims
            var newUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = trimmedEmail,
                Email = trimmedEmail,
                EmailConfirmed = false, // Require confirmation before sign-in
                CreatedAtUtc = DateTime.UtcNow
            };

            var createResult = await userManager.CreateAsync(newUser, request.Password);
            if (!createResult.Succeeded)
            {
                var errors = string.Join(" ", createResult.Errors.Select(e => e.Description));
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Registration Failed",
                    detail: errors);
            }

            // Public registration preserves standard participant access and never grants elevated roles

            // Generate email confirmation token
            var token = await userManager.GenerateEmailConfirmationTokenAsync(newUser);
            var frontendUrl = configuration["FrontendUrl"] ?? "http://localhost:3000";
            var verificationUrl = $"{frontendUrl.TrimEnd('/')}/?tab=verify&userId={newUser.Id}&token={Uri.EscapeDataString(token)}";

            var emailHtml = $"""
                <div style="font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 8px; background: #ffffff;">
                    <div style="margin-bottom: 20px;">
                        <span style="font-size: 20px; font-weight: bold; color: #0284c7;">CampusVenue</span>
                    </div>
                    <h2 style="color: #0f172a; margin-top: 0; font-size: 18px;">Verify your email address</h2>
                    <p style="color: #334155; line-height: 1.5; font-size: 14px;">
                        Thank you for creating an account with CampusVenue. Please confirm your email address to activate your account and start exploring university spaces.
                    </p>
                    <div style="margin: 28px 0;">
                        <a href="{verificationUrl}" style="background-color: #0284c7; color: #ffffff; padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: bold; font-size: 14px; display: inline-block;">
                            Confirm Email Address
                        </a>
                    </div>
                    <p style="color: #64748b; font-size: 13px; line-height: 1.4;">
                        If the button above does not work, copy and paste this link into your browser:
                    </p>
                    <p style="font-size: 12px; word-break: break-all; color: #0284c7; background: #f1f5f9; padding: 8px 12px; border-radius: 4px;">
                        <a href="{verificationUrl}" style="color: #0284c7; text-decoration: underline;">{verificationUrl}</a>
                    </p>
                    <hr style="border: none; border-top: 1px solid #e2e8f0; margin: 24px 0;" />
                    <p style="color: #94a3b8; font-size: 11px; margin: 0;">
                        CampusVenue University Venue Reservation & Event Management Platform. If you did not create this account, you can safely ignore this email.
                    </p>
                </div>
                """;

            await emailSender.SendEmailAsync(newUser.Email, "Verify your CampusVenue account", emailHtml);

            // Log security event without logging verification link or token
            logger.LogInformation("Registration completed and confirmation email sent for user ID: {UserId}", newUser.Id);

            return Results.Ok(new RegisterResponse("If this email address is eligible, a confirmation link has been sent. Please check your inbox."));
        })
        .WithName("Register")
        .WithSummary("Register a new user account and dispatch verification email")
        .RequireRateLimiting("AuthLimiter")
        .Produces<RegisterResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // 6. Confirm Email Endpoint (POST for SPA JSON requests)
        group.MapPost("/confirm-email", async (
            ConfirmEmailRequest request,
            UserManager<ApplicationUser> userManager,
            ILogger<ApplicationUser> logger) =>
        {
            if (request.UserId == Guid.Empty || string.IsNullOrWhiteSpace(request.Token))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Email Confirmation Failed",
                    detail: "Invalid verification link parameters.");
            }

            var user = await userManager.FindByIdAsync(request.UserId.ToString());
            if (user == null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Email Confirmation Failed",
                    detail: "Invalid or expired confirmation token.");
            }

            if (user.EmailConfirmed)
            {
                return Results.Ok(new ConfirmEmailResponse(true, "Email has already been confirmed. You can now sign in."));
            }

            var confirmResult = await userManager.ConfirmEmailAsync(user, request.Token);
            if (!confirmResult.Succeeded)
            {
                logger.LogWarning("Email confirmation token verification failed for user ID: {UserId}", user.Id);
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Email Confirmation Failed",
                    detail: "Invalid or expired confirmation token.");
            }

            logger.LogInformation("Email confirmed successfully for user ID: {UserId}", user.Id);
            return Results.Ok(new ConfirmEmailResponse(true, "Email confirmed successfully. You can now sign in."));
        })
        .WithName("ConfirmEmailPost")
        .WithSummary("Confirm email address with confirmation token")
        .Produces<ConfirmEmailResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        // 7. Confirm Email Endpoint (GET for direct browser links)
        group.MapGet("/confirm-email", async (
            [FromQuery] Guid userId,
            [FromQuery] string token,
            UserManager<ApplicationUser> userManager,
            ILogger<ApplicationUser> logger) =>
        {
            if (userId == Guid.Empty || string.IsNullOrWhiteSpace(token))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Email Confirmation Failed",
                    detail: "Invalid verification link parameters.");
            }

            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user == null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Email Confirmation Failed",
                    detail: "Invalid or expired confirmation token.");
            }

            if (user.EmailConfirmed)
            {
                return Results.Ok(new ConfirmEmailResponse(true, "Email has already been confirmed. You can now sign in."));
            }

            var confirmResult = await userManager.ConfirmEmailAsync(user, token);
            if (!confirmResult.Succeeded)
            {
                logger.LogWarning("Email confirmation token verification failed for user ID: {UserId}", user.Id);
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Email Confirmation Failed",
                    detail: "Invalid or expired confirmation token.");
            }

            logger.LogInformation("Email confirmed successfully for user ID: {UserId}", user.Id);
            return Results.Ok(new ConfirmEmailResponse(true, "Email confirmed successfully. You can now sign in."));
        })
        .WithName("ConfirmEmailGet")
        .WithSummary("Confirm email address via GET link")
        .Produces<ConfirmEmailResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

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

    private static IResult InvalidCredentialsResult()
    {
        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Authentication Failed",
            detail: "Invalid email or password.");
    }
}
