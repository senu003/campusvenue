using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Modules.Identity.Domain;
using CampusVenue.Api.Modules.Identity.Features.Auth;
using CampusVenue.Api.Modules.Identity.Features.TwoFactor;
using CampusVenue.Api.Modules.Identity.Features.Users;
using CampusVenue.Api.Modules.Identity.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CampusVenue.Tests;

public class UserDirectoryIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UserDirectoryIntegrationTests(TestWebApplicationFactory factory)
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
            await roleManager.CreateAsync(new ApplicationRole
            {
                Id = Guid.NewGuid(),
                Name = role,
                NormalizedName = role.ToUpperInvariant(),
                Description = CampusRoles.GetDescription(role),
                CreatedAtUtc = DateTime.UtcNow
            });
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

    private static string ComputeTotp(string base32Secret)
    {
        var keyBytes = Base32Decode(base32Secret);
        long timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var timeBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(timeStep));

        using var hmac = new System.Security.Cryptography.HMACSHA1(keyBytes);
        var hash = hmac.ComputeHash(timeBytes);

        int offset = hash[^1] & 0x0F;
        int binaryCode = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        int totp = binaryCode % 1_000_000;
        return totp.ToString("D6");
    }

    private static byte[] Base32Decode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var clean = base32.Trim().TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>();

        int buffer = 0;
        int bitsLeft = 0;

        foreach (char c in clean)
        {
            int val = alphabet.IndexOf(c);
            if (val < 0) continue;

            buffer = (buffer << 5) | val;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bytes.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }

        return [.. bytes];
    }

    private async Task<HttpClient> CreateSessionWithCompletedMfaAsync(string email, string password, string role)
    {
        var user = await EnsureUserWithRoleAsync(email, password, role, emailConfirmed: true);

        // 1. Initial login with password to enroll MFA
        var setupClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf1, _) = await GetAntiforgeryTokensAsync(setupClient);

        var loginReq1 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq1.Headers.Add("X-XSRF-TOKEN", csrf1);
        var loginResp1 = await setupClient.SendAsync(loginReq1);
        loginResp1.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Setup key
        var setupResp = await setupClient.GetAsync("/api/auth/2fa/setup");
        setupResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Enable MFA
        var totpCode = await GenerateTotpCodeAsync(user.Id);
        var (csrf2, _) = await GetAntiforgeryTokensAsync(setupClient);
        var enableReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(password, totpCode))
        };
        enableReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        var enableResp = await setupClient.SendAsync(enableReq);
        enableResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Fresh client performs sign-in going through 2FA challenge
        var mfaClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf3, _) = await GetAntiforgeryTokensAsync(mfaClient);
        var loginReq2 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq2.Headers.Add("X-XSRF-TOKEN", csrf3);
        var loginResp2 = await mfaClient.SendAsync(loginReq2);
        loginResp2.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginData2 = await loginResp2.Content.ReadFromJsonAsync<LoginResponse>();
        loginData2.Should().NotBeNull();
        loginData2!.RequiresTwoFactor.Should().BeTrue();

        // 5. Submit challenge code
        var challengeTotp = await GenerateTotpCodeAsync(user.Id);
        var (csrf4, _) = await GetAntiforgeryTokensAsync(mfaClient);
        var challengeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/challenge")
        {
            Content = JsonContent.Create(new TwoFactorChallengeRequest(challengeTotp))
        };
        challengeReq.Headers.Add("X-XSRF-TOKEN", csrf4);
        var challengeResp = await mfaClient.SendAsync(challengeReq);
        challengeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        return mfaClient;
    }

    [Fact]
    public async Task UserDirectory_SignedOut_Returns401Unauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UserDirectory_PendingMfaUser_Returns401Unauthorized()
    {
        // Arrange: Account with MFA enabled attempts login (leaves session in pending-MFA state)
        var email = $"pendingmfa_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var user = await EnsureUserWithRoleAsync(email, password, CampusRoles.UniversityAdmin, emailConfirmed: true);

        // Enable MFA for this user
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var dbUser = await userManager.FindByIdAsync(user.Id.ToString());
            await userManager.ResetAuthenticatorKeyAsync(dbUser!);
            await userManager.SetTwoFactorEnabledAsync(dbUser!, true);
        }

        var pendingClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf, _) = await GetAntiforgeryTokensAsync(pendingClient);

        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf);
        var loginResp = await pendingClient.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act: Attempt to access user directory with pending-MFA cookie only
        var response = await pendingClient.GetAsync("/api/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UserDirectory_ParticipantWithMfa_Returns403Forbidden()
    {
        // Arrange: Standard Participant who has completed MFA
        var email = $"participant_mfa_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var participantClient = await CreateSessionWithCompletedMfaAsync(email, password, CampusRoles.Participant);

        // Act
        var response = await participantClient.GetAsync("/api/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UserDirectory_AdminWithoutMfa_Returns403Forbidden()
    {
        // Arrange: Admin account that signed in with password only (no completed MFA on session)
        var email = $"admin_nomfa_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        await EnsureUserWithRoleAsync(email, password, CampusRoles.UniversityAdmin, emailConfirmed: true);

        var adminClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf, _) = await GetAntiforgeryTokensAsync(adminClient);

        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf);
        var loginResp = await adminClient.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act: Admin has UniversityAdmin role, but session does not have completed MFA
        var response = await adminClient.GetAsync("/api/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UserDirectory_AdminWhoEnrolledMfaWithoutRelogging_Returns403Forbidden()
    {
        // Arrange: Admin signs in with password, enables MFA in My Account, but has not completed MFA sign-in challenge
        var email = $"admin_enrollonly_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var user = await EnsureUserWithRoleAsync(email, password, CampusRoles.UniversityAdmin, emailConfirmed: true);

        var adminClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf1, _) = await GetAntiforgeryTokensAsync(adminClient);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        await adminClient.SendAsync(loginReq);

        // Call setup & enable in this session
        await adminClient.GetAsync("/api/auth/2fa/setup");
        var totp = await GenerateTotpCodeAsync(user.Id);

        var (csrf2, _) = await GetAntiforgeryTokensAsync(adminClient);
        var enableReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(password, totp))
        };
        enableReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        var enableResp = await adminClient.SendAsync(enableReq);
        enableResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act: Session was established before MFA; enrollment alone does not satisfy the policy
        var response = await adminClient.GetAsync("/api/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UserDirectory_AdminWithCompletedMfa_Returns200OK_AndUserDirectory()
    {
        // Arrange: Admin with UniversityAdmin role AND a completed MFA session
        var email = $"admin_mfadone_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var adminClient = await CreateSessionWithCompletedMfaAsync(email, password, CampusRoles.UniversityAdmin);

        // Act
        var response = await adminClient.GetAsync("/api/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var data = await response.Content.ReadFromJsonAsync<UserDirectoryResponse>();
        data.Should().NotBeNull();
        data!.Items.Should().NotBeEmpty();
        data.Page.Should().Be(1);
        data.PageSize.Should().Be(10);
        data.TotalCount.Should().BeGreaterThan(0);
        data.TotalPages.Should().BeGreaterThan(0);

        // Verify specific admin by searching to confirm safe field contents
        var searchResp = await adminClient.GetAsync($"/api/users?search={email}");
        searchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchData = await searchResp.Content.ReadFromJsonAsync<UserDirectoryResponse>();
        searchData.Should().NotBeNull();
        var currentAdmin = searchData!.Items.FirstOrDefault(u => u.Email == email);
        currentAdmin.Should().NotBeNull();
        currentAdmin!.Roles.Should().Contain(CampusRoles.UniversityAdmin);
        currentAdmin.TwoFactorEnabled.Should().BeTrue();
        currentAdmin.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task UserDirectory_GetCurrentUser_ReturnsRolesAndMfaStatus()
    {
        // Arrange: Admin with completed MFA
        var email = $"admin_me_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var adminClient = await CreateSessionWithCompletedMfaAsync(email, password, CampusRoles.UniversityAdmin);

        // Act
        var meResp = await adminClient.GetAsync("/api/auth/me");

        // Assert
        meResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var meData = await meResp.Content.ReadFromJsonAsync<CurrentUserResponse>();
        meData.Should().NotBeNull();
        meData!.IsAuthenticated.Should().BeTrue();
        meData.TwoFactorEnabled.Should().BeTrue();
        meData.MfaCompleted.Should().BeTrue();
        meData.Roles.Should().Contain(CampusRoles.UniversityAdmin);
    }

    [Fact]
    public async Task UserDirectory_PublicRegistration_AssignsParticipantRole_CannotGrantAdministratorAccess()
    {
        // Arrange: Register a new user via public self-service registration
        var email = $"new_student_{Guid.NewGuid():N}@campus.internal";
        var password = "StudentPassword123!";

        var registerClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf, _) = await GetAntiforgeryTokensAsync(registerClient);

        var registerReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new RegisterRequest(email, password, password))
        };
        registerReq.Headers.Add("X-XSRF-TOKEN", csrf);

        // Act
        var registerResp = await registerClient.SendAsync(registerReq);
        registerResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert in Database: user has zero elevated roles and standard participant access
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            user.Should().NotBeNull();

            var roles = await userManager.GetRolesAsync(user!);
            roles.Should().NotContain(CampusRoles.UniversityAdmin);
            roles.Should().NotContain(CampusRoles.FacilityAdmin);
            roles.Should().NotContain(CampusRoles.EventCoordinator);
        }
    }

    [Fact]
    public async Task Operator_GrantUniversityAdmin_ExistingConfirmedAccount_Succeeds()
    {
        // Arrange: Create confirmed participant user
        var email = $"operator_promote_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        await EnsureUserWithRoleAsync(email, password, CampusRoles.Participant, emailConfirmed: true);

        // Act: Execute operator grant utility
        var (succeeded, error) = await OperatorRoleProvisioner.GrantUniversityAdminAsync(_factory.Services, email);

        // Assert
        succeeded.Should().BeTrue();
        error.Should().BeNull();

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();

        var roles = await userManager.GetRolesAsync(user!);
        roles.Should().Contain(CampusRoles.UniversityAdmin);
    }

    [Fact]
    public async Task Operator_GrantUniversityAdmin_NonExistentAccount_FailsGracefully()
    {
        // Act
        var (succeeded, error) = await OperatorRoleProvisioner.GrantUniversityAdminAsync(
            _factory.Services,
            "nonexistent_operator_user@campus.internal");

        // Assert
        succeeded.Should().BeFalse();
        error.Should().Contain("does not exist");
    }

    [Fact]
    public async Task Operator_GrantUniversityAdmin_UnconfirmedAccount_FailsGracefully()
    {
        // Arrange: Create unconfirmed user
        var email = $"unconfirmed_admin_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        await EnsureUserWithRoleAsync(email, password, CampusRoles.Participant, emailConfirmed: false);

        // Act
        var (succeeded, error) = await OperatorRoleProvisioner.GrantUniversityAdminAsync(_factory.Services, email);

        // Assert
        succeeded.Should().BeFalse();
        error.Should().Contain("has not confirmed its email address");
    }

    [Fact]
    public async Task UserDirectory_PaginationAndSearch_FiltersCorrectly()
    {
        // Arrange: Admin with MFA
        var adminEmail = $"admin_search_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var adminClient = await CreateSessionWithCompletedMfaAsync(adminEmail, password, CampusRoles.UniversityAdmin);

        // Ensure unique user for search
        var searchTargetEmail = $"target_search_{Guid.NewGuid():N}@campus.internal";
        await EnsureUserWithRoleAsync(searchTargetEmail, password, CampusRoles.Participant, emailConfirmed: true);

        // Act: Search for the target user
        var searchResp = await adminClient.GetAsync($"/api/users?search={searchTargetEmail}&page=1&pageSize=10");
        searchResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var searchData = await searchResp.Content.ReadFromJsonAsync<UserDirectoryResponse>();
        searchData.Should().NotBeNull();
        searchData!.Items.Should().ContainSingle();
        searchData.Items[0].Email.Should().Be(searchTargetEmail);
        searchData.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task UserDirectory_AdminWithCompletedMfa_CanRequestBothWithAndWithoutTrailingSlash()
    {
        // Arrange
        var adminEmail = $"admin_slash_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var adminClient = await CreateSessionWithCompletedMfaAsync(adminEmail, password, CampusRoles.UniversityAdmin);

        // Act & Assert 1: Without trailing slash (/api/users)
        var respNoSlash = await adminClient.GetAsync("/api/users");
        respNoSlash.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act & Assert 2: With trailing slash (/api/users/)
        var respWithSlash = await adminClient.GetAsync("/api/users/");
        respWithSlash.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Operator_ProcessOperatorCommands_WithGrantAdminEmailArg_ExecutesAndReturnsTrue()
    {
        // Arrange
        var email = $"operator_cli_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        await EnsureUserWithRoleAsync(email, password, CampusRoles.Participant, emailConfirmed: true);

        var logger = _factory.Services.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("Test");

        // Act: Operator supplies --grant-admin-email via CLI args
        var isOperatorCommand = await OperatorRoleProvisioner.ProcessOperatorCommandsAsync(
            _factory.Services,
            ["--grant-admin-email", email],
            logger);

        // Assert: Command indicates it handled an operator action and should exit
        isOperatorCommand.Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();

        var roles = await userManager.GetRolesAsync(user!);
        roles.Should().Contain(CampusRoles.UniversityAdmin);
    }

    [Fact]
    public async Task Operator_ProcessOperatorCommands_WithoutGrantAdminEmailArg_ReturnsFalse_AllowingNormalStartup()
    {
        // Arrange
        var logger = _factory.Services.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("Test");

        // Act: Regular startup without operator commands
        var isOperatorCommand = await OperatorRoleProvisioner.ProcessOperatorCommandsAsync(
            _factory.Services,
            ["--urls", "http://localhost:5000"],
            logger);

        // Assert: Returns false so regular WebApplication startup proceeds
        isOperatorCommand.Should().BeFalse();
    }

    [Fact]
    public async Task Operator_ProcessOperatorCommands_IgnoresEnvironmentVariables_OnlyActsOnExplicitArg()
    {
        // Arrange: An existing participant account and an environment variable set
        var email = $"env_test_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        await EnsureUserWithRoleAsync(email, password, CampusRoles.Participant, emailConfirmed: true);

        Environment.SetEnvironmentVariable("GRANT_ADMIN_EMAIL", email);
        try
        {
            var logger = _factory.Services.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("Test");

            // Act: Normal server startup without CLI args (even if GRANT_ADMIN_EMAIL env var is set)
            var isOperatorCommand = await OperatorRoleProvisioner.ProcessOperatorCommandsAsync(
                _factory.Services,
                [],
                logger);

            // Assert: Does not execute operator exit, does NOT elevate account
            isOperatorCommand.Should().BeFalse();

            using var scope = _factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            user.Should().NotBeNull();

            var roles = await userManager.GetRolesAsync(user!);
            roles.Should().NotContain(CampusRoles.UniversityAdmin);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GRANT_ADMIN_EMAIL", null);
        }
    }
}
