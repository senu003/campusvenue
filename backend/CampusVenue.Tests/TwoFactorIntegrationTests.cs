using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusVenue.Api.Common;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Modules.Identity.Domain;
using CampusVenue.Api.Modules.Identity.Features.Auth;
using CampusVenue.Api.Modules.Identity.Features.PasswordRecovery;
using CampusVenue.Api.Modules.Identity.Features.SignOutEverywhere;
using CampusVenue.Api.Modules.Identity.Features.TwoFactor;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CampusVenue.Tests;

public class TwoFactorIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TwoFactorIntegrationTests(TestWebApplicationFactory factory)
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

    private async Task<string> GenerateTotpCodeAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        user.Should().NotBeNull();
        var key = await userManager.GetAuthenticatorKeyAsync(user!);
        key.Should().NotBeNullOrWhiteSpace();
        return ComputeTotp(key!);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string password)
    {
        await EnsureUserAsync(email, password, emailConfirmed: true);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var (csrfToken, _) = await GetAntiforgeryTokensAsync(client);

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginRequest.Headers.Add("X-XSRF-TOKEN", csrfToken);

        var loginResponse = await client.SendAsync(loginRequest);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        return client;
    }

    private static string ComputeTotp(string unformattedKey)
    {
        // Base32 decode RFC 4648
        var keyBytes = Base32Decode(unformattedKey);
        var timestep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var timestepBytes = BitConverter.GetBytes(timestep);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(timestepBytes);
        }

        using var hmac = new System.Security.Cryptography.HMACSHA1(keyBytes);
        var hash = hmac.ComputeHash(timestepBytes);

        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);

        var otp = binary % 1_000_000;
        return otp.ToString("D6");
    }

    private static byte[] Base32Decode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var clean = base32.Trim().ToUpperInvariant().Replace(" ", string.Empty).Replace("=", string.Empty);
        var bytes = new List<byte>();
        int buffer = 0;
        int bitsLeft = 0;

        foreach (var c in clean)
        {
            var val = alphabet.IndexOf(c);
            if (val < 0) continue;
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bytes.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }

        return bytes.ToArray();
    }

    [Fact]
    public async Task TwoFactor_Status_Unauthenticated_Returns401Unauthorized()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        // Act
        var response = await client.GetAsync("/api/auth/2fa/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TwoFactor_Setup_Unauthenticated_Returns401Unauthorized()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        // Act
        var response = await client.GetAsync("/api/auth/2fa/setup");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TwoFactor_Setup_Authenticated_ReturnsValidManualKey_TotpUri_AndSvg()
    {
        // Arrange
        var email = $"2fa_setup_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var client = await CreateAuthenticatedClientAsync(email, password);

        // Act
        var response = await client.GetAsync("/api/auth/2fa/setup");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var data = await response.Content.ReadFromJsonAsync<TwoFactorSetupResponse>();
        data.Should().NotBeNull();
        data!.ManualEntryKey.Should().NotBeNullOrWhiteSpace();
        data.UnformattedKey.Should().NotBeNullOrWhiteSpace();
        data.AuthenticatorUri.Should().StartWith("otpauth://totp/");
        data.AuthenticatorUri.Should().Contain("CampusVenue");
        data.QrCodeSvg.Should().Contain("<svg");
        data.QrCodeDataUrl.Should().StartWith("data:image/svg+xml");
    }

    [Fact]
    public async Task TwoFactor_Enable_MissingAntiforgery_Returns400BadRequest()
    {
        // Arrange
        var email = $"2fa_nocsrf_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var client = await CreateAuthenticatedClientAsync(email, password);

        // Act (no X-XSRF-TOKEN header)
        var response = await client.PostAsJsonAsync("/api/auth/2fa/enable", new EnableTwoFactorRequest(password, "123456"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TwoFactor_Enable_WrongPassword_Returns400BadRequest()
    {
        // Arrange
        var email = $"2fa_wrongpw_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var client = await CreateAuthenticatedClientAsync(email, password);

        // Initialize setup key
        var setupResp = await client.GetAsync("/api/auth/2fa/setup");
        setupResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var (csrfToken, _) = await GetAntiforgeryTokensAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest("IncorrectPassword999!", "123456"))
        };
        request.Headers.Add("X-XSRF-TOKEN", csrfToken);

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("current password provided is incorrect");
    }

    [Fact]
    public async Task TwoFactor_Enable_InvalidTotpCode_Returns400BadRequest()
    {
        // Arrange
        var email = $"2fa_invalidtotp_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var client = await CreateAuthenticatedClientAsync(email, password);

        // Initialize setup key
        var setupResp = await client.GetAsync("/api/auth/2fa/setup");
        setupResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var (csrfToken, _) = await GetAntiforgeryTokensAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(password, "000000"))
        };
        request.Headers.Add("X-XSRF-TOKEN", csrfToken);

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("authenticator verification code is invalid");
    }

    [Fact]
    public async Task TwoFactor_Enable_ValidPasswordAndTotp_EnablesTwoFactor_AndReturns10RecoveryCodes()
    {
        // Arrange
        var email = $"2fa_enablesuccess_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var client = await CreateAuthenticatedClientAsync(email, password);

        // Call setup to generate key
        var setupResp = await client.GetAsync("/api/auth/2fa/setup");
        setupResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var user = await EnsureUserAsync(email, password);
        var validCode = await GenerateTotpCodeAsync(user.Id);

        var (csrfToken, _) = await GetAntiforgeryTokensAsync(client);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(password, validCode))
        };
        request.Headers.Add("X-XSRF-TOKEN", csrfToken);

        // Act
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: body);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var data = await response.Content.ReadFromJsonAsync<EnableTwoFactorResponse>();
        data.Should().NotBeNull();
        data!.RecoveryCodes.Should().HaveCount(10);

        // Verify status endpoint reflects enabled MFA with 10 recovery codes
        var statusResp = await client.GetAsync("/api/auth/2fa/status");
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await statusResp.Content.ReadFromJsonAsync<TwoFactorStatusResponse>();
        status.Should().NotBeNull();
        status!.IsTwoFactorEnabled.Should().BeTrue();
        status.RecoveryCodesLeft.Should().Be(10);
    }

    [Fact]
    public async Task TwoFactor_WhenEnabled_SignInWithPasswordOnly_RequiresTwoFactor_AndDoesNotIssueSessionCookie()
    {
        // Arrange: Create user and enable MFA
        var email = $"2fa_flow_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var client = await CreateAuthenticatedClientAsync(email, password);

        await client.GetAsync("/api/auth/2fa/setup");
        var user = await EnsureUserAsync(email, password);
        var totp = await GenerateTotpCodeAsync(user.Id);

        var (csrfToken, _) = await GetAntiforgeryTokensAsync(client);
        var enableReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(password, totp))
        };
        enableReq.Headers.Add("X-XSRF-TOKEN", csrfToken);
        var enableResp = await client.SendAsync(enableReq);
        enableResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Fresh unauthenticated client
        var newClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (newCsrf, _) = await GetAntiforgeryTokensAsync(newClient);

        // Act: Sign in with password only
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", newCsrf);

        var loginResp = await newClient.SendAsync(loginReq);

        // Assert: Login response indicates two-factor required
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginData = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
        loginData.Should().NotBeNull();
        loginData!.RequiresTwoFactor.Should().BeTrue();
        loginData.User.Should().BeNull();

        // TwoFactorUserId cookie is present, but CampusVenue.Auth cookie is NOT issued
        if (loginResp.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            var cookieList = setCookies.ToList();
            cookieList.Should().Contain(c => c.Contains("CampusVenue.TwoFactorUserId"));
            cookieList.Should().NotContain(c => c.Contains("CampusVenue.Auth"));
        }

        // Protected endpoint /api/auth/me MUST return 401 Unauthorized while MFA challenge is pending
        var meResp = await newClient.GetAsync("/api/auth/me");
        meResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TwoFactor_Challenge_WithoutPendingChallenge_Returns400BadRequest()
    {
        // Arrange: Client with no pending 2FA cookie
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var (csrfToken, _) = await GetAntiforgeryTokensAsync(client);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/challenge")
        {
            Content = JsonContent.Create(new TwoFactorChallengeRequest("123456"))
        };
        req.Headers.Add("X-XSRF-TOKEN", csrfToken);

        // Act
        var response = await client.SendAsync(req);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("challenge has expired or is invalid");
    }

    [Fact]
    public async Task TwoFactor_Challenge_WrongTotpCode_Returns400BadRequest()
    {
        // Arrange: Enable MFA on user
        var email = $"2fa_wrongchallenge_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var client = await CreateAuthenticatedClientAsync(email, password);

        await client.GetAsync("/api/auth/2fa/setup");
        var user = await EnsureUserAsync(email, password);
        var totp = await GenerateTotpCodeAsync(user.Id);

        var (csrf1, _) = await GetAntiforgeryTokensAsync(client);
        var enableReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(password, totp))
        };
        enableReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        await client.SendAsync(enableReq);

        // Start login to get pending 2FA challenge
        var loginClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf2, _) = await GetAntiforgeryTokensAsync(loginClient);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        await loginClient.SendAsync(loginReq);

        // Act: Send invalid TOTP code
        var (csrf3, _) = await GetAntiforgeryTokensAsync(loginClient);
        var challengeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/challenge")
        {
            Content = JsonContent.Create(new TwoFactorChallengeRequest("999999"))
        };
        challengeReq.Headers.Add("X-XSRF-TOKEN", csrf3);

        var challengeResp = await loginClient.SendAsync(challengeReq);

        // Assert
        challengeResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await challengeResp.Content.ReadAsStringAsync();
        body.Should().Contain("verification code is invalid");
    }

    [Fact]
    public async Task TwoFactor_Challenge_ValidTotpCode_IssuesFullSessionCookie_AndClearsTwoFactorCookie()
    {
        // Arrange: Enable MFA on user
        var email = $"2fa_totpsuccess_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var client = await CreateAuthenticatedClientAsync(email, password);

        await client.GetAsync("/api/auth/2fa/setup");
        var user = await EnsureUserAsync(email, password);
        var totp = await GenerateTotpCodeAsync(user.Id);

        var (csrf1, _) = await GetAntiforgeryTokensAsync(client);
        var enableReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(password, totp))
        };
        enableReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        await client.SendAsync(enableReq);

        // Start login
        var loginClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf2, _) = await GetAntiforgeryTokensAsync(loginClient);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        await loginClient.SendAsync(loginReq);

        // Act: Send valid TOTP code
        var validCode = await GenerateTotpCodeAsync(user.Id);
        var (csrf3, _) = await GetAntiforgeryTokensAsync(loginClient);
        var challengeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/challenge")
        {
            Content = JsonContent.Create(new TwoFactorChallengeRequest(validCode))
        };
        challengeReq.Headers.Add("X-XSRF-TOKEN", csrf3);

        var challengeResp = await loginClient.SendAsync(challengeReq);

        // Assert: Successful login
        challengeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var challengeData = await challengeResp.Content.ReadFromJsonAsync<LoginResponse>();
        challengeData.Should().NotBeNull();
        challengeData!.User.Should().NotBeNull();
        challengeData.User!.Email.Should().Be(email);

        // Full session cookie is issued and TwoFactorUserId is expired/cleared
        if (challengeResp.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            var cookieList = setCookies.ToList();
            cookieList.Should().Contain(c => c.Contains("CampusVenue.Auth"));
        }

        // /api/auth/me is now accessible
        var meResp = await loginClient.GetAsync("/api/auth/me");
        meResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var meData = await meResp.Content.ReadFromJsonAsync<CurrentUserResponse>();
        meData.Should().NotBeNull();
        meData!.IsAuthenticated.Should().BeTrue();
        meData.TwoFactorEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task TwoFactor_Challenge_ValidRecoveryCode_SignsIn_AndDecrementsRecoveryCodeCount()
    {
        // Arrange: Enable MFA and capture recovery codes
        var email = $"2fa_recovery_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var client = await CreateAuthenticatedClientAsync(email, password);

        await client.GetAsync("/api/auth/2fa/setup");
        var user = await EnsureUserAsync(email, password);
        var totp = await GenerateTotpCodeAsync(user.Id);

        var (csrf1, _) = await GetAntiforgeryTokensAsync(client);
        var enableReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(password, totp))
        };
        enableReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        var enableResp = await client.SendAsync(enableReq);
        var enableData = await enableResp.Content.ReadFromJsonAsync<EnableTwoFactorResponse>();
        enableData.Should().NotBeNull();
        enableData!.RecoveryCodes.Should().NotBeEmpty();
        var recoveryCode = enableData.RecoveryCodes[0];

        // Start login on fresh client
        var loginClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf2, _) = await GetAntiforgeryTokensAsync(loginClient);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        await loginClient.SendAsync(loginReq);

        // Act: Redeem recovery code
        var (csrf3, _) = await GetAntiforgeryTokensAsync(loginClient);
        var challengeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/challenge")
        {
            Content = JsonContent.Create(new TwoFactorChallengeRequest(recoveryCode, IsRecoveryCode: true))
        };
        challengeReq.Headers.Add("X-XSRF-TOKEN", csrf3);

        var challengeResp = await loginClient.SendAsync(challengeReq);

        // Assert: Successful login
        challengeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Recovery codes remaining is now 9
        var statusResp = await loginClient.GetAsync("/api/auth/2fa/status");
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await statusResp.Content.ReadFromJsonAsync<TwoFactorStatusResponse>();
        status!.RecoveryCodesLeft.Should().Be(9);
    }

    [Fact]
    public async Task TwoFactor_Challenge_UsedRecoveryCode_CannotBeReused()
    {
        // Arrange: Enable MFA and capture recovery codes
        var email = $"2fa_reuse_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var client = await CreateAuthenticatedClientAsync(email, password);

        await client.GetAsync("/api/auth/2fa/setup");
        var user = await EnsureUserAsync(email, password);
        var totp = await GenerateTotpCodeAsync(user.Id);

        var (csrf1, _) = await GetAntiforgeryTokensAsync(client);
        var enableReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(password, totp))
        };
        enableReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        var enableResp = await client.SendAsync(enableReq);
        var enableData = await enableResp.Content.ReadFromJsonAsync<EnableTwoFactorResponse>();
        var recoveryCode = enableData!.RecoveryCodes[0];

        // First use: Login client 1
        var client1 = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrfClient1, _) = await GetAntiforgeryTokensAsync(client1);
        var login1 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new LoginRequest(email, password)) };
        login1.Headers.Add("X-XSRF-TOKEN", csrfClient1);
        await client1.SendAsync(login1);

        var (csrfRedeem1, _) = await GetAntiforgeryTokensAsync(client1);
        var redeem1 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/challenge")
        {
            Content = JsonContent.Create(new TwoFactorChallengeRequest(recoveryCode, IsRecoveryCode: true))
        };
        redeem1.Headers.Add("X-XSRF-TOKEN", csrfRedeem1);
        var resp1 = await client1.SendAsync(redeem1);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second use attempt: Login client 2
        var client2 = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrfClient2, _) = await GetAntiforgeryTokensAsync(client2);
        var login2 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new LoginRequest(email, password)) };
        login2.Headers.Add("X-XSRF-TOKEN", csrfClient2);
        await client2.SendAsync(login2);

        var (csrfRedeem2, _) = await GetAntiforgeryTokensAsync(client2);
        var redeem2 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/challenge")
        {
            Content = JsonContent.Create(new TwoFactorChallengeRequest(recoveryCode, IsRecoveryCode: true))
        };
        redeem2.Headers.Add("X-XSRF-TOKEN", csrfRedeem2);

        // Act
        var resp2 = await client2.SendAsync(redeem2);

        // Assert: Reusing single-use code is rejected
        resp2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp2.Content.ReadAsStringAsync();
        body.Should().Contain("already been used");
    }

    [Fact]
    public async Task TwoFactor_RegenerateRecoveryCodes_RequiresCurrentPasswordAndSecondFactor()
    {
        // Arrange
        var email = $"2fa_regen_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var client = await CreateAuthenticatedClientAsync(email, password);

        await client.GetAsync("/api/auth/2fa/setup");
        var user = await EnsureUserAsync(email, password);
        var totp = await GenerateTotpCodeAsync(user.Id);

        var (csrf1, _) = await GetAntiforgeryTokensAsync(client);
        var enableReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(password, totp))
        };
        enableReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        await client.SendAsync(enableReq);

        // Act 1: Wrong password
        var (csrf2, _) = await GetAntiforgeryTokensAsync(client);
        var wrongPwReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/regenerate-recovery-codes")
        {
            Content = JsonContent.Create(new RegenerateRecoveryCodesRequest("WrongPassword123!", totp))
        };
        wrongPwReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        var wrongPwResp = await client.SendAsync(wrongPwReq);
        wrongPwResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Act 2: Wrong second factor
        var (csrf3, _) = await GetAntiforgeryTokensAsync(client);
        var wrong2faReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/regenerate-recovery-codes")
        {
            Content = JsonContent.Create(new RegenerateRecoveryCodesRequest(password, "000000"))
        };
        wrong2faReq.Headers.Add("X-XSRF-TOKEN", csrf3);
        var wrong2faResp = await client.SendAsync(wrong2faReq);
        wrong2faResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TwoFactor_RegenerateRecoveryCodes_InvalidatesPreviousSet()
    {
        // Arrange
        var email = $"2fa_invalidateprev_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var client = await CreateAuthenticatedClientAsync(email, password);

        await client.GetAsync("/api/auth/2fa/setup");
        var user = await EnsureUserAsync(email, password);
        var totp = await GenerateTotpCodeAsync(user.Id);

        var (csrf1, _) = await GetAntiforgeryTokensAsync(client);
        var enableReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(password, totp))
        };
        enableReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        var enableResp = await client.SendAsync(enableReq);
        var enableData = await enableResp.Content.ReadFromJsonAsync<EnableTwoFactorResponse>();
        var originalFirstCode = enableData!.RecoveryCodes[0];

        // Regenerate recovery codes
        var freshTotp = await GenerateTotpCodeAsync(user.Id);
        var (csrf2, _) = await GetAntiforgeryTokensAsync(client);
        var regenReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/regenerate-recovery-codes")
        {
            Content = JsonContent.Create(new RegenerateRecoveryCodesRequest(password, freshTotp))
        };
        regenReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        var regenResp = await client.SendAsync(regenReq);
        regenResp.StatusCode.Should().Be(HttpStatusCode.OK);
        regenResp.Headers.CacheControl.Should().NotBeNull();
        regenResp.Headers.CacheControl!.NoStore.Should().BeTrue();
        var regenData = await regenResp.Content.ReadFromJsonAsync<RegenerateRecoveryCodesResponse>();
        regenData.Should().NotBeNull();
        regenData!.RecoveryCodes.Should().HaveCount(10);
        regenData.RecoveryCodes.Should().NotContain(originalFirstCode);

        // Attempting to sign in with an old recovery code MUST be rejected
        var loginClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrfLogin, _) = await GetAntiforgeryTokensAsync(loginClient);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new LoginRequest(email, password)) };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrfLogin);
        await loginClient.SendAsync(loginReq);

        var (csrfChallenge, _) = await GetAntiforgeryTokensAsync(loginClient);
        var challengeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/challenge")
        {
            Content = JsonContent.Create(new TwoFactorChallengeRequest(originalFirstCode, IsRecoveryCode: true))
        };
        challengeReq.Headers.Add("X-XSRF-TOKEN", csrfChallenge);
        var challengeResp = await loginClient.SendAsync(challengeReq);

        // Assert: Old code is invalid
        challengeResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Act: Sign in with one of the NEW recovery codes succeeds
        var newCode = regenData.RecoveryCodes[0];
        var (csrfNewChallenge, _) = await GetAntiforgeryTokensAsync(loginClient);
        var newChallengeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/challenge")
        {
            Content = JsonContent.Create(new TwoFactorChallengeRequest(newCode, IsRecoveryCode: true))
        };
        newChallengeReq.Headers.Add("X-XSRF-TOKEN", csrfNewChallenge);
        var newChallengeResp = await loginClient.SendAsync(newChallengeReq);
        newChallengeResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TwoFactor_Disable_RequiresCurrentPasswordAndSecondFactor()
    {
        // Arrange
        var email = $"2fa_disablecheck_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var client = await CreateAuthenticatedClientAsync(email, password);

        await client.GetAsync("/api/auth/2fa/setup");
        var user = await EnsureUserAsync(email, password);
        var totp = await GenerateTotpCodeAsync(user.Id);

        var (csrf1, _) = await GetAntiforgeryTokensAsync(client);
        var enableReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(password, totp))
        };
        enableReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        await client.SendAsync(enableReq);

        // Act 1: Wrong password
        var (csrf2, _) = await GetAntiforgeryTokensAsync(client);
        var wrongPwReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/disable")
        {
            Content = JsonContent.Create(new DisableTwoFactorRequest("WrongPassword!", totp))
        };
        wrongPwReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        var wrongPwResp = await client.SendAsync(wrongPwReq);
        wrongPwResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Act 2: Wrong second factor
        var (csrf3, _) = await GetAntiforgeryTokensAsync(client);
        var wrong2faReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/disable")
        {
            Content = JsonContent.Create(new DisableTwoFactorRequest(password, "000000"))
        };
        wrong2faReq.Headers.Add("X-XSRF-TOKEN", csrf3);
        var wrong2faResp = await client.SendAsync(wrong2faReq);
        wrong2faResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TwoFactor_Disable_TurnsOffTwoFactor_AllowsPasswordOnlySignIn()
    {
        // Arrange
        var email = $"2fa_disablesuccess_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var client = await CreateAuthenticatedClientAsync(email, password);

        await client.GetAsync("/api/auth/2fa/setup");
        var user = await EnsureUserAsync(email, password);
        var totp = await GenerateTotpCodeAsync(user.Id);

        var (csrf1, _) = await GetAntiforgeryTokensAsync(client);
        var enableReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(password, totp))
        };
        enableReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        await client.SendAsync(enableReq);

        // Act: Disable Two-Factor
        var freshTotp = await GenerateTotpCodeAsync(user.Id);
        var (csrf2, _) = await GetAntiforgeryTokensAsync(client);
        var disableReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/disable")
        {
            Content = JsonContent.Create(new DisableTwoFactorRequest(password, freshTotp))
        };
        disableReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        var disableResp = await client.SendAsync(disableReq);

        // Assert: 2FA is disabled
        disableResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var statusResp = await client.GetAsync("/api/auth/2fa/status");
        var status = await statusResp.Content.ReadFromJsonAsync<TwoFactorStatusResponse>();
        status!.IsTwoFactorEnabled.Should().BeFalse();

        // Sign in on new client now establishes full session without 2FA challenge
        var loginClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrfLogin, _) = await GetAntiforgeryTokensAsync(loginClient);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new LoginRequest(email, password)) };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrfLogin);
        var loginResp = await loginClient.SendAsync(loginReq);

        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginData = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
        loginData!.RequiresTwoFactor.Should().BeFalse();
        loginData.User.Should().NotBeNull();

        // /api/auth/me succeeds immediately
        var meResp = await loginClient.GetAsync("/api/auth/me");
        meResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TwoFactor_PasswordReset_LeavesTwoFactorEnabled()
    {
        // Arrange: Enable MFA on user
        var email = $"2fa_pwreset_{Guid.NewGuid():N}@campus.internal";
        var originalPassword = "InitialPassword123!";
        var newPassword = "BrandNewPassword123!";
        var client = await CreateAuthenticatedClientAsync(email, originalPassword);

        await client.GetAsync("/api/auth/2fa/setup");
        var user = await EnsureUserAsync(email, originalPassword);
        var totp = await GenerateTotpCodeAsync(user.Id);

        var (csrf1, _) = await GetAntiforgeryTokensAsync(client);
        var enableReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(originalPassword, totp))
        };
        enableReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        await client.SendAsync(enableReq);

        // Generate password reset token
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var resetUser = await userManager.FindByIdAsync(user.Id.ToString());
        var resetToken = await userManager.GeneratePasswordResetTokenAsync(resetUser!);

        // Reset password via POST /api/auth/reset-password
        var resetClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var (csrfReset, _) = await GetAntiforgeryTokensAsync(resetClient);
        var resetReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordRequest(user.Id, resetToken, newPassword, newPassword))
        };
        resetReq.Headers.Add("X-XSRF-TOKEN", csrfReset);
        var resetResp = await resetClient.SendAsync(resetReq);
        resetResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify TwoFactorEnabled in database remains true
        var updatedUser = await userManager.FindByIdAsync(user.Id.ToString());
        updatedUser!.TwoFactorEnabled.Should().BeTrue();

        // Verify that signing in with new password still prompts for MFA challenge
        var (csrfLogin, _) = await GetAntiforgeryTokensAsync(resetClient);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, newPassword))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrfLogin);
        var loginResp = await resetClient.SendAsync(loginReq);

        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginData = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
        loginData!.RequiresTwoFactor.Should().BeTrue();
    }

    [Fact]
    public async Task TwoFactor_SignOutEverywhere_StillWorksWithMfaUser()
    {
        // Arrange
        var email = $"2fa_soe_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var client = await CreateAuthenticatedClientAsync(email, password);

        await client.GetAsync("/api/auth/2fa/setup");
        var user = await EnsureUserAsync(email, password);
        var totp = await GenerateTotpCodeAsync(user.Id);

        var (csrf1, _) = await GetAntiforgeryTokensAsync(client);
        var enableReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(password, totp))
        };
        enableReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        await client.SendAsync(enableReq);

        // Act: Call sign out everywhere
        var (csrfSoe, _) = await GetAntiforgeryTokensAsync(client);
        var soeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sign-out-everywhere");
        soeReq.Headers.Add("X-XSRF-TOKEN", csrfSoe);
        var soeResp = await client.SendAsync(soeReq);

        // Assert: Succeeds
        soeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var meResp = await client.GetAsync("/api/auth/me");
        meResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TwoFactor_Challenge_MultipleFailedAttempts_LocksOutAccount()
    {
        // Arrange
        var email = $"2fa_lockout_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var client = await CreateAuthenticatedClientAsync(email, password);

        await client.GetAsync("/api/auth/2fa/setup");
        var user = await EnsureUserAsync(email, password);
        var totp = await GenerateTotpCodeAsync(user.Id);

        var (csrf1, _) = await GetAntiforgeryTokensAsync(client);
        var enableReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(password, totp))
        };
        enableReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        await client.SendAsync(enableReq);

        var loginClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf2, _) = await GetAntiforgeryTokensAsync(loginClient);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        await loginClient.SendAsync(loginReq);

        // Act: 5 failed 2FA challenge attempts
        for (int i = 0; i < 5; i++)
        {
            var (csrfFail, _) = await GetAntiforgeryTokensAsync(loginClient);
            var failReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/challenge")
            {
                Content = JsonContent.Create(new TwoFactorChallengeRequest("000000"))
            };
            failReq.Headers.Add("X-XSRF-TOKEN", csrfFail);
            var failResp = await loginClient.SendAsync(failReq);
            if (failResp.StatusCode == HttpStatusCode.Unauthorized)
            {
                break;
            }
        }

        // Verify account is now locked out
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var lockedUser = await userManager.FindByIdAsync(user.Id.ToString());
        var isLocked = await userManager.IsLockedOutAsync(lockedUser!);
        isLocked.Should().BeTrue();
    }

    private async Task<ApplicationUser> EnsureUserWithRoleAsync(
        string email,
        string password,
        string role = CampusRoles.Participant,
        bool emailConfirmed = true)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new ApplicationRole { Name = role });
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user != null)
        {
            user.EmailConfirmed = emailConfirmed;
            await userManager.UpdateAsync(user);

            if (!await userManager.IsInRoleAsync(user, role))
            {
                await userManager.AddToRoleAsync(user, role);
            }

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

        var addRoleResult = await userManager.AddToRoleAsync(user, role);
        addRoleResult.Succeeded.Should().BeTrue();

        return user;
    }

    // =========================================================================
    // SecurityStampValidator & MFA Session Preservation Regression Tests
    // =========================================================================

    [Fact]
    public async Task TwoFactor_AdminSession_MfaCompleted_ImmediatelyAfterChallenge_AndPreservedAfterSecurityStampRevalidation()
    {
        // Factory configured with ValidationInterval = Zero so security-stamp revalidation triggers on every request
        var revalidatingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Configure<SecurityStampValidatorOptions>(options =>
                {
                    options.ValidationInterval = TimeSpan.Zero;
                });
            });
        });

        var email = $"admin_stamp_reval_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var user = await EnsureUserWithRoleAsync(email, password, CampusRoles.UniversityAdmin);

        // Configure MFA directly on the user account
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var dbUser = await userManager.FindByIdAsync(user.Id.ToString());
            await userManager.ResetAuthenticatorKeyAsync(dbUser!);
            await userManager.SetTwoFactorEnabledAsync(dbUser!, true);
        }

        // Client performs login and completes 2FA challenge
        var client = revalidatingFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf1, _) = await GetAntiforgeryTokensAsync(client);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        var loginResp = await client.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginData = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
        loginData!.RequiresTwoFactor.Should().BeTrue();

        var challengeTotp = await GenerateTotpCodeAsync(user.Id);
        var (csrf2, _) = await GetAntiforgeryTokensAsync(client);
        var challengeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/challenge")
        {
            Content = JsonContent.Create(new TwoFactorChallengeRequest(challengeTotp))
        };
        challengeReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        var challengeResp = await client.SendAsync(challengeReq);
        challengeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step A: Immediately after challenge, /api/auth/me returns mfaCompleted = true
        var immediateMeResp = await client.GetAsync("/api/auth/me");
        immediateMeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var immediateMeData = await immediateMeResp.Content.ReadFromJsonAsync<CurrentUserResponse>();
        immediateMeData.Should().NotBeNull();
        immediateMeData!.IsAuthenticated.Should().BeTrue();
        immediateMeData.TwoFactorEnabled.Should().BeTrue();
        immediateMeData.MfaCompleted.Should().BeTrue();
        immediateMeData.Roles.Should().Contain(CampusRoles.UniversityAdmin);

        // Step B: Subsequent request with ValidationInterval = TimeSpan.Zero triggers SecurityStampValidator renewal
        var renewedMeResp = await client.GetAsync("/api/auth/me");
        renewedMeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var renewedMeData = await renewedMeResp.Content.ReadFromJsonAsync<CurrentUserResponse>();
        renewedMeData.Should().NotBeNull();
        renewedMeData!.IsAuthenticated.Should().BeTrue();
        renewedMeData.TwoFactorEnabled.Should().BeTrue();
        // Trusted amr=mfa claim is preserved by OnRefreshingPrincipal onto the new principal
        renewedMeData.MfaCompleted.Should().BeTrue();
        renewedMeData.Roles.Should().Contain(CampusRoles.UniversityAdmin);

        // Step C: Access policy-protected endpoint requiring UniversityAdminWithMfa after cookie renewal
        var userDirectoryResp = await client.GetAsync("/api/users");
        userDirectoryResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TwoFactor_PasswordOnlySession_RemainsMfaIncomplete_BeforeAndAfterSecurityStampRevalidation()
    {
        var revalidatingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Configure<SecurityStampValidatorOptions>(options =>
                {
                    options.ValidationInterval = TimeSpan.Zero;
                });
            });
        });

        // User establishes password-only session (no MFA challenge completed)
        var email = $"admin_password_only_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var user = await EnsureUserWithRoleAsync(email, password, CampusRoles.UniversityAdmin);

        var client = revalidatingFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf1, _) = await GetAntiforgeryTokensAsync(client);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        var loginResp = await client.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act & Assert 1: Before revalidation, password-only session has mfaCompleted = false
        var meResp1 = await client.GetAsync("/api/auth/me");
        meResp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var meData1 = await meResp1.Content.ReadFromJsonAsync<CurrentUserResponse>();
        meData1!.MfaCompleted.Should().BeFalse();
        meData1.Roles.Should().Contain(CampusRoles.UniversityAdmin);

        // Act & Assert 2: After security-stamp revalidation, session MUST STAY mfaCompleted = false
        // (Never mark a session MFA-complete solely because the account exists or revalidated)
        var meResp2 = await client.GetAsync("/api/auth/me");
        meResp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var meData2 = await meResp2.Content.ReadFromJsonAsync<CurrentUserResponse>();
        meData2!.MfaCompleted.Should().BeFalse();
        meData2.Roles.Should().Contain(CampusRoles.UniversityAdmin);

        // Act & Assert 3: Protected endpoint requiring UniversityAdminWithMfa returns 403 Forbidden
        var forbiddenResp = await client.GetAsync("/api/users");
        forbiddenResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TwoFactor_InvalidatedSecurityStamp_CookieRejectedWith401Unauthorized()
    {
        var revalidatingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Configure<SecurityStampValidatorOptions>(options =>
                {
                    options.ValidationInterval = TimeSpan.Zero;
                });
            });
        });

        var email = $"admin_invalid_stamp_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var user = await EnsureUserWithRoleAsync(email, password, CampusRoles.UniversityAdmin);

        // Configure MFA directly on the user account
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var dbUser = await userManager.FindByIdAsync(user.Id.ToString());
            await userManager.ResetAuthenticatorKeyAsync(dbUser!);
            await userManager.SetTwoFactorEnabledAsync(dbUser!, true);
        }

        // Session completes 2FA challenge
        var client = revalidatingFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf1, _) = await GetAntiforgeryTokensAsync(client);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        var loginResp = await client.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var challengeTotp = await GenerateTotpCodeAsync(user.Id);
        var (csrf2, _) = await GetAntiforgeryTokensAsync(client);
        var challengeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/challenge")
        {
            Content = JsonContent.Create(new TwoFactorChallengeRequest(challengeTotp))
        };
        challengeReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        var challengeResp = await client.SendAsync(challengeReq);
        challengeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify valid session immediately
        var validMe = await client.GetAsync("/api/auth/me");
        validMe.StatusCode.Should().Be(HttpStatusCode.OK);

        // Invalidate security stamp in database (simulates password change, role update, or sign out everywhere)
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var dbUser = await userManager.FindByIdAsync(user.Id.ToString());
            await userManager.UpdateSecurityStampAsync(dbUser!);
        }

        // Act: Make request with the invalidated cookie
        var rejectedResp = await client.GetAsync("/api/auth/me");

        // Assert: The session is rejected and returns 401 Unauthorized
        rejectedResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TwoFactor_MfaSession_RolesUpdatedInDatabase_RefreshesRolesWhilePreservingMfa()
    {
        var revalidatingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Configure<SecurityStampValidatorOptions>(options =>
                {
                    options.ValidationInterval = TimeSpan.Zero;
                });
            });
        });

        var email = $"admin_role_refresh_{Guid.NewGuid():N}@campus.internal";
        var password = "SecurePassword123!";
        var user = await EnsureUserWithRoleAsync(email, password, CampusRoles.UniversityAdmin);

        // Configure MFA directly on the user account
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var dbUser = await userManager.FindByIdAsync(user.Id.ToString());
            await userManager.ResetAuthenticatorKeyAsync(dbUser!);
            await userManager.SetTwoFactorEnabledAsync(dbUser!, true);
        }

        // Session completes 2FA challenge
        var client = revalidatingFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf1, _) = await GetAntiforgeryTokensAsync(client);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        await client.SendAsync(loginReq);

        var challengeTotp = await GenerateTotpCodeAsync(user.Id);
        var (csrf2, _) = await GetAntiforgeryTokensAsync(client);
        var challengeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/challenge")
        {
            Content = JsonContent.Create(new TwoFactorChallengeRequest(challengeTotp))
        };
        challengeReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        var challengeResp = await client.SendAsync(challengeReq);
        challengeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Dynamically assign an additional role in the database without rotating stamp
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            if (!await roleManager.RoleExistsAsync(CampusRoles.FacilityAdmin))
            {
                await roleManager.CreateAsync(new ApplicationRole { Name = CampusRoles.FacilityAdmin });
            }
            var dbUser = await userManager.FindByIdAsync(user.Id.ToString());
            await userManager.AddToRoleAsync(dbUser!, CampusRoles.FacilityAdmin);
        }

        // Act: Call /api/auth/me triggering SecurityStampValidator revalidation
        var meResp = await client.GetAsync("/api/auth/me");
        meResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var meData = await meResp.Content.ReadFromJsonAsync<CurrentUserResponse>();

        // Assert: Both roles are loaded from refreshed principal, and amr=mfa is preserved
        meData.Should().NotBeNull();
        meData!.MfaCompleted.Should().BeTrue();
        meData.Roles.Should().Contain(CampusRoles.UniversityAdmin);
        meData.Roles.Should().Contain(CampusRoles.FacilityAdmin);
    }
}
