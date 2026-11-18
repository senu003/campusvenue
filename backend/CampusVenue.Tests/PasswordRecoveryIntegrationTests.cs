using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Infrastructure.Email;
using CampusVenue.Api.Modules.Identity.Domain;
using CampusVenue.Api.Modules.Identity.Features.Auth;
using CampusVenue.Api.Modules.Identity.Features.PasswordRecovery;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CampusVenue.Tests;

public class ShortExpirationWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly TimeSpan _tokenLifespan;
    private readonly TimeSpan _stampValidationInterval;

    public ShortExpirationWebApplicationFactory(TimeSpan tokenLifespan, TimeSpan stampValidationInterval)
    {
        _tokenLifespan = tokenLifespan;
        _stampValidationInterval = stampValidationInterval;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            var descriptors = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<CampusVenueDbContext>) ||
                d.ServiceType == typeof(CampusVenueDbContext)).ToList();
            foreach (var d in descriptors)
            {
                services.Remove(d);
            }

            services.AddDbContext<CampusVenueDbContext>(options =>
            {
                options.UseNpgsql(TestDatabaseHelper.ConnectionString);
            });

            services.Configure<PasswordResetTokenProviderOptions>(options =>
            {
                options.TokenLifespan = _tokenLifespan;
            });

            services.Configure<DataProtectionTokenProviderOptions>(options =>
            {
                options.TokenLifespan = _tokenLifespan;
            });

            services.Configure<SecurityStampValidatorOptions>(options =>
            {
                options.ValidationInterval = _stampValidationInterval;
            });
        });

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDatabaseHelper.ConnectionString,
                ["RateLimiting:AuthPermitLimit"] = "200",
                ["DataProtection:KeyPath"] = Path.Combine(Path.GetTempPath(), "campusvenue_dpkeys_test")
            });
        });
    }
}

public class PasswordRecoveryIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PasswordRecoveryIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        TestDatabaseHelper.EnsureMigrated(factory.Services);
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    private async Task<(string RequestToken, string CookieHeader)> GetAntiforgeryTokensAsync()
    {
        var response = await _client.GetAsync("/api/auth/antiforgery");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var data = await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();
        data.Should().NotBeNull();
        data!.RequestToken.Should().NotBeNullOrWhiteSpace();

        var cookieHeader = string.Empty;
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            cookieHeader = string.Join("; ", setCookies.Select(c => c.Split(';')[0]));
        }

        return (data.RequestToken, cookieHeader);
    }

    private async Task<ApplicationUser> EnsureUserAsync(string email, string password, bool emailConfirmed = true)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        if (user != null)
        {
            user.EmailConfirmed = emailConfirmed;
            await userManager.UpdateAsync(user);
            return user;
        }

        user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = emailConfirmed,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(user, password);
        result.Succeeded.Should().BeTrue();
        return user;
    }

    private void ClearDevMailbox()
    {
        using var scope = _factory.Services.CreateScope();
        var mailbox = scope.ServiceProvider.GetRequiredService<DevMailboxService>();
        mailbox.Clear();
    }

    [Fact]
    public async Task ForgotPassword_MissingAntiforgeryToken_Returns400BadRequest()
    {
        // Arrange
        var request = new ForgotPasswordRequest("student_forgot_1@campus.internal");

        // Act: POST without X-XSRF-TOKEN header
        var response = await _client.PostAsJsonAsync("/api/auth/forgot-password", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Antiforgery Validation Failed");
    }

    [Fact]
    public async Task ForgotPassword_InvalidEmailFormat_Returns400BadRequest()
    {
        // Arrange
        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password")
        {
            Content = JsonContent.Create(new ForgotPasswordRequest("not-an-email-address"))
        };
        req.Headers.Add("X-XSRF-TOKEN", csrfToken);
        if (!string.IsNullOrEmpty(cookie)) req.Headers.Add("Cookie", cookie);

        // Act
        var response = await _client.SendAsync(req);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Validation Error");
        problem.GetProperty("detail").GetString().Should().Contain("email address");
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_ReturnsGeneric200_AndSendsNoEmail()
    {
        // Arrange
        ClearDevMailbox();
        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync();
        var unknownEmail = $"unknown_user_{Guid.NewGuid():N}@campus.internal";
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password")
        {
            Content = JsonContent.Create(new ForgotPasswordRequest(unknownEmail))
        };
        req.Headers.Add("X-XSRF-TOKEN", csrfToken);
        if (!string.IsNullOrEmpty(cookie)) req.Headers.Add("Cookie", cookie);

        // Act
        var response = await _client.SendAsync(req);

        // Assert: Same generic message returned (prevents account enumeration)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(PasswordRecoveryEndpoints.GenericForgotPasswordMessage);

        // Mailbox should NOT contain any emails for this unknown address
        using var scope = _factory.Services.CreateScope();
        var mailbox = scope.ServiceProvider.GetRequiredService<DevMailboxService>();
        mailbox.GetMessages().Any(m => m.To.Equals(unknownEmail, StringComparison.OrdinalIgnoreCase)).Should().BeFalse();
    }

    [Fact]
    public async Task ForgotPassword_UnconfirmedUser_ReturnsGeneric200_AndSendsNoEmail()
    {
        // Arrange
        ClearDevMailbox();
        var unconfirmedEmail = $"unconfirmed_{Guid.NewGuid():N}@campus.internal";
        await EnsureUserAsync(unconfirmedEmail, "Password123!", emailConfirmed: false);

        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password")
        {
            Content = JsonContent.Create(new ForgotPasswordRequest(unconfirmedEmail))
        };
        req.Headers.Add("X-XSRF-TOKEN", csrfToken);
        if (!string.IsNullOrEmpty(cookie)) req.Headers.Add("Cookie", cookie);

        // Act
        var response = await _client.SendAsync(req);

        // Assert: Same generic message returned
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(PasswordRecoveryEndpoints.GenericForgotPasswordMessage);

        // Mailbox should NOT contain any emails for this unconfirmed address
        using var scope = _factory.Services.CreateScope();
        var mailbox = scope.ServiceProvider.GetRequiredService<DevMailboxService>();
        mailbox.GetMessages().Any(m => m.To.Equals(unconfirmedEmail, StringComparison.OrdinalIgnoreCase)).Should().BeFalse();
    }

    [Fact]
    public async Task ForgotPassword_ConfirmedUser_DispatchesResetEmail_WithValidTokenInFragment()
    {
        // Arrange
        ClearDevMailbox();
        var confirmedEmail = $"confirmed_{Guid.NewGuid():N}@campus.internal";
        var user = await EnsureUserAsync(confirmedEmail, "OldPassword123!", emailConfirmed: true);

        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password")
        {
            Content = JsonContent.Create(new ForgotPasswordRequest(confirmedEmail))
        };
        req.Headers.Add("X-XSRF-TOKEN", csrfToken);
        if (!string.IsNullOrEmpty(cookie)) req.Headers.Add("Cookie", cookie);

        // Act
        var response = await _client.SendAsync(req);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(PasswordRecoveryEndpoints.GenericForgotPasswordMessage);

        // Verify email in DevMailbox
        using var scope = _factory.Services.CreateScope();
        var mailbox = scope.ServiceProvider.GetRequiredService<DevMailboxService>();
        var sentEmail = mailbox.GetMessages().FirstOrDefault(m => m.To.Equals(confirmedEmail, StringComparison.OrdinalIgnoreCase));
        sentEmail.Should().NotBeNull();
        sentEmail!.Subject.Should().Be("Reset your CampusVenue password");

        // Verify that the link uses a URL fragment (#userId=...&token=...) so the token is kept out of the initial HTTP request
        sentEmail.BodyHtml.Should().Contain($"tab=reset-password#userId={user.Id}&token=");

        // Token should not be present in response body
        var responseString = await response.Content.ReadAsStringAsync();
        responseString.Should().NotContain("token");
    }

    [Fact]
    public async Task ResetPassword_MissingAntiforgeryToken_Returns400BadRequest()
    {
        // Arrange
        var request = new ResetPasswordRequest(Guid.NewGuid(), "some-token", "NewPassword123!", "NewPassword123!");

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/reset-password", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Antiforgery Validation Failed");
    }

    [Fact]
    public async Task ResetPassword_MismatchedPasswords_Returns400BadRequest()
    {
        // Arrange
        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordRequest(Guid.NewGuid(), "some-token", "NewPass123!", "DifferentPass123!"))
        };
        req.Headers.Add("X-XSRF-TOKEN", csrfToken);
        if (!string.IsNullOrEmpty(cookie)) req.Headers.Add("Cookie", cookie);

        // Act
        var response = await _client.SendAsync(req);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Validation Error");
        problem.GetProperty("detail").GetString().Should().Contain("do not match");
    }

    [Fact]
    public async Task ResetPassword_WeakPassword_Returns400BadRequest_WithPolicyViolation()
    {
        // Arrange
        var email = $"weakpass_{Guid.NewGuid():N}@campus.internal";
        var user = await EnsureUserAsync(email, "InitialPass123!", emailConfirmed: true);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordRequest(user.Id, token, "weak", "weak"))
        };
        req.Headers.Add("X-XSRF-TOKEN", csrfToken);
        if (!string.IsNullOrEmpty(cookie)) req.Headers.Add("Cookie", cookie);

        // Act
        var response = await _client.SendAsync(req);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Password Policy Error");
        problem.GetProperty("detail").GetString().Should().Contain("8");
    }

    [Fact]
    public async Task ResetPassword_InvalidOrExpiredToken_Returns400BadRequest()
    {
        // Arrange
        var email = $"invalidtoken_{Guid.NewGuid():N}@campus.internal";
        var user = await EnsureUserAsync(email, "InitialPass123!", emailConfirmed: true);

        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordRequest(user.Id, "invalid-token-value-xyz", "ValidNewPass123!", "ValidNewPass123!"))
        };
        req.Headers.Add("X-XSRF-TOKEN", csrfToken);
        if (!string.IsNullOrEmpty(cookie)) req.Headers.Add("Cookie", cookie);

        // Act
        var response = await _client.SendAsync(req);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Password Reset Failed");
        problem.GetProperty("detail").GetString().Should().Contain("Invalid or expired");
    }

    [Fact]
    public async Task ResetPassword_ValidToken_SuccessfullyChangesPassword_AndAllowsLoginWithNewPassword()
    {
        // Arrange: User with old password
        var email = $"successreset_{Guid.NewGuid():N}@campus.internal";
        const string oldPassword = "InitialPassword123!";
        const string newPassword = "BrandNewSecurePassword456!";
        var user = await EnsureUserAsync(email, oldPassword, emailConfirmed: true);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync();

        // Act 1: Submit reset request
        var resetReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordRequest(user.Id, token, newPassword, newPassword))
        };
        resetReq.Headers.Add("X-XSRF-TOKEN", csrfToken);
        if (!string.IsNullOrEmpty(cookie)) resetReq.Headers.Add("Cookie", cookie);

        var resetResponse = await _client.SendAsync(resetReq);

        // Assert 1: Reset successful
        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var resetBody = await resetResponse.Content.ReadFromJsonAsync<ResetPasswordResponse>();
        resetBody.Should().NotBeNull();
        resetBody!.Message.Should().Contain("successfully reset");

        // Act 2: Attempt login with OLD password -> MUST FAIL (401)
        var (csrfTokenLogin1, cookieLogin1) = await GetAntiforgeryTokensAsync();
        var loginOldReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, oldPassword))
        };
        loginOldReq.Headers.Add("X-XSRF-TOKEN", csrfTokenLogin1);
        if (!string.IsNullOrEmpty(cookieLogin1)) loginOldReq.Headers.Add("Cookie", cookieLogin1);

        var loginOldResponse = await _client.SendAsync(loginOldReq);
        loginOldResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Act 3: Attempt login with NEW password -> MUST SUCCEED (200)
        var (csrfTokenLogin2, cookieLogin2) = await GetAntiforgeryTokensAsync();
        var loginNewReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, newPassword))
        };
        loginNewReq.Headers.Add("X-XSRF-TOKEN", csrfTokenLogin2);
        if (!string.IsNullOrEmpty(cookieLogin2)) loginNewReq.Headers.Add("Cookie", cookieLogin2);

        var loginNewResponse = await _client.SendAsync(loginNewReq);
        loginNewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginNewResponse.Content.ReadFromJsonAsync<LoginResponse>();
        loginBody.Should().NotBeNull();
        loginBody!.User.Email.Should().Be(email);
    }

    [Fact]
    public async Task ResetPassword_TokenReuse_FailsOnSecondAttempt()
    {
        // Arrange
        var email = $"reuse_{Guid.NewGuid():N}@campus.internal";
        var user = await EnsureUserAsync(email, "InitialPassword123!", emailConfirmed: true);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync();

        // First attempt with valid token -> Succeeds
        var firstReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordRequest(user.Id, token, "NewPasswordOne123!", "NewPasswordOne123!"))
        };
        firstReq.Headers.Add("X-XSRF-TOKEN", csrfToken);
        if (!string.IsNullOrEmpty(cookie)) firstReq.Headers.Add("Cookie", cookie);

        var firstResponse = await _client.SendAsync(firstReq);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act: Second attempt with the SAME token -> MUST FAIL (400)
        var (csrfToken2, cookie2) = await GetAntiforgeryTokensAsync();
        var secondReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordRequest(user.Id, token, "NewPasswordTwo456!", "NewPasswordTwo456!"))
        };
        secondReq.Headers.Add("X-XSRF-TOKEN", csrfToken2);
        if (!string.IsNullOrEmpty(cookie2)) secondReq.Headers.Add("Cookie", cookie2);

        var secondResponse = await _client.SendAsync(secondReq);

        // Assert: Second attempt rejected because token cannot be reused
        secondResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Password Reset Failed");
        problem.GetProperty("detail").GetString().Should().Contain("Invalid or expired");
    }

    [Fact]
    public async Task ResetPassword_ChangesSecurityStamp_DocumentingSessionInvalidation()
    {
        // Arrange
        var email = $"secstamp_{Guid.NewGuid():N}@campus.internal";
        var user = await EnsureUserAsync(email, "InitialPassword123!", emailConfirmed: true);

        using var scope1 = _factory.Services.CreateScope();
        var userManager1 = scope1.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var initialUser = await userManager1.FindByIdAsync(user.Id.ToString());
        initialUser.Should().NotBeNull();
        var initialSecurityStamp = initialUser!.SecurityStamp;
        initialSecurityStamp.Should().NotBeNullOrWhiteSpace();

        var token = await userManager1.GeneratePasswordResetTokenAsync(initialUser);

        // Act: Reset password
        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordRequest(user.Id, token, "NewPasswordSecurity789!", "NewPasswordSecurity789!"))
        };
        req.Headers.Add("X-XSRF-TOKEN", csrfToken);
        if (!string.IsNullOrEmpty(cookie)) req.Headers.Add("Cookie", cookie);

        var response = await _client.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert: Verify that SecurityStamp has been automatically rotated
        using var scope2 = _factory.Services.CreateScope();
        var userManager2 = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var updatedUser = await userManager2.FindByIdAsync(user.Id.ToString());
        updatedUser.Should().NotBeNull();
        updatedUser!.SecurityStamp.Should().NotBeNullOrWhiteSpace();
        updatedUser.SecurityStamp.Should().NotBe(initialSecurityStamp,
            "ASP.NET Core Identity rotates the user security stamp upon password reset, invalidating previously issued session cookies");
    }

    [Fact]
    public async Task ResetPassword_TokenExpiry_FailsAfterTokenLifespan()
    {
        // Arrange: Use custom WebApplicationFactory with short 50ms token lifespan
        await using var shortLifespanFactory = new ShortExpirationWebApplicationFactory(
            tokenLifespan: TimeSpan.FromMilliseconds(50),
            stampValidationInterval: TimeSpan.FromMinutes(5));

        var client = shortLifespanFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var email = $"token_expiry_{Guid.NewGuid():N}@campus.internal";
        var user = await EnsureUserAsync(email, "InitialPassword123!", emailConfirmed: true);

        using var scope = shortLifespanFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        // Wait for token lifespan to elapse
        await Task.Delay(100);

        // Fetch CSRF token
        var csrfResponse = await client.GetAsync("/api/auth/antiforgery");
        var csrfData = await csrfResponse.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();
        var cookieHeader = string.Empty;
        if (csrfResponse.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            cookieHeader = string.Join("; ", setCookies.Select(c => c.Split(';')[0]));
        }

        // Act: Attempt to use the expired token
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordRequest(user.Id, token, "NewPasswordAfterExpiry123!", "NewPasswordAfterExpiry123!"))
        };
        req.Headers.Add("X-XSRF-TOKEN", csrfData!.RequestToken);
        if (!string.IsNullOrEmpty(cookieHeader)) req.Headers.Add("Cookie", cookieHeader);

        var response = await client.SendAsync(req);

        // Assert: Rejection with expired token error
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Password Reset Failed");
        problem.GetProperty("detail").GetString().Should().Contain("Invalid or expired");
    }

    [Fact]
    public async Task ResetPassword_PreResetSessionCookie_IsRejectedAfterStampValidationInterval()
    {
        // Arrange: Factory configured with 1-second stamp validation interval
        await using var shortIntervalFactory = new ShortExpirationWebApplicationFactory(
            tokenLifespan: TimeSpan.FromHours(2),
            stampValidationInterval: TimeSpan.FromSeconds(1));

        var client = shortIntervalFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var email = $"prereset_session_{Guid.NewGuid():N}@campus.internal";
        const string oldPassword = "InitialPassword123!";
        const string newPassword = "NewSecretPassword456!";
        var user = await EnsureUserAsync(email, oldPassword, emailConfirmed: true);

        // Step 1: User logs in and receives pre-reset session cookie
        var csrfResponse = await client.GetAsync("/api/auth/antiforgery");
        var csrfData = await csrfResponse.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();
        var cookieHeader = string.Empty;
        if (csrfResponse.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            cookieHeader = string.Join("; ", setCookies.Select(c => c.Split(';')[0]));
        }

        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, oldPassword))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrfData!.RequestToken);
        if (!string.IsNullOrEmpty(cookieHeader)) loginReq.Headers.Add("Cookie", cookieHeader);

        var loginResponse = await client.SendAsync(loginReq);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        loginResponse.Headers.TryGetValues("Set-Cookie", out var authCookies).Should().BeTrue();
        var authCookieHeader = string.Join("; ", authCookies!.Select(c => c.Split(';')[0]));

        // Step 2: Verify /api/auth/me works with this pre-reset session cookie
        var meReq1 = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meReq1.Headers.Add("Cookie", authCookieHeader);
        var meResponse1 = await client.SendAsync(meReq1);
        meResponse1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 3: Perform password reset (rotating user.SecurityStamp in database)
        using var scope = shortIntervalFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        // Fetch refreshed antiforgery token for authenticated session
        var resetCsrfResp = await client.GetAsync("/api/auth/antiforgery");
        var resetCsrfData = await resetCsrfResp.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();

        var resetReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordRequest(user.Id, token, newPassword, newPassword))
        };
        resetReq.Headers.Add("X-XSRF-TOKEN", resetCsrfData!.RequestToken);

        var resetResponse = await client.SendAsync(resetReq);
        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 4: Wait for the configured 1-second SecurityStamp validation interval to elapse
        await Task.Delay(1200);

        // Step 5: Send request to /api/auth/me using the PRE-RESET session cookie
        var meReq2 = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meReq2.Headers.Add("Cookie", authCookieHeader);
        var meResponse2 = await client.SendAsync(meReq2);

        // Assert: The pre-reset session cookie is rejected because user's SecurityStamp was rotated
        meResponse2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConfirmEmail_TokenExpiry_FailsAfterTokenLifespan()
    {
        // Arrange: Factory with 50ms token lifespan
        await using var shortLifespanFactory = new ShortExpirationWebApplicationFactory(
            tokenLifespan: TimeSpan.FromMilliseconds(50),
            stampValidationInterval: TimeSpan.FromMinutes(5));

        var client = shortLifespanFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var email = $"confirm_expiry_{Guid.NewGuid():N}@campus.internal";
        var user = await EnsureUserAsync(email, "InitialPassword123!", emailConfirmed: false);

        using var scope = shortLifespanFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);

        // Wait for token lifespan to elapse
        await Task.Delay(100);

        // Act: Attempt to confirm email with expired token
        var response = await client.PostAsJsonAsync("/api/auth/confirm-email", new ConfirmEmailRequest(user.Id, token));

        // Assert: Expired token is rejected
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Email Confirmation Failed");
        problem.GetProperty("detail").GetString().Should().Contain("Invalid or expired");
    }

    [Fact]
    public async Task ConfirmEmail_ValidTokenWithinLifetime_SuccessfullyConfirmsEmail()
    {
        // Arrange
        var email = $"confirm_valid_{Guid.NewGuid():N}@campus.internal";
        var user = await EnsureUserAsync(email, "InitialPassword123!", emailConfirmed: false);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);

        // Act: Confirm email within valid lifespan
        var response = await _client.PostAsJsonAsync("/api/auth/confirm-email", new ConfirmEmailRequest(user.Id, token));

        // Assert: Successfully confirmed
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ConfirmEmailResponse>();
        result.Should().NotBeNull();
        result!.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("confirmed successfully");

        // Verify database state
        var refreshedUser = await userManager.FindByIdAsync(user.Id.ToString());
        refreshedUser!.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public void TokenProviders_HaveIndependentlyConfiguredLifespans()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var identityOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityOptions>>().Value;
        var defaultDpOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DataProtectionTokenProviderOptions>>().Value;
        var passwordResetOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PasswordResetTokenProviderOptions>>().Value;

        // Email confirmation uses default provider with its standard 24-hour default lifespan
        identityOptions.Tokens.EmailConfirmationTokenProvider.Should().Be(TokenOptions.DefaultProvider);
        defaultDpOptions.TokenLifespan.Should().Be(TimeSpan.FromDays(1));

        // Password reset uses the dedicated provider with its scoped 2-hour lifespan
        identityOptions.Tokens.PasswordResetTokenProvider.Should().Be(PasswordResetTokenProviderDefaults.ProviderName);
        passwordResetOptions.TokenLifespan.Should().Be(TimeSpan.FromHours(2));

        // Regression assertion: verify the two token lifespans are configured independently
        passwordResetOptions.TokenLifespan.Should().NotBe(defaultDpOptions.TokenLifespan);
    }
}
