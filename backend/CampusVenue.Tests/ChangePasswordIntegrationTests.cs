using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Modules.Identity.Domain;
using CampusVenue.Api.Modules.Identity.Features.Auth;
using CampusVenue.Api.Modules.Identity.Features.ChangePassword;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CampusVenue.Tests;

public class ChangePasswordIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ChangePasswordIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        TestDatabaseHelper.EnsureMigrated();
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    private async Task<(string RequestToken, string CookieHeader)> GetAntiforgeryTokensAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery");
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

    private async Task<ApplicationUser> EnsureUserAsync(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        if (user != null)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
            return user;
        }

        user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(user, password);
        result.Succeeded.Should().BeTrue();
        return user;
    }

    [Fact]
    public async Task ChangePassword_SignedOut_Returns401Unauthorized()
    {
        // Arrange: Unauthenticated client
        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync(_client);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest("OldPass123!", "NewPass123!", "NewPass123!"))
        };
        req.Headers.Add("X-XSRF-TOKEN", csrfToken);
        if (!string.IsNullOrEmpty(cookie)) req.Headers.Add("Cookie", cookie);

        // Act
        var response = await _client.SendAsync(req);

        // Assert: Authorization denied
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_MissingAntiforgeryToken_Returns400BadRequest()
    {
        // Arrange: Authenticated client but missing X-XSRF-TOKEN header
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"chg_nocsrf_{Guid.NewGuid():N}@campus.internal";
        const string password = "InitialPassword123!";
        await EnsureUserAsync(email, password);

        // Login
        var (csrfToken, cookie) = await GetAntiforgeryTokensAsync(client);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrfToken);
        if (!string.IsNullOrEmpty(cookie)) loginReq.Headers.Add("Cookie", cookie);
        var loginResponse = await client.SendAsync(loginReq);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act: POST change-password without X-XSRF-TOKEN header
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(password, "NewPassword123!", "NewPassword123!"))
        };
        var response = await client.SendAsync(req);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Antiforgery Validation Failed");
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_Returns400BadRequest_AndLeavesPasswordUnchanged()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"chg_wrongcurr_{Guid.NewGuid():N}@campus.internal";
        const string initialPassword = "OriginalSecret123!";
        await EnsureUserAsync(email, initialPassword);

        // Login
        var (csrf1, cookie1) = await GetAntiforgeryTokensAsync(client);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, initialPassword))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        if (!string.IsNullOrEmpty(cookie1)) loginReq.Headers.Add("Cookie", cookie1);
        var loginResponse = await client.SendAsync(loginReq);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Fetch refreshed CSRF token for authenticated session
        var (authCsrf, _) = await GetAntiforgeryTokensAsync(client);

        // Act: Change password with incorrect current password
        var changeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest("WrongCurrentPass999!", "NewValidSecret456!", "NewValidSecret456!"))
        };
        changeReq.Headers.Add("X-XSRF-TOKEN", authCsrf);
        var changeResponse = await client.SendAsync(changeReq);

        // Assert: 400 Bad Request
        changeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await changeResponse.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Password Change Failed");
        problem.GetProperty("detail").GetString().Should().Contain("current password provided is incorrect");

        // Verify account's original password still works
        var verifyClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var (verifyCsrf, verifyCookie) = await GetAntiforgeryTokensAsync(verifyClient);
        var verifyReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, initialPassword))
        };
        verifyReq.Headers.Add("X-XSRF-TOKEN", verifyCsrf);
        if (!string.IsNullOrEmpty(verifyCookie)) verifyReq.Headers.Add("Cookie", verifyCookie);

        var verifyResponse = await verifyClient.SendAsync(verifyReq);
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_WeakNewPassword_Returns400BadRequest_WithPolicyViolation()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"chg_weak_{Guid.NewGuid():N}@campus.internal";
        const string initialPassword = "OriginalSecret123!";
        await EnsureUserAsync(email, initialPassword);

        // Login
        var (csrf1, cookie1) = await GetAntiforgeryTokensAsync(client);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, initialPassword))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        if (!string.IsNullOrEmpty(cookie1)) loginReq.Headers.Add("Cookie", cookie1);
        var loginResponse = await client.SendAsync(loginReq);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var (authCsrf, _) = await GetAntiforgeryTokensAsync(client);

        // Act: Change password with weak password (e.g. short)
        var changeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(initialPassword, "short", "short"))
        };
        changeReq.Headers.Add("X-XSRF-TOKEN", authCsrf);
        var changeResponse = await client.SendAsync(changeReq);

        // Assert: 400 Bad Request with Policy Error
        changeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await changeResponse.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Password Policy Error");
        problem.GetProperty("detail").GetString().Should().Contain("8");
    }

    [Fact]
    public async Task ChangePassword_MismatchedPasswords_Returns400BadRequest()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"chg_mismatch_{Guid.NewGuid():N}@campus.internal";
        const string initialPassword = "OriginalSecret123!";
        await EnsureUserAsync(email, initialPassword);

        // Login
        var (csrf1, cookie1) = await GetAntiforgeryTokensAsync(client);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, initialPassword))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        if (!string.IsNullOrEmpty(cookie1)) loginReq.Headers.Add("Cookie", cookie1);
        var loginResponse = await client.SendAsync(loginReq);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var (authCsrf, _) = await GetAntiforgeryTokensAsync(client);

        // Act: Passwords don't match
        var changeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(initialPassword, "NewValidPass123!", "DifferentPass456!"))
        };
        changeReq.Headers.Add("X-XSRF-TOKEN", authCsrf);
        var changeResponse = await client.SendAsync(changeReq);

        // Assert
        changeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await changeResponse.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Validation Error");
        problem.GetProperty("detail").GetString().Should().Contain("do not match");
    }

    [Fact]
    public async Task ChangePassword_ValidRequest_ChangesPassword_SignsOutCurrentSession_AndOnlyNewPasswordWorks()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"chg_success_{Guid.NewGuid():N}@campus.internal";
        const string initialPassword = "OriginalPassword123!";
        const string newPassword = "BrandNewSecurePassword456!";
        await EnsureUserAsync(email, initialPassword);

        // Login
        var (csrf1, cookie1) = await GetAntiforgeryTokensAsync(client);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, initialPassword))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        if (!string.IsNullOrEmpty(cookie1)) loginReq.Headers.Add("Cookie", cookie1);
        var loginResponse = await client.SendAsync(loginReq);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var (authCsrf, _) = await GetAntiforgeryTokensAsync(client);

        // Act: Change password
        var changeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(initialPassword, newPassword, newPassword))
        };
        changeReq.Headers.Add("X-XSRF-TOKEN", authCsrf);
        var changeResponse = await client.SendAsync(changeReq);

        // Assert 1: Succeeded
        changeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await changeResponse.Content.ReadFromJsonAsync<ChangePasswordResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Contain("successfully changed");

        // Assert 2: Current browser session is signed out (subsequent /api/auth/me returns 401)
        var meResponse = await client.GetAsync("/api/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Assert 3: Login with OLD password fails (401)
        var clientOld = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var (csrfOld, cookieOld) = await GetAntiforgeryTokensAsync(clientOld);
        var loginOldReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, initialPassword))
        };
        loginOldReq.Headers.Add("X-XSRF-TOKEN", csrfOld);
        if (!string.IsNullOrEmpty(cookieOld)) loginOldReq.Headers.Add("Cookie", cookieOld);
        var loginOldResponse = await clientOld.SendAsync(loginOldReq);
        loginOldResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Assert 4: Login with NEW password succeeds (200)
        var clientNew = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var (csrfNew, cookieNew) = await GetAntiforgeryTokensAsync(clientNew);
        var loginNewReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, newPassword))
        };
        loginNewReq.Headers.Add("X-XSRF-TOKEN", csrfNew);
        if (!string.IsNullOrEmpty(cookieNew)) loginNewReq.Headers.Add("Cookie", cookieNew);
        var loginNewResponse = await clientNew.SendAsync(loginNewReq);
        loginNewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_OtherExistingSessions_AreRejectedAfterStampValidationInterval()
    {
        // Arrange: Factory with 1-second stamp validation interval
        await using var shortIntervalFactory = new ShortExpirationWebApplicationFactory(
            tokenLifespan: TimeSpan.FromHours(2),
            stampValidationInterval: TimeSpan.FromSeconds(1));

        var clientA = shortIntervalFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var clientB = shortIntervalFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var email = $"chg_multisess_{Guid.NewGuid():N}@campus.internal";
        const string initialPassword = "MultiSessionPass123!";
        const string newPassword = "BrandNewMultiSessionPass456!";
        await EnsureUserAsync(email, initialPassword);

        // Step 1: Client A logs in
        var (csrfA, cookieA) = await GetAntiforgeryTokensAsync(clientA);
        var loginReqA = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, initialPassword))
        };
        loginReqA.Headers.Add("X-XSRF-TOKEN", csrfA);
        if (!string.IsNullOrEmpty(cookieA)) loginReqA.Headers.Add("Cookie", cookieA);
        var loginRespA = await clientA.SendAsync(loginReqA);
        loginRespA.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 2: Client B logs in
        var (csrfB, cookieB) = await GetAntiforgeryTokensAsync(clientB);
        var loginReqB = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, initialPassword))
        };
        loginReqB.Headers.Add("X-XSRF-TOKEN", csrfB);
        if (!string.IsNullOrEmpty(cookieB)) loginReqB.Headers.Add("Cookie", cookieB);
        var loginRespB = await clientB.SendAsync(loginReqB);
        loginRespB.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify Client B is authenticated
        var meRespB1 = await clientB.GetAsync("/api/auth/me");
        meRespB1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 3: Client A changes password
        var (authCsrfA, _) = await GetAntiforgeryTokensAsync(clientA);
        var changeReqA = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(initialPassword, newPassword, newPassword))
        };
        changeReqA.Headers.Add("X-XSRF-TOKEN", authCsrfA);
        var changeRespA = await clientA.SendAsync(changeReqA);
        changeRespA.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 4: Wait for the configured 1-second stamp validation interval to elapse
        await Task.Delay(1200);

        // Step 5: Client B sends request to /api/auth/me with its pre-change session cookie
        var meRespB2 = await clientB.GetAsync("/api/auth/me");

        // Assert: Client B's session cookie is rejected because user.SecurityStamp rotated in the database
        meRespB2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
