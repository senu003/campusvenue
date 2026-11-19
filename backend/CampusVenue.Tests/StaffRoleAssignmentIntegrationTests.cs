using System.Net;
using System.Net.Http.Json;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Modules.Identity.Domain;
using CampusVenue.Api.Modules.Identity.Features.Auth;
using CampusVenue.Api.Modules.Identity.Features.TwoFactor;
using CampusVenue.Api.Modules.Identity.Features.Users;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CampusVenue.Tests;

/// <summary>
/// Interceptor to simulate a failure when saving audit records in PostgreSQL.
/// Verifies complete transaction rollback across user roles and security stamps.
/// </summary>
public class FailOnAuditSaveInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null)
        {
            var auditEntries = eventData.Context.ChangeTracker.Entries<RoleAssignmentAudit>().ToList();
            if (auditEntries.Count > 0)
            {
                throw new DbUpdateException("Simulated audit write failure for PostgreSQL rollback verification.", new Exception("Simulated inner error"));
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

public class RollbackTestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestDatabaseHelper.EnsureMigrated();
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
                options.AddInterceptors(new FailOnAuditSaveInterceptor());
            });
        });

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDatabaseHelper.ConnectionString,
                ["RateLimiting:AuthPermitLimit"] = "200",
                ["DataProtection:KeyPath"] = Path.Combine(Path.GetTempPath(), "campusvenue_dpkeys_rollback_test")
            });
        });
    }
}

public class StaffRoleAssignmentIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public StaffRoleAssignmentIntegrationTests(TestWebApplicationFactory factory)
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
    public async Task RoleAssignment_SignedOut_Returns401Unauthorized()
    {
        // Arrange
        var targetId = Guid.NewGuid();
        var (csrf, _) = await GetAntiforgeryTokensAsync(_client);

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetId}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest([CampusRoles.FacilityAdmin]))
        };
        request.Headers.Add("X-XSRF-TOKEN", csrf);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RoleAssignment_ParticipantWithMfa_Returns403Forbidden()
    {
        // Arrange: Participant with MFA
        var participantEmail = $"part_roles_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var participantClient = await CreateSessionWithCompletedMfaAsync(participantEmail, password, CampusRoles.Participant);
        var (csrf, _) = await GetAntiforgeryTokensAsync(participantClient);

        var targetId = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetId}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest([CampusRoles.FacilityAdmin]))
        };
        request.Headers.Add("X-XSRF-TOKEN", csrf);

        // Act
        var response = await participantClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RoleAssignment_AdminWithoutMfa_Returns403Forbidden()
    {
        // Arrange: Admin signed in with password only (no completed MFA)
        var adminEmail = $"admin_nomfa_roles_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        await EnsureUserWithRoleAsync(adminEmail, password, CampusRoles.UniversityAdmin, emailConfirmed: true);

        var adminClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf, _) = await GetAntiforgeryTokensAsync(adminClient);

        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(adminEmail, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf);
        await adminClient.SendAsync(loginReq);

        var targetId = Guid.NewGuid();
        var (csrf2, _) = await GetAntiforgeryTokensAsync(adminClient);
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetId}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest([CampusRoles.FacilityAdmin]))
        };
        request.Headers.Add("X-XSRF-TOKEN", csrf2);

        // Act
        var response = await adminClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RoleAssignment_MissingAntiforgeryToken_Returns400BadRequest()
    {
        // Arrange: Admin with MFA but missing CSRF token
        var adminEmail = $"admin_nocsrf_roles_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var adminClient = await CreateSessionWithCompletedMfaAsync(adminEmail, password, CampusRoles.UniversityAdmin);

        var targetId = Guid.NewGuid();
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetId}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest([CampusRoles.FacilityAdmin]))
        };
        // Omit X-XSRF-TOKEN header

        // Act
        var response = await adminClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RoleAssignment_AttemptToGrantUniversityAdmin_Returns400BadRequest()
    {
        // Arrange: Admin with MFA attempts to grant UniversityAdmin through the API
        var adminEmail = $"admin_grant_ua_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var adminClient = await CreateSessionWithCompletedMfaAsync(adminEmail, password, CampusRoles.UniversityAdmin);

        var targetEmail = $"target_ua_{Guid.NewGuid():N}@campus.internal";
        var targetUser = await EnsureUserWithRoleAsync(targetEmail, password, CampusRoles.Participant, emailConfirmed: true);

        var (csrf, _) = await GetAntiforgeryTokensAsync(adminClient);
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetUser.Id}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest([CampusRoles.UniversityAdmin]))
        };
        request.Headers.Add("X-XSRF-TOKEN", csrf);

        // Act
        var response = await adminClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errorContent = await response.Content.ReadAsStringAsync();
        errorContent.Should().Contain("UniversityAdmin");

        // Verify target user was NOT granted UniversityAdmin
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(targetUser.Id.ToString());
        var roles = await userManager.GetRolesAsync(user!);
        roles.Should().NotContain(CampusRoles.UniversityAdmin);
    }

    [Fact]
    public async Task RoleAssignment_AttemptToModifyUniversityAdminAccount_Returns400BadRequest()
    {
        // Arrange: Admin attempts to modify another UniversityAdmin account's roles
        var adminEmail = $"admin_actor_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var adminClient = await CreateSessionWithCompletedMfaAsync(adminEmail, password, CampusRoles.UniversityAdmin);

        var targetAdminEmail = $"admin_target_{Guid.NewGuid():N}@campus.internal";
        var targetAdmin = await EnsureUserWithRoleAsync(targetAdminEmail, password, CampusRoles.UniversityAdmin, emailConfirmed: true);

        var (csrf, _) = await GetAntiforgeryTokensAsync(adminClient);
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetAdmin.Id}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest([CampusRoles.FacilityAdmin]))
        };
        request.Headers.Add("X-XSRF-TOKEN", csrf);

        // Act
        var response = await adminClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errorContent = await response.Content.ReadAsStringAsync();
        errorContent.Should().Contain("UniversityAdmin");
    }

    [Fact]
    public async Task RoleAssignment_InvalidRoleName_Returns400BadRequest()
    {
        // Arrange
        var adminEmail = $"admin_invalid_role_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var adminClient = await CreateSessionWithCompletedMfaAsync(adminEmail, password, CampusRoles.UniversityAdmin);

        var targetEmail = $"target_inv_{Guid.NewGuid():N}@campus.internal";
        var targetUser = await EnsureUserWithRoleAsync(targetEmail, password, CampusRoles.Participant, emailConfirmed: true);

        var (csrf, _) = await GetAntiforgeryTokensAsync(adminClient);
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetUser.Id}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest(["NonExistentRole"]))
        };
        request.Headers.Add("X-XSRF-TOKEN", csrf);

        // Act
        var response = await adminClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errorContent = await response.Content.ReadAsStringAsync();
        errorContent.Should().Contain("NonExistentRole");
    }

    [Fact]
    public async Task RoleAssignment_GrantAndRemoveRoles_Succeeds_RotatesSecurityStamp_AndWritesAuditRecord()
    {
        // Arrange
        var adminEmail = $"admin_grant_test_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var adminClient = await CreateSessionWithCompletedMfaAsync(adminEmail, password, CampusRoles.UniversityAdmin);

        var targetEmail = $"target_grant_test_{Guid.NewGuid():N}@campus.internal";
        var targetUser = await EnsureUserWithRoleAsync(targetEmail, password, CampusRoles.Participant, emailConfirmed: true);

        string originalStamp = targetUser.SecurityStamp!;

        // --- Step 1: Grant FacilityAdmin ---
        var (csrf1, _) = await GetAntiforgeryTokensAsync(adminClient);
        var grantReq = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetUser.Id}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest([CampusRoles.FacilityAdmin], targetUser.ConcurrencyStamp))
        };
        grantReq.Headers.Add("X-XSRF-TOKEN", csrf1);

        var grantResp = await adminClient.SendAsync(grantReq);
        grantResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var grantData = await grantResp.Content.ReadFromJsonAsync<AssignUserRolesResponse>();
        grantData.Should().NotBeNull();
        grantData!.Roles.Should().ContainSingle().Which.Should().Be(CampusRoles.FacilityAdmin);
        grantData.ConcurrencyStamp.Should().NotBeNullOrWhiteSpace();

        // Verify Database State after Grant
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var dbUser = await userManager.FindByIdAsync(targetUser.Id.ToString());
            dbUser.Should().NotBeNull();
            dbUser!.SecurityStamp.Should().NotBe(originalStamp, "SecurityStamp must rotate when roles are changed");

            var userRoles = await userManager.GetRolesAsync(dbUser);
            userRoles.Should().Contain(CampusRoles.FacilityAdmin);
            userRoles.Should().NotContain(CampusRoles.EventCoordinator);

            // Verify Audit record was written
            var audits = await dbContext.RoleAssignmentAudits
                .Where(a => a.TargetUserId == targetUser.Id)
                .OrderBy(a => a.CreatedAtUtc)
                .ToListAsync();

            audits.Should().ContainSingle();
            var audit1 = audits[0];
            audit1.PreviousRoles.Should().ContainSingle().Which.Should().Be(CampusRoles.Participant);
            audit1.NewRoles.Should().ContainSingle().Which.Should().Be(CampusRoles.FacilityAdmin);
            audit1.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
        }

        // --- Step 2: Grant EventCoordinator (so user has BOTH staff roles) ---
        var (csrf2, _) = await GetAntiforgeryTokensAsync(adminClient);
        var grantBothReq = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetUser.Id}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest(
                [CampusRoles.FacilityAdmin, CampusRoles.EventCoordinator],
                grantData.ConcurrencyStamp))
        };
        grantBothReq.Headers.Add("X-XSRF-TOKEN", csrf2);

        var grantBothResp = await adminClient.SendAsync(grantBothReq);
        grantBothResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var bothData = await grantBothResp.Content.ReadFromJsonAsync<AssignUserRolesResponse>();
        bothData.Should().NotBeNull();
        bothData!.Roles.Should().BeEquivalentTo([CampusRoles.FacilityAdmin, CampusRoles.EventCoordinator]);

        // --- Step 3: Remove All Staff Roles (revert to Participant) ---
        var (csrf3, _) = await GetAntiforgeryTokensAsync(adminClient);
        var removeAllReq = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetUser.Id}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest(
                [], // Empty list reverts to standard Participant access
                bothData.ConcurrencyStamp))
        };
        removeAllReq.Headers.Add("X-XSRF-TOKEN", csrf3);

        var removeAllResp = await adminClient.SendAsync(removeAllReq);
        removeAllResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var removeData = await removeAllResp.Content.ReadFromJsonAsync<AssignUserRolesResponse>();
        removeData.Should().NotBeNull();
        removeData!.Roles.Should().ContainSingle().Which.Should().Be(CampusRoles.Participant);

        // Verify Database State after Removal
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var dbUser = await userManager.FindByIdAsync(targetUser.Id.ToString());
            var userRoles = await userManager.GetRolesAsync(dbUser!);
            userRoles.Should().NotContain(CampusRoles.FacilityAdmin);
            userRoles.Should().NotContain(CampusRoles.EventCoordinator);

            var audits = await dbContext.RoleAssignmentAudits
                .Where(a => a.TargetUserId == targetUser.Id)
                .OrderBy(a => a.CreatedAtUtc)
                .ToListAsync();

            audits.Should().HaveCount(3);
            var lastAudit = audits.Last();
            lastAudit.PreviousRoles.Should().BeEquivalentTo([CampusRoles.FacilityAdmin, CampusRoles.EventCoordinator]);
            lastAudit.NewRoles.Should().ContainSingle().Which.Should().Be(CampusRoles.Participant);
        }
    }

    [Fact]
    public async Task RoleAssignment_DuplicateSubmission_ReturnsNoOp_WithoutDuplicateAuditRecord()
    {
        // Arrange: User already has FacilityAdmin
        var adminEmail = $"admin_dup_test_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var adminClient = await CreateSessionWithCompletedMfaAsync(adminEmail, password, CampusRoles.UniversityAdmin);

        var targetEmail = $"target_dup_test_{Guid.NewGuid():N}@campus.internal";
        var targetUser = await EnsureUserWithRoleAsync(targetEmail, password, CampusRoles.Participant, emailConfirmed: true);

        // Step 1: Assign FacilityAdmin
        var (csrf1, _) = await GetAntiforgeryTokensAsync(adminClient);
        var req1 = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetUser.Id}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest([CampusRoles.FacilityAdmin], targetUser.ConcurrencyStamp))
        };
        req1.Headers.Add("X-XSRF-TOKEN", csrf1);
        var resp1 = await adminClient.SendAsync(req1);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);

        string stampAfterFirstAssignment;
        int auditCountAfterFirst;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var dbUser = await dbContext.Users.FindAsync(targetUser.Id);
            stampAfterFirstAssignment = dbUser!.SecurityStamp!;
            auditCountAfterFirst = await dbContext.RoleAssignmentAudits.CountAsync(a => a.TargetUserId == targetUser.Id);
        }
        auditCountAfterFirst.Should().Be(1);

        // Step 2: Send duplicate request requesting the exact same role set (FacilityAdmin)
        var (csrf2, _) = await GetAntiforgeryTokensAsync(adminClient);
        var duplicateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetUser.Id}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest([CampusRoles.FacilityAdmin]))
        };
        duplicateReq.Headers.Add("X-XSRF-TOKEN", csrf2);

        var duplicateResp = await adminClient.SendAsync(duplicateReq);

        // Assert: Successful no-op (200 OK)
        duplicateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dupData = await duplicateResp.Content.ReadFromJsonAsync<AssignUserRolesResponse>();
        dupData.Should().NotBeNull();
        dupData!.Roles.Should().ContainSingle().Which.Should().Be(CampusRoles.FacilityAdmin);
        dupData.Message.Should().Contain("up to date");

        // Verify NO duplicate audit record was created and security stamp was not rotated
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var dbUser = await dbContext.Users.FindAsync(targetUser.Id);
            dbUser!.SecurityStamp.Should().Be(stampAfterFirstAssignment, "SecurityStamp must not rotate on a duplicate no-op retry");

            var auditCountAfterDuplicate = await dbContext.RoleAssignmentAudits.CountAsync(a => a.TargetUserId == targetUser.Id);
            auditCountAfterDuplicate.Should().Be(auditCountAfterFirst, "No duplicate audit record must be written on a no-op retry");
        }
    }

    [Fact]
    public async Task RoleAssignment_ConflictingEdits_StaleConcurrencyStamp_Returns409Conflict()
    {
        // Arrange
        var adminEmail = $"admin_conflict_test_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var adminClient = await CreateSessionWithCompletedMfaAsync(adminEmail, password, CampusRoles.UniversityAdmin);

        var targetEmail = $"target_conflict_{Guid.NewGuid():N}@campus.internal";
        var targetUser = await EnsureUserWithRoleAsync(targetEmail, password, CampusRoles.Participant, emailConfirmed: true);

        // Capture initial concurrency stamp (Admin A's view)
        string staleStamp = targetUser.ConcurrencyStamp!;

        // Admin B modifies target user to EventCoordinator
        string adminBStamp;
        string adminBSecurityStamp;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var dbUser = await dbContext.Users.FindAsync(targetUser.Id);
            var ecRole = await dbContext.Roles.FirstAsync(r => r.Name == CampusRoles.EventCoordinator);

            dbContext.UserRoles.Add(new IdentityUserRole<Guid> { UserId = targetUser.Id, RoleId = ecRole.Id });
            dbUser!.ConcurrencyStamp = Guid.NewGuid().ToString("D");
            dbUser.SecurityStamp = Guid.NewGuid().ToString("D");
            await dbContext.SaveChangesAsync();
            adminBStamp = dbUser.ConcurrencyStamp;
            adminBSecurityStamp = dbUser.SecurityStamp;
        }

        // Admin A now submits with their stale concurrency stamp attempting to set FacilityAdmin
        var (csrf, _) = await GetAntiforgeryTokensAsync(adminClient);
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetUser.Id}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest(
                [CampusRoles.FacilityAdmin],
                ConcurrencyStamp: staleStamp))
        };
        request.Headers.Add("X-XSRF-TOKEN", csrf);

        // Act
        var response = await adminClient.SendAsync(request);

        // Assert: HTTP 409 Conflict
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Verify target user's roles and security stamp were NOT overwritten and newer change is preserved
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var dbUser = await userManager.FindByIdAsync(targetUser.Id.ToString());
            var roles = await userManager.GetRolesAsync(dbUser!);
            roles.Should().Contain(CampusRoles.EventCoordinator, "Newer change by Admin B must be preserved");
            roles.Should().NotContain(CampusRoles.FacilityAdmin, "Stale request must not overwrite roles");
            dbUser!.ConcurrencyStamp.Should().Be(adminBStamp, "Concurrency stamp from newer change must be preserved");
            dbUser.SecurityStamp.Should().Be(adminBSecurityStamp, "Security stamp from newer change must be preserved");

            var auditCount = await dbContext.RoleAssignmentAudits.CountAsync(a => a.TargetUserId == targetUser.Id);
            auditCount.Should().Be(0, "Stale conflict request must not create an audit record");
        }
    }

    [Fact]
    public async Task RoleAssignment_AuditWriteFailure_RollsBackEntireTransaction()
    {
        // Arrange: Use RollbackTestWebApplicationFactory where saving RoleAssignmentAudit throws
        await using var rollbackFactory = new RollbackTestWebApplicationFactory();

        var adminEmail = $"admin_rb_test_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";

        // Provision users directly in database
        Guid targetUserId;
        string originalSecurityStamp;
        string originalConcurrencyStamp;
        using (var scope = rollbackFactory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

            // Ensure UniversityAdmin role
            if (!await roleManager.RoleExistsAsync(CampusRoles.UniversityAdmin))
            {
                await roleManager.CreateAsync(new ApplicationRole
                {
                    Id = Guid.NewGuid(),
                    Name = CampusRoles.UniversityAdmin,
                    NormalizedName = CampusRoles.UniversityAdmin.ToUpperInvariant(),
                    Description = CampusRoles.GetDescription(CampusRoles.UniversityAdmin)
                });
            }

            // Create target user
            var targetUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = $"target_rb_{Guid.NewGuid():N}@campus.internal",
                Email = $"target_rb_{Guid.NewGuid():N}@campus.internal",
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString("D"),
                ConcurrencyStamp = Guid.NewGuid().ToString("D")
            };
            await userManager.CreateAsync(targetUser, password);
            targetUserId = targetUser.Id;
            originalSecurityStamp = targetUser.SecurityStamp;
            originalConcurrencyStamp = targetUser.ConcurrencyStamp;
        }

        // Establish admin session with MFA in rollback factory
        var adminClient = rollbackFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using (var scope = rollbackFactory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var adminUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true
            };
            await userManager.CreateAsync(adminUser, password);
            await userManager.AddToRoleAsync(adminUser, CampusRoles.UniversityAdmin);
            await userManager.SetTwoFactorEnabledAsync(adminUser, true);
        }

        // Login Admin
        var (csrf1, _) = await GetAntiforgeryTokensAsync(adminClient);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(adminEmail, password))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        var loginResp = await adminClient.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Challenge TOTP
        using (var scope = rollbackFactory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var dbAdmin = await userManager.FindByEmailAsync(adminEmail);
            await userManager.ResetAuthenticatorKeyAsync(dbAdmin!);
            var key = await userManager.GetAuthenticatorKeyAsync(dbAdmin!);
            var totp = ComputeTotp(key!);

            var (csrf2, _) = await GetAntiforgeryTokensAsync(adminClient);
            var challengeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/challenge")
            {
                Content = JsonContent.Create(new TwoFactorChallengeRequest(totp))
            };
            challengeReq.Headers.Add("X-XSRF-TOKEN", csrf2);
            var challengeResp = await adminClient.SendAsync(challengeReq);
            challengeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Act: Attempt to assign FacilityAdmin (interceptor will throw during SaveChangesAsync)
        var (csrf3, _) = await GetAntiforgeryTokensAsync(adminClient);
        var assignReq = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetUserId}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest([CampusRoles.FacilityAdmin], originalConcurrencyStamp))
        };
        assignReq.Headers.Add("X-XSRF-TOKEN", csrf3);

        var assignResp = await adminClient.SendAsync(assignReq);

        // Assert: Endpoint returned 500 Internal Server Error due to interceptor exception
        assignResp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // Assert: Entire transaction was rolled back in PostgreSQL
        using (var scope = rollbackFactory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var dbUser = await userManager.FindByIdAsync(targetUserId.ToString());
            dbUser.Should().NotBeNull();

            // Roles must NOT contain FacilityAdmin
            var roles = await userManager.GetRolesAsync(dbUser!);
            roles.Should().NotContain(CampusRoles.FacilityAdmin, "Transaction rollback must restore user_roles to pre-request state");

            // SecurityStamp must be unchanged
            dbUser.SecurityStamp.Should().Be(originalSecurityStamp, "Transaction rollback must restore security_stamp");

            // Zero audit records must be present
            var auditCount = await dbContext.RoleAssignmentAudits.CountAsync(a => a.TargetUserId == targetUserId);
            auditCount.Should().Be(0, "Transaction rollback must not commit any audit record");
        }
    }

    [Fact]
    public async Task RoleAssignment_OldSessionRejection_AfterRoleRemovalAndValidationIntervalElapses()
    {
        // Arrange: Factory with 1-second stamp validation interval
        await using var shortIntervalFactory = new ShortExpirationWebApplicationFactory(
            tokenLifespan: TimeSpan.FromHours(1),
            stampValidationInterval: TimeSpan.FromSeconds(1));

        var adminEmail = $"admin_rejection_{Guid.NewGuid():N}@campus.internal";
        var staffEmail = $"staff_rejection_{Guid.NewGuid():N}@campus.internal";
        var password = "ValidPassword123!";

        // Provision Admin and Staff User
        Guid targetUserId;
        string staffConcurrencyStamp;
        using (var scope = shortIntervalFactory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

            foreach (var r in CampusRoles.All)
            {
                if (!await roleManager.RoleExistsAsync(r))
                {
                    await roleManager.CreateAsync(new ApplicationRole
                    {
                        Id = Guid.NewGuid(),
                        Name = r,
                        NormalizedName = r.ToUpperInvariant(),
                        Description = CampusRoles.GetDescription(r)
                    });
                }
            }

            var staffUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = staffEmail,
                Email = staffEmail,
                EmailConfirmed = true,
                ConcurrencyStamp = Guid.NewGuid().ToString("D"),
                SecurityStamp = Guid.NewGuid().ToString("D")
            };
            await userManager.CreateAsync(staffUser, password);
            await userManager.AddToRoleAsync(staffUser, CampusRoles.FacilityAdmin);
            targetUserId = staffUser.Id;
            staffConcurrencyStamp = staffUser.ConcurrencyStamp;

            var adminUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true
            };
            await userManager.CreateAsync(adminUser, password);
            await userManager.AddToRoleAsync(adminUser, CampusRoles.UniversityAdmin);
            await userManager.SetTwoFactorEnabledAsync(adminUser, true);
        }

        // Step 1: Staff user signs in on Client B
        var clientB = shortIntervalFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var (csrfB, _) = await GetAntiforgeryTokensAsync(clientB);
        var loginReqB = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(staffEmail, password))
        };
        loginReqB.Headers.Add("X-XSRF-TOKEN", csrfB);
        var loginRespB = await clientB.SendAsync(loginReqB);
        loginRespB.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify Client B session is active and has FacilityAdmin
        var meRespB1 = await clientB.GetAsync("/api/auth/me");
        meRespB1.StatusCode.Should().Be(HttpStatusCode.OK);
        var meDataB1 = await meRespB1.Content.ReadFromJsonAsync<CurrentUserResponse>();
        meDataB1.Should().NotBeNull();
        meDataB1!.Roles.Should().Contain(CampusRoles.FacilityAdmin);

        // Step 2: Admin signs in with MFA on Client A
        var clientA = shortIntervalFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var (csrfA1, _) = await GetAntiforgeryTokensAsync(clientA);
        var loginReqA = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(adminEmail, password))
        };
        loginReqA.Headers.Add("X-XSRF-TOKEN", csrfA1);
        var loginRespA = await clientA.SendAsync(loginReqA);
        loginRespA.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = shortIntervalFactory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var dbAdmin = await userManager.FindByEmailAsync(adminEmail);
            await userManager.ResetAuthenticatorKeyAsync(dbAdmin!);
            var key = await userManager.GetAuthenticatorKeyAsync(dbAdmin!);
            var totp = ComputeTotp(key!);

            var (csrfA2, _) = await GetAntiforgeryTokensAsync(clientA);
            var challengeReqA = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/challenge")
            {
                Content = JsonContent.Create(new TwoFactorChallengeRequest(totp))
            };
            challengeReqA.Headers.Add("X-XSRF-TOKEN", csrfA2);
            var challengeRespA = await clientA.SendAsync(challengeReqA);
            challengeRespA.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Step 3: Admin removes staff role from target user
        var (csrfA3, _) = await GetAntiforgeryTokensAsync(clientA);
        var removeRoleReq = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetUserId}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest([], staffConcurrencyStamp))
        };
        removeRoleReq.Headers.Add("X-XSRF-TOKEN", csrfA3);
        var removeResp = await clientA.SendAsync(removeRoleReq);
        removeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 4: Wait for the configured 1-second stamp validation interval to elapse
        await Task.Delay(1200);

        // Step 5: Client B sends a request with their pre-revocation session cookie
        var meRespB2 = await clientB.GetAsync("/api/auth/me");

        // Assert: Client B's session cookie is rejected because target user's SecurityStamp was rotated in PostgreSQL
        meRespB2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RoleAssignment_MissingConcurrencyPrecondition_WhenChangingRoles_Returns400BadRequest()
    {
        // Arrange
        var adminEmail = $"admin_precond_test_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var adminClient = await CreateSessionWithCompletedMfaAsync(adminEmail, password, CampusRoles.UniversityAdmin);

        var targetEmail = $"target_precond_{Guid.NewGuid():N}@campus.internal";
        var targetUser = await EnsureUserWithRoleAsync(targetEmail, password, CampusRoles.Participant, emailConfirmed: true);
        string originalSecurityStamp = targetUser.SecurityStamp!;
        string originalConcurrencyStamp = targetUser.ConcurrencyStamp!;

        // Act 1: Attempt to grant FacilityAdmin without ConcurrencyStamp, If-Match, or ExpectedRoles
        var (csrf1, _) = await GetAntiforgeryTokensAsync(adminClient);
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetUser.Id}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest([CampusRoles.FacilityAdmin]))
        };
        request.Headers.Add("X-XSRF-TOKEN", csrf1);

        var response = await adminClient.SendAsync(request);

        // Assert 1: HTTP 400 Bad Request with Precondition Required Problem Details
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errorContent = await response.Content.ReadAsStringAsync();
        errorContent.Should().Contain("Precondition Required");

        // Verify database roles, security stamp, and concurrency stamp stay unchanged, and no audit record was written
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var dbUser = await userManager.FindByIdAsync(targetUser.Id.ToString());
            var roles = await userManager.GetRolesAsync(dbUser!);
            roles.Should().NotContain(CampusRoles.FacilityAdmin);
            dbUser!.SecurityStamp.Should().Be(originalSecurityStamp, "Security stamp must stay unchanged when request omits concurrency precondition");
            dbUser.ConcurrencyStamp.Should().Be(originalConcurrencyStamp, "Concurrency stamp must stay unchanged when request omits concurrency precondition");

            var auditCount = await dbContext.RoleAssignmentAudits.CountAsync(a => a.TargetUserId == targetUser.Id);
            auditCount.Should().Be(0, "No audit record must be written when request omits concurrency precondition");
        }

        // Act 2: A retry requesting the already-stored role set (Participant) without precondition succeeds as no-op
        var (csrf2, _) = await GetAntiforgeryTokensAsync(adminClient);
        var retryNoOpReq = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetUser.Id}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest([CampusRoles.Participant]))
        };
        retryNoOpReq.Headers.Add("X-XSRF-TOKEN", csrf2);

        var retryNoOpResp = await adminClient.SendAsync(retryNoOpReq);

        // Assert 2: HTTP 200 OK No-Op
        retryNoOpResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var noOpData = await retryNoOpResp.Content.ReadFromJsonAsync<AssignUserRolesResponse>();
        noOpData.Should().NotBeNull();
        noOpData!.Roles.Should().ContainSingle().Which.Should().Be(CampusRoles.Participant);
        noOpData.Message.Should().Contain("up to date");

        // Verify security stamp and audit count remain unchanged after no-op replay
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var dbUser = await dbContext.Users.FindAsync(targetUser.Id);
            dbUser!.SecurityStamp.Should().Be(originalSecurityStamp, "Security stamp must not update on an already-applied no-op retry");
            dbUser.ConcurrencyStamp.Should().Be(originalConcurrencyStamp, "Concurrency stamp must not update on an already-applied no-op retry");

            var auditCount = await dbContext.RoleAssignmentAudits.CountAsync(a => a.TargetUserId == targetUser.Id);
            auditCount.Should().Be(0, "No audit record must be written on an already-applied no-op retry");
        }
    }

    [Fact]
    public async Task RoleAssignment_ConflictingEdits_StaleExpectedRoles_Returns409Conflict()
    {
        // Arrange
        var adminEmail = $"admin_exp_roles_{Guid.NewGuid():N}@campus.internal";
        var password = "Password123!";
        var adminClient = await CreateSessionWithCompletedMfaAsync(adminEmail, password, CampusRoles.UniversityAdmin);

        var targetEmail = $"target_exp_roles_{Guid.NewGuid():N}@campus.internal";
        var targetUser = await EnsureUserWithRoleAsync(targetEmail, password, CampusRoles.Participant, emailConfirmed: true);

        // Admin B modifies target user to EventCoordinator
        string adminBStamp;
        string adminBSecurityStamp;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var dbUser = await dbContext.Users.FindAsync(targetUser.Id);
            var ecRole = await dbContext.Roles.FirstAsync(r => r.Name == CampusRoles.EventCoordinator);

            dbContext.UserRoles.Add(new IdentityUserRole<Guid> { UserId = targetUser.Id, RoleId = ecRole.Id });
            dbUser!.ConcurrencyStamp = Guid.NewGuid().ToString("D");
            dbUser.SecurityStamp = Guid.NewGuid().ToString("D");
            await dbContext.SaveChangesAsync();
            adminBStamp = dbUser.ConcurrencyStamp;
            adminBSecurityStamp = dbUser.SecurityStamp;
        }

        // Admin A now submits claiming they expected no staff roles ([Participant]), attempting to set FacilityAdmin
        var (csrf, _) = await GetAntiforgeryTokensAsync(adminClient);
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{targetUser.Id}/roles")
        {
            Content = JsonContent.Create(new AssignUserRolesRequest(
                Roles: [CampusRoles.FacilityAdmin],
                ExpectedRoles: [CampusRoles.Participant]))
        };
        request.Headers.Add("X-XSRF-TOKEN", csrf);

        // Act
        var response = await adminClient.SendAsync(request);

        // Assert: HTTP 409 Conflict
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Verify newer change from Admin B is preserved
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var dbUser = await userManager.FindByIdAsync(targetUser.Id.ToString());
            var roles = await userManager.GetRolesAsync(dbUser!);
            roles.Should().Contain(CampusRoles.EventCoordinator, "Newer change by Admin B must be preserved");
            roles.Should().NotContain(CampusRoles.FacilityAdmin, "Stale request must not overwrite roles");
            dbUser!.ConcurrencyStamp.Should().Be(adminBStamp, "Concurrency stamp from newer change must be preserved");
            dbUser.SecurityStamp.Should().Be(adminBSecurityStamp, "Security stamp from newer change must be preserved");

            var auditCount = await dbContext.RoleAssignmentAudits.CountAsync(a => a.TargetUserId == targetUser.Id);
            auditCount.Should().Be(0, "Stale conflict request must not create an audit record");
        }
    }
}
