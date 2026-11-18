using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Infrastructure.Email;
using CampusVenue.Api.Modules.Identity.Domain;
using CampusVenue.Api.Modules.Identity.Features.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CampusVenue.Tests;

public class RegisterIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RegisterIntegrationTests(TestWebApplicationFactory factory)
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

    [Fact]
    public async Task Register_MissingAntiforgeryToken_Returns400BadRequest()
    {
        // Arrange
        var request = new RegisterRequest("newstudent@campus.internal", "ValidPass123!", "ValidPass123!");

        // Act: POST without X-XSRF-TOKEN
        var response = await _client.PostAsJsonAsync("/api/auth/register", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Antiforgery Validation Failed");
    }

    [Fact]
    public async Task Register_InvalidEmailFormat_Returns400BadRequest()
    {
        // Arrange
        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new RegisterRequest("not-an-email", "ValidPass123!", "ValidPass123!"))
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
    public async Task Register_MismatchedPasswords_Returns400BadRequest()
    {
        // Arrange
        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new RegisterRequest("student2@campus.internal", "Password123!", "Different123!"))
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
    public async Task Register_WeakPassword_EnforcesServerPolicyAndReturns400()
    {
        // Arrange: Password without digit, uppercase, or special character
        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new RegisterRequest("weak@campus.internal", "weak", "weak"))
        };
        req.Headers.Add("X-XSRF-TOKEN", csrfToken);
        if (!string.IsNullOrEmpty(cookie)) req.Headers.Add("Cookie", cookie);

        // Act
        var response = await _client.SendAsync(req);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Registration Failed");
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsGenericResponseWithoutDisclosingUserPresence()
    {
        // Arrange: Pre-existing user
        var email = $"duplicate_{Guid.NewGuid():N}@campus.internal";
        var password = "ValidPassword123!";

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            var result = await userManager.CreateAsync(user, password);
            result.Succeeded.Should().BeTrue();
        }

        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new RegisterRequest(email, "NewDifferentPassword123!", "NewDifferentPassword123!"))
        };
        req.Headers.Add("X-XSRF-TOKEN", csrfToken);
        if (!string.IsNullOrEmpty(cookie)) req.Headers.Add("Cookie", cookie);

        // Act
        var response = await _client.SendAsync(req);

        // Assert: 200 OK with uniform generic message
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        data.Should().NotBeNull();
        data!.Message.Should().Be("If this email address is eligible, a confirmation link has been sent. Please check your inbox.");

        // Assert: Exactly 1 account remains in database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var count = db.Users.Count(u => u.Email == email);
            count.Should().Be(1);
        }
    }

    [Fact]
    public async Task SelfServiceSignUp_CompleteEndToEndFlow_WithEmailConfirmation()
    {
        // Arrange
        var email = $"signup_{Guid.NewGuid():N}@campus.internal";
        var password = "SecureCampus2026!";

        // 1. Submit Registration Form
        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync();
        var registerReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new RegisterRequest(email, password, password))
        };
        registerReq.Headers.Add("X-XSRF-TOKEN", csrfToken);
        if (!string.IsNullOrEmpty(cookie)) registerReq.Headers.Add("Cookie", cookie);

        var registerRes = await _client.SendAsync(registerReq);

        // Assert registration response
        registerRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var registerBodyText = await registerRes.Content.ReadAsStringAsync();

        // Verification token and URLs MUST NOT be leaked in API response
        registerBodyText.Should().NotContain("token=");
        registerBodyText.Should().NotContain("userId=");

        // 2. Inspect Local Development Mailbox
        using var scope = _factory.Services.CreateScope();
        var mailbox = scope.ServiceProvider.GetRequiredService<DevMailboxService>();
        var sentEmails = mailbox.GetMessages();
        var verificationEmail = sentEmails.FirstOrDefault(m => m.To == email);

        verificationEmail.Should().NotBeNull("A confirmation email must be sent to the registered address");
        verificationEmail!.Subject.Should().Be("Verify your CampusVenue account");
        verificationEmail.BodyHtml.Should().Contain("Confirm Email Address");

        // Extract confirmation parameters from email body
        var match = Regex.Match(verificationEmail.BodyHtml, @"userId=([a-f0-9\-]+)&amp;token=([^""'<>&]+)");
        if (!match.Success)
        {
            match = Regex.Match(verificationEmail.BodyHtml, @"userId=([a-f0-9\-]+)&token=([^""'<>&]+)");
        }
        match.Success.Should().BeTrue("Email must contain confirmation link parameters");

        var userId = Guid.Parse(match.Groups[1].Value);
        var rawToken = Uri.UnescapeDataString(match.Groups[2].Value);

        // 3. Attempt Sign-In BEFORE Confirmation -> MUST FAIL (401 Email Not Confirmed)
        var (loginCsrf, loginCookie) = await GetAntiforgeryTokensAsync();
        var unconfirmedLoginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        unconfirmedLoginReq.Headers.Add("X-XSRF-TOKEN", loginCsrf);
        if (!string.IsNullOrEmpty(loginCookie)) unconfirmedLoginReq.Headers.Add("Cookie", loginCookie);

        var unconfirmedRes = await _client.SendAsync(unconfirmedLoginReq);
        unconfirmedRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var unconfirmedProblem = await unconfirmedRes.Content.ReadFromJsonAsync<JsonElement>();
        unconfirmedProblem.GetProperty("title").GetString().Should().Be("Email Not Confirmed");

        // 4. Confirm Email with Token (via POST /api/auth/confirm-email)
        var confirmReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/confirm-email")
        {
            Content = JsonContent.Create(new ConfirmEmailRequest(userId, rawToken))
        };
        var confirmRes = await _client.SendAsync(confirmReq);
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var confirmData = await confirmRes.Content.ReadFromJsonAsync<ConfirmEmailResponse>();
        confirmData!.Succeeded.Should().BeTrue();

        // 5. Verify User State in Database & Check Privilege Boundaries
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var confirmedUser = await userManager.FindByIdAsync(userId.ToString());
        confirmedUser.Should().NotBeNull();
        confirmedUser!.EmailConfirmed.Should().BeTrue();

        // Security check: Self-registered users must NOT have administrator or staff roles
        var roles = await userManager.GetRolesAsync(confirmedUser);
        roles.Should().BeEmpty("Self-registered users must have zero initial administrative roles");

        var claims = await userManager.GetClaimsAsync(confirmedUser);
        claims.Should().BeEmpty("Self-registered users must have zero initial administrative claims");

        // 6. Attempt Sign-In AFTER Confirmation -> MUST SUCCEED (200 OK + Session Cookie)
        var (activeCsrf, activeCookie) = await GetAntiforgeryTokensAsync();
        var confirmedLoginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        confirmedLoginReq.Headers.Add("X-XSRF-TOKEN", activeCsrf);
        if (!string.IsNullOrEmpty(activeCookie)) confirmedLoginReq.Headers.Add("Cookie", activeCookie);

        var confirmedLoginRes = await _client.SendAsync(confirmedLoginReq);
        confirmedLoginRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginBody = await confirmedLoginRes.Content.ReadFromJsonAsync<LoginResponse>();
        loginBody!.User.Email.Should().Be(email);

        // 7. Verify Protected Access (/api/auth/me) with Established Session
        var meRes = await _client.GetAsync("/api/auth/me");
        meRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var meBody = await meRes.Content.ReadFromJsonAsync<CurrentUserResponse>();
        meBody!.Email.Should().Be(email);
        meBody.IsAuthenticated.Should().BeTrue();
    }
}
