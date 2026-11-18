using System.Net;
using System.Net.Http.Json;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Modules.Identity.Domain;
using CampusVenue.Api.Modules.Identity.Features.Auth;
using CampusVenue.Api.Modules.Identity.Features.SignOutEverywhere;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CampusVenue.Tests;

public class SignOutEverywhereIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SignOutEverywhereIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        TestDatabaseHelper.EnsureMigrated(factory.Services);
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
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, password);
        result.Succeeded.Should().BeTrue();
        return user;
    }

    [Fact]
    public async Task SignOutEverywhere_SignedOut_Returns401Unauthorized()
    {
        // Arrange: Unauthenticated client
        var (csrf, cookie) = await GetAntiforgeryTokensAsync(_client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-out-everywhere");
        request.Headers.Add("X-XSRF-TOKEN", csrf);
        if (!string.IsNullOrEmpty(cookie))
        {
            request.Headers.Add("Cookie", cookie);
        }

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SignOutEverywhere_MissingAntiforgeryToken_Returns400BadRequest()
    {
        // Arrange: Authenticated client but missing X-XSRF-TOKEN header
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"soe_nocsrf_{Guid.NewGuid():N}@campus.internal";
        const string password = "ValidPassword123!";
        await EnsureUserAsync(email, password);

        var (csrf, cookie) = await GetAntiforgeryTokensAsync(client);

        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf);
        if (!string.IsNullOrEmpty(cookie)) loginReq.Headers.Add("Cookie", cookie);
        var loginResponse = await client.SendAsync(loginReq);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act: POST /api/auth/sign-out-everywhere WITHOUT X-XSRF-TOKEN
        var soeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-out-everywhere");

        var response = await client.SendAsync(soeReq);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem.Should().NotBeNull();
        problem!["title"].ToString().Should().Be("Antiforgery Validation Failed");
    }

    [Fact]
    public async Task SignOutEverywhere_ValidRequest_ImmediatelySignsOutCurrentBrowser()
    {
        // Arrange: Authenticated client
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"soe_immed_{Guid.NewGuid():N}@campus.internal";
        const string password = "ValidPassword123!";
        await EnsureUserAsync(email, password);

        var (csrf, cookie) = await GetAntiforgeryTokensAsync(client);

        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf);
        if (!string.IsNullOrEmpty(cookie)) loginReq.Headers.Add("Cookie", cookie);
        var loginResponse = await client.SendAsync(loginReq);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Obtain fresh antiforgery token for authenticated principal
        var (authCsrf, _) = await GetAntiforgeryTokensAsync(client);

        // Act: Call POST /api/auth/sign-out-everywhere
        var soeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-out-everywhere");
        soeReq.Headers.Add("X-XSRF-TOKEN", authCsrf);

        var response = await client.SendAsync(soeReq);

        // Assert 1: HTTP 200 OK with success message and new antiforgery token
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await response.Content.ReadFromJsonAsync<SignOutEverywhereResponse>();
        data.Should().NotBeNull();
        data!.Message.Should().Contain("signed out from all devices");
        data.AntiforgeryToken.Should().NotBeNullOrWhiteSpace();

        // Assert 2: Set-Cookie header contains expired CampusVenue.Auth cookie
        response.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeTrue();
        var authCookie = setCookies!.FirstOrDefault(c => c.StartsWith("CampusVenue.Auth="));
        authCookie.Should().NotBeNull();
        authCookie.Should().Contain("expires=");

        // Assert 3: Current client is immediately signed out on subsequent /api/auth/me request
        var meResponse = await client.GetAsync("/api/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SignOutEverywhere_OtherExistingSessions_AreRejectedAfterStampValidationInterval()
    {
        // Arrange: Factory with 1-second stamp validation interval
        await using var shortIntervalFactory = new ShortExpirationWebApplicationFactory(
            tokenLifespan: TimeSpan.FromHours(2),
            stampValidationInterval: TimeSpan.FromSeconds(1));

        var clientA = shortIntervalFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var clientB = shortIntervalFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var email = $"soe_multisess_{Guid.NewGuid():N}@campus.internal";
        const string password = "MultiSessionPass123!";
        await EnsureUserAsync(email, password);

        // Step 1: Client A signs in
        var (csrfA, cookieA) = await GetAntiforgeryTokensAsync(clientA);
        var loginReqA = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReqA.Headers.Add("X-XSRF-TOKEN", csrfA);
        if (!string.IsNullOrEmpty(cookieA)) loginReqA.Headers.Add("Cookie", cookieA);
        var loginRespA = await clientA.SendAsync(loginReqA);
        loginRespA.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 2: Client B signs in
        var (csrfB, cookieB) = await GetAntiforgeryTokensAsync(clientB);
        var loginReqB = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReqB.Headers.Add("X-XSRF-TOKEN", csrfB);
        if (!string.IsNullOrEmpty(cookieB)) loginReqB.Headers.Add("Cookie", cookieB);
        var loginRespB = await clientB.SendAsync(loginReqB);
        loginRespB.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify Client B is authenticated
        var meRespB1 = await clientB.GetAsync("/api/auth/me");
        meRespB1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 3: Client A triggers sign-out everywhere
        var (authCsrfA, _) = await GetAntiforgeryTokensAsync(clientA);
        var soeReqA = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-out-everywhere");
        soeReqA.Headers.Add("X-XSRF-TOKEN", authCsrfA);
        var soeRespA = await clientA.SendAsync(soeReqA);
        soeRespA.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 4: Wait for the configured 1-second stamp validation interval to elapse
        await Task.Delay(1200);

        // Step 5: Client B sends request to /api/auth/me with its pre-revocation session cookie
        var meRespB2 = await clientB.GetAsync("/api/auth/me");

        // Assert: Client B's session cookie is rejected because user.SecurityStamp rotated in the database
        meRespB2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SignOutEverywhere_UserCanSignInAgain_WithSamePassword()
    {
        // Arrange: User signs in and then signs out everywhere
        var clientA = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"soe_relogin_{Guid.NewGuid():N}@campus.internal";
        const string password = "PersistentSamePassword123!";
        await EnsureUserAsync(email, password);

        var (csrfA, cookieA) = await GetAntiforgeryTokensAsync(clientA);
        var loginReqA = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReqA.Headers.Add("X-XSRF-TOKEN", csrfA);
        if (!string.IsNullOrEmpty(cookieA)) loginReqA.Headers.Add("Cookie", cookieA);
        var loginRespA = await clientA.SendAsync(loginReqA);
        loginRespA.StatusCode.Should().Be(HttpStatusCode.OK);

        // Client A triggers sign-out everywhere
        var (authCsrfA, _) = await GetAntiforgeryTokensAsync(clientA);
        var soeReqA = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-out-everywhere");
        soeReqA.Headers.Add("X-XSRF-TOKEN", authCsrfA);
        var soeRespA = await clientA.SendAsync(soeReqA);
        soeRespA.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act: User creates a new session (Client B) and signs in with the SAME password
        var clientB = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var (csrfB, cookieB) = await GetAntiforgeryTokensAsync(clientB);
        var loginReqB = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReqB.Headers.Add("X-XSRF-TOKEN", csrfB);
        if (!string.IsNullOrEmpty(cookieB)) loginReqB.Headers.Add("Cookie", cookieB);
        var loginRespB = await clientB.SendAsync(loginReqB);

        // Assert: Login succeeds with same password and session is fully active
        loginRespB.StatusCode.Should().Be(HttpStatusCode.OK);

        var meRespB = await clientB.GetAsync("/api/auth/me");
        meRespB.StatusCode.Should().Be(HttpStatusCode.OK);
        var meData = await meRespB.Content.ReadFromJsonAsync<CurrentUserResponse>();
        meData.Should().NotBeNull();
        meData!.Email.Should().Be(email);
        meData.IsAuthenticated.Should().BeTrue();
    }
}
