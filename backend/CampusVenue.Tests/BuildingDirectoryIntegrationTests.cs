using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using CampusVenue.Api.Common;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Modules.Facilities.Domain;
using CampusVenue.Api.Modules.Facilities.Features.Availability;
using CampusVenue.Api.Modules.Facilities.Features.Buildings;
using CampusVenue.Api.Modules.Facilities.Features.Calendar;
using CampusVenue.Api.Modules.Facilities.Features.Venues;
using CampusVenue.Api.Modules.Identity.Domain;
using CampusVenue.Api.Modules.Identity.Features.Auth;
using CampusVenue.Api.Modules.Identity.Features.TwoFactor;
using CampusVenue.Api.Modules.Reservations.Domain;
using CampusVenue.Api.Modules.Reservations.Features.BookingRequests;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CampusVenue.Tests;

public class BuildingDirectoryIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BuildingDirectoryIntegrationTests(TestWebApplicationFactory factory)
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

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string password, string role)
    {
        await EnsureUserWithRoleAsync(email, password, role);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var (csrf, _) = await GetAntiforgeryTokensAsync(client);
        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginRequest.Headers.Add("X-XSRF-TOKEN", csrf);

        var loginResponse = await client.SendAsync(loginRequest);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        return client;
    }

    private async Task<HttpClient> CreateAdminWithMfaClientAsync(string email, string password)
    {
        var user = await EnsureUserWithRoleAsync(email, password, CampusRoles.UniversityAdmin);

        var setupClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf1, _) = await GetAntiforgeryTokensAsync(setupClient);

        var loginReq1 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq1.Headers.Add("X-XSRF-TOKEN", csrf1);
        var loginResp1 = await setupClient.SendAsync(loginReq1);
        loginResp1.StatusCode.Should().Be(HttpStatusCode.OK);

        var setupResp = await setupClient.GetAsync("/api/auth/2fa/setup");
        setupResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var totpCode = await GenerateTotpCodeAsync(user.Id);
        var (csrf2, _) = await GetAntiforgeryTokensAsync(setupClient);
        var enableReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/enable")
        {
            Content = JsonContent.Create(new EnableTwoFactorRequest(password, totpCode))
        };
        enableReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        var enableResp = await setupClient.SendAsync(enableReq);
        enableResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var mfaClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var (csrf3, _) = await GetAntiforgeryTokensAsync(mfaClient);
        var loginReq2 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        loginReq2.Headers.Add("X-XSRF-TOKEN", csrf3);
        var loginResp2 = await mfaClient.SendAsync(loginReq2);
        loginResp2.StatusCode.Should().Be(HttpStatusCode.OK);

        var totpChallengeCode = await GenerateTotpCodeAsync(user.Id);
        var (csrf4, _) = await GetAntiforgeryTokensAsync(mfaClient);
        var challengeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/challenge")
        {
            Content = JsonContent.Create(new TwoFactorChallengeRequest(totpChallengeCode))
        };
        challengeReq.Headers.Add("X-XSRF-TOKEN", csrf4);
        var challengeResp = await mfaClient.SendAsync(challengeReq);
        challengeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        return mfaClient;
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

        using var hmac = new HMACSHA1(keyBytes);
        var hash = hmac.ComputeHash(timeBytes);

        int offset = hash[^1] & 0x0F;
        int binaryCode = ((hash[offset] & 0x7F) << 24)
                       | ((hash[offset + 1] & 0xFF) << 16)
                       | ((hash[offset + 2] & 0xFF) << 8)
                       | (hash[offset + 3] & 0xFF);

        int otp = binaryCode % 1_000_000;
        return otp.ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var cleanInput = input.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>();

        int buffer = 0;
        int bitsLeft = 0;

        foreach (char c in cleanInput)
        {
            int val = alphabet.IndexOf(c);
            if (val < 0) continue;

            buffer = (buffer << 5) | val;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                output.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }

        return output.ToArray();
    }

    [Fact]
    public async Task CreateBuilding_AuthorizedAdminWithCompletedMfa_Returns201Created_AndNormalizesCode()
    {
        // Arrange
        var adminEmail = $"admin-building-{Guid.NewGuid():N}@campus.internal";
        var adminClient = await CreateAdminWithMfaClientAsync(adminEmail, "P@ssword123!");
        var (csrf, _) = await GetAntiforgeryTokensAsync(adminClient);

        var uniqueCode = $"BLD-{Guid.NewGuid():N}"[..8].ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/buildings")
        {
            Content = JsonContent.Create(new CreateBuildingRequest("Sumanadasa Building", $"  {uniqueCode}  "))
        };
        request.Headers.Add("X-XSRF-TOKEN", csrf);

        // Act
        var response = await adminClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var building = await response.Content.ReadFromJsonAsync<BuildingDto>();
        building.Should().NotBeNull();
        building!.Name.Should().Be("Sumanadasa Building");
        building.Code.Should().Be(uniqueCode.Trim().ToUpperInvariant());
        building.VenueCount.Should().Be(0);
        building.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateBuilding_DuplicateCode_Returns409Conflict()
    {
        // Arrange
        var adminEmail = $"admin-bld-dup-{Guid.NewGuid():N}@campus.internal";
        var adminClient = await CreateAdminWithMfaClientAsync(adminEmail, "P@ssword123!");
        var (csrf1, _) = await GetAntiforgeryTokensAsync(adminClient);

        var code = $"DUP-{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/buildings")
        {
            Content = JsonContent.Create(new CreateBuildingRequest("Building One", code))
        };
        req1.Headers.Add("X-XSRF-TOKEN", csrf1);
        var resp1 = await adminClient.SendAsync(req1);
        resp1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act - Attempt to create another building with same code in lowercase
        var (csrf2, _) = await GetAntiforgeryTokensAsync(adminClient);
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/buildings")
        {
            Content = JsonContent.Create(new CreateBuildingRequest("Building Duplicate", code.ToLowerInvariant()))
        };
        req2.Headers.Add("X-XSRF-TOKEN", csrf2);
        var resp2 = await adminClient.SendAsync(req2);

        // Assert
        resp2.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateBuilding_EmptyNameOrCode_Returns400BadRequest()
    {
        // Arrange
        var adminEmail = $"admin-bld-bad-{Guid.NewGuid():N}@campus.internal";
        var adminClient = await CreateAdminWithMfaClientAsync(adminEmail, "P@ssword123!");

        var (csrf1, _) = await GetAntiforgeryTokensAsync(adminClient);
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/buildings")
        {
            Content = JsonContent.Create(new CreateBuildingRequest("", "VALID-CODE"))
        };
        req1.Headers.Add("X-XSRF-TOKEN", csrf1);
        var resp1 = await adminClient.SendAsync(req1);
        resp1.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (csrf2, _) = await GetAntiforgeryTokensAsync(adminClient);
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/buildings")
        {
            Content = JsonContent.Create(new CreateBuildingRequest("Valid Name", "   "))
        };
        req2.Headers.Add("X-XSRF-TOKEN", csrf2);
        var resp2 = await adminClient.SendAsync(req2);
        resp2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateBuilding_DuplicateCodeOnOtherBuilding_Returns409Conflict()
    {
        // Arrange
        var adminEmail = $"admin-bld-upd-{Guid.NewGuid():N}@campus.internal";
        var adminClient = await CreateAdminWithMfaClientAsync(adminEmail, "P@ssword123!");

        var codeA = $"BLD-A-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var codeB = $"BLD-B-{Guid.NewGuid():N}"[..10].ToUpperInvariant();

        var (csrf1, _) = await GetAntiforgeryTokensAsync(adminClient);
        var reqA = new HttpRequestMessage(HttpMethod.Post, "/api/buildings")
        {
            Content = JsonContent.Create(new CreateBuildingRequest("Building A", codeA))
        };
        reqA.Headers.Add("X-XSRF-TOKEN", csrf1);
        var respA = await adminClient.SendAsync(reqA);
        respA.StatusCode.Should().Be(HttpStatusCode.Created);

        var (csrf2, _) = await GetAntiforgeryTokensAsync(adminClient);
        var reqB = new HttpRequestMessage(HttpMethod.Post, "/api/buildings")
        {
            Content = JsonContent.Create(new CreateBuildingRequest("Building B", codeB))
        };
        reqB.Headers.Add("X-XSRF-TOKEN", csrf2);
        var respB = await adminClient.SendAsync(reqB);
        respB.StatusCode.Should().Be(HttpStatusCode.Created);
        var bldB = await respB.Content.ReadFromJsonAsync<BuildingDto>();

        // Act - Attempt to rename Building B to codeA
        var (csrf3, _) = await GetAntiforgeryTokensAsync(adminClient);
        var reqUpd = new HttpRequestMessage(HttpMethod.Put, $"/api/buildings/{bldB!.Id}")
        {
            Content = JsonContent.Create(new UpdateBuildingRequest("Building B Renamed", codeA.ToLowerInvariant()))
        };
        reqUpd.Headers.Add("X-XSRF-TOKEN", csrf3);
        var respUpd = await adminClient.SendAsync(reqUpd);

        // Assert
        respUpd.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateBuilding_SameCodeOnSameBuilding_Returns200OK()
    {
        // Arrange
        var adminEmail = $"admin-bld-same-{Guid.NewGuid():N}@campus.internal";
        var adminClient = await CreateAdminWithMfaClientAsync(adminEmail, "P@ssword123!");

        var code = $"BLD-S-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var (csrf1, _) = await GetAntiforgeryTokensAsync(adminClient);
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/buildings")
        {
            Content = JsonContent.Create(new CreateBuildingRequest("Original Name", code))
        };
        req1.Headers.Add("X-XSRF-TOKEN", csrf1);
        var resp1 = await adminClient.SendAsync(req1);
        var bld = await resp1.Content.ReadFromJsonAsync<BuildingDto>();

        // Act - Update name while keeping identical code
        var (csrf2, _) = await GetAntiforgeryTokensAsync(adminClient);
        var reqUpd = new HttpRequestMessage(HttpMethod.Put, $"/api/buildings/{bld!.Id}")
        {
            Content = JsonContent.Create(new UpdateBuildingRequest("Updated Name", code))
        };
        reqUpd.Headers.Add("X-XSRF-TOKEN", csrf2);
        var respUpd = await adminClient.SendAsync(reqUpd);

        // Assert
        respUpd.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await respUpd.Content.ReadFromJsonAsync<BuildingDto>();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated Name");
        updated.Code.Should().Be(code);
    }

    [Fact]
    public async Task BuildingEndpoints_UnauthorizedWrites_Returns401Or403()
    {
        // 1. Anonymous writes -> 401
        var anonClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var anonPost = await anonClient.PostAsJsonAsync("/api/buildings", new CreateBuildingRequest("Anon", "ANON"));
        anonPost.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. EventCoordinator writes -> 403
        var coordEmail = $"coord-write-{Guid.NewGuid():N}@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "P@ssword123!", CampusRoles.EventCoordinator);
        var (csrf1, _) = await GetAntiforgeryTokensAsync(coordClient);
        var coordReq = new HttpRequestMessage(HttpMethod.Post, "/api/buildings")
        {
            Content = JsonContent.Create(new CreateBuildingRequest("Coord", "COORD"))
        };
        coordReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        var coordResp = await coordClient.SendAsync(coordReq);
        coordResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. FacilityAdmin writes -> 403
        var facEmail = $"fac-write-{Guid.NewGuid():N}@campus.internal";
        var facClient = await CreateAuthenticatedClientAsync(facEmail, "P@ssword123!", CampusRoles.FacilityAdmin);
        var (csrf2, _) = await GetAntiforgeryTokensAsync(facClient);
        var facReq = new HttpRequestMessage(HttpMethod.Post, "/api/buildings")
        {
            Content = JsonContent.Create(new CreateBuildingRequest("Fac", "FAC"))
        };
        facReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        var facResp = await facClient.SendAsync(facReq);
        facResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 4. UniversityAdmin WITHOUT MFA (password-only session) -> 403
        var adminNoMfaEmail = $"admin-nomfa-{Guid.NewGuid():N}@campus.internal";
        var adminNoMfaClient = await CreateAuthenticatedClientAsync(adminNoMfaEmail, "P@ssword123!", CampusRoles.UniversityAdmin);
        var (csrf3, _) = await GetAntiforgeryTokensAsync(adminNoMfaClient);
        var adminNoMfaReq = new HttpRequestMessage(HttpMethod.Post, "/api/buildings")
        {
            Content = JsonContent.Create(new CreateBuildingRequest("NoMfa", "NOMFA"))
        };
        adminNoMfaReq.Headers.Add("X-XSRF-TOKEN", csrf3);
        var adminNoMfaResp = await adminNoMfaClient.SendAsync(adminNoMfaReq);
        adminNoMfaResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AssignVenueBuilding_AuthorizedAdmin_AssignsVenue_Returns200OK()
    {
        // Arrange
        var adminEmail = $"admin-assign-{Guid.NewGuid():N}@campus.internal";
        var adminClient = await CreateAdminWithMfaClientAsync(adminEmail, "P@ssword123!");

        // Create building
        var bldCode = $"BLD-ASN-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var (csrf1, _) = await GetAntiforgeryTokensAsync(adminClient);
        var bldReq = new HttpRequestMessage(HttpMethod.Post, "/api/buildings")
        {
            Content = JsonContent.Create(new CreateBuildingRequest("Civil Complex", bldCode))
        };
        bldReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        var bldResp = await adminClient.SendAsync(bldReq);
        bldResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bld = await bldResp.Content.ReadFromJsonAsync<BuildingDto>();

        // Create venue
        var venueCode = $"V-ASN-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var (csrf2, _) = await GetAntiforgeryTokensAsync(adminClient);
        var venueReq = new HttpRequestMessage(HttpMethod.Post, "/api/venues")
        {
            Content = JsonContent.Create(new CreateVenueRequest(
                "Civil Hall 1",
                venueCode,
                "Departmental",
                "Civil Engineering",
                120,
                "08:00",
                "18:00",
                24,
                30,
                15,
                6,
                false
            ))
        };
        venueReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        var venueResp = await adminClient.SendAsync(venueReq);
        venueResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var venue = await venueResp.Content.ReadFromJsonAsync<VenueDto>();
        venue!.BuildingId.Should().BeNull();

        // Act - Assign venue to building
        var (csrf3, _) = await GetAntiforgeryTokensAsync(adminClient);
        var assignReq = new HttpRequestMessage(HttpMethod.Put, $"/api/venues/{venue.Id}/building")
        {
            Content = JsonContent.Create(new AssignVenueBuildingRequest(bld!.Id))
        };
        assignReq.Headers.Add("X-XSRF-TOKEN", csrf3);
        var assignResp = await adminClient.SendAsync(assignReq);

        // Assert
        assignResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var assignedVenue = await assignResp.Content.ReadFromJsonAsync<VenueDto>();
        assignedVenue.Should().NotBeNull();
        assignedVenue!.BuildingId.Should().Be(bld.Id);
        assignedVenue.BuildingName.Should().Be("Civil Complex");
        assignedVenue.BuildingCode.Should().Be(bldCode);
    }

    [Fact]
    public async Task AssignVenueBuilding_ReassignAndUnassign_Returns200OK()
    {
        // Arrange
        var adminEmail = $"admin-reassign-{Guid.NewGuid():N}@campus.internal";
        var adminClient = await CreateAdminWithMfaClientAsync(adminEmail, "P@ssword123!");

        // Create two buildings
        var code1 = $"BLD-1-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var code2 = $"BLD-2-{Guid.NewGuid():N}"[..10].ToUpperInvariant();

        var (csrf1, _) = await GetAntiforgeryTokensAsync(adminClient);
        var bld1Req = new HttpRequestMessage(HttpMethod.Post, "/api/buildings") { Content = JsonContent.Create(new CreateBuildingRequest("Building One", code1)) };
        bld1Req.Headers.Add("X-XSRF-TOKEN", csrf1);
        var bld1 = await (await adminClient.SendAsync(bld1Req)).Content.ReadFromJsonAsync<BuildingDto>();

        var (csrf2, _) = await GetAntiforgeryTokensAsync(adminClient);
        var bld2Req = new HttpRequestMessage(HttpMethod.Post, "/api/buildings") { Content = JsonContent.Create(new CreateBuildingRequest("Building Two", code2)) };
        bld2Req.Headers.Add("X-XSRF-TOKEN", csrf2);
        var bld2 = await (await adminClient.SendAsync(bld2Req)).Content.ReadFromJsonAsync<BuildingDto>();

        // Create venue
        var vCode = $"V-RE-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var (csrf3, _) = await GetAntiforgeryTokensAsync(adminClient);
        var vReq = new HttpRequestMessage(HttpMethod.Post, "/api/venues")
        {
            Content = JsonContent.Create(new CreateVenueRequest("Reassign Hall", vCode, "Central", "Admin", 100, "08:00", "18:00", 24, 30, 15, 6, false))
        };
        vReq.Headers.Add("X-XSRF-TOKEN", csrf3);
        var v = await (await adminClient.SendAsync(vReq)).Content.ReadFromJsonAsync<VenueDto>();

        // 1. Assign to Building 1
        var (csrf4, _) = await GetAntiforgeryTokensAsync(adminClient);
        var a1 = new HttpRequestMessage(HttpMethod.Put, $"/api/venues/{v!.Id}/building") { Content = JsonContent.Create(new AssignVenueBuildingRequest(bld1!.Id)) };
        a1.Headers.Add("X-XSRF-TOKEN", csrf4);
        var r1 = await (await adminClient.SendAsync(a1)).Content.ReadFromJsonAsync<VenueDto>();
        r1!.BuildingId.Should().Be(bld1.Id);

        // 2. Reassign to Building 2
        var (csrf5, _) = await GetAntiforgeryTokensAsync(adminClient);
        var a2 = new HttpRequestMessage(HttpMethod.Put, $"/api/venues/{v.Id}/building") { Content = JsonContent.Create(new AssignVenueBuildingRequest(bld2!.Id)) };
        a2.Headers.Add("X-XSRF-TOKEN", csrf5);
        var r2 = await (await adminClient.SendAsync(a2)).Content.ReadFromJsonAsync<VenueDto>();
        r2!.BuildingId.Should().Be(bld2.Id);
        r2.BuildingName.Should().Be("Building Two");

        // 3. Unassign building (null)
        var (csrf6, _) = await GetAntiforgeryTokensAsync(adminClient);
        var a3 = new HttpRequestMessage(HttpMethod.Put, $"/api/venues/{v.Id}/building") { Content = JsonContent.Create(new AssignVenueBuildingRequest(null)) };
        a3.Headers.Add("X-XSRF-TOKEN", csrf6);
        var r3 = await (await adminClient.SendAsync(a3)).Content.ReadFromJsonAsync<VenueDto>();
        r3!.BuildingId.Should().BeNull();
        r3.BuildingName.Should().BeNull();
        r3.BuildingCode.Should().BeNull();
    }

    [Fact]
    public async Task AssignVenueBuilding_NonexistentBuilding_Returns400BadRequest()
    {
        // Arrange
        var adminEmail = $"admin-nobld-{Guid.NewGuid():N}@campus.internal";
        var adminClient = await CreateAdminWithMfaClientAsync(adminEmail, "P@ssword123!");

        var vCode = $"V-NB-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var (csrf1, _) = await GetAntiforgeryTokensAsync(adminClient);
        var vReq = new HttpRequestMessage(HttpMethod.Post, "/api/venues")
        {
            Content = JsonContent.Create(new CreateVenueRequest("NB Hall", vCode, "Central", "Admin", 100, "08:00", "18:00", 24, 30, 15, 6, false))
        };
        vReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        var v = await (await adminClient.SendAsync(vReq)).Content.ReadFromJsonAsync<VenueDto>();

        // Act - Assign nonexistent building ID
        var (csrf2, _) = await GetAntiforgeryTokensAsync(adminClient);
        var aReq = new HttpRequestMessage(HttpMethod.Put, $"/api/venues/{v!.Id}/building")
        {
            Content = JsonContent.Create(new AssignVenueBuildingRequest(Guid.NewGuid()))
        };
        aReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        var response = await adminClient.SendAsync(aReq);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListVenues_LegacyUnassignedVenue_AppearsInCatalog_WithNullBuilding()
    {
        // Arrange
        var coordEmail = $"coord-legacy-{Guid.NewGuid():N}@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "P@ssword123!", CampusRoles.EventCoordinator);

        // Act - Query catalog
        var response = await coordClient.GetAsync("/api/venues");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var venues = await response.Content.ReadFromJsonAsync<List<VenueDto>>();
        venues.Should().NotBeNull();
        venues!.Should().NotBeEmpty();

        // Seeded or unassigned venues remain usable without a building
        var unassigned = venues.FirstOrDefault(v => v.BuildingId == null);
        unassigned.Should().NotBeNull();
        unassigned!.BuildingName.Should().BeNull();
        unassigned.BuildingCode.Should().BeNull();
    }

    [Fact]
    public async Task AssignBuilding_PreservesExistingReservationHistory()
    {
        // Arrange
        var adminEmail = $"admin-hist-{Guid.NewGuid():N}@campus.internal";
        var adminClient = await CreateAdminWithMfaClientAsync(adminEmail, "P@ssword123!");

        var managerEmail = $"mgr-hist-{Guid.NewGuid():N}@campus.internal";
        var managerUser = await EnsureUserWithRoleAsync(managerEmail, "P@ssword123!", CampusRoles.FacilityAdmin);

        var coordEmail = $"coord-hist-{Guid.NewGuid():N}@campus.internal";
        var coordUser = await EnsureUserWithRoleAsync(coordEmail, "P@ssword123!", CampusRoles.EventCoordinator);
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "P@ssword123!", CampusRoles.EventCoordinator);

        // 1. Create a venue without a building
        var venueCode = $"V-HIST-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var (csrf1, _) = await GetAntiforgeryTokensAsync(adminClient);
        var createVenueReq = new HttpRequestMessage(HttpMethod.Post, "/api/venues")
        {
            Content = JsonContent.Create(new CreateVenueRequest(
                "History Preservation Hall",
                venueCode,
                "Departmental",
                "History Dept",
                150,
                "08:00",
                "20:00",
                0,
                30,
                30,
                8,
                false
            ))
        };
        createVenueReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        var venueResp = await adminClient.SendAsync(createVenueReq);
        venueResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var venue = await venueResp.Content.ReadFromJsonAsync<VenueDto>();

        // 2. Assign manager to venue
        var (csrf2, _) = await GetAntiforgeryTokensAsync(adminClient);
        var assignMgrReq = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue!.Id}/managers")
        {
            Content = JsonContent.Create(new AssignVenueManagerRequest(managerUser.Id))
        };
        assignMgrReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        var assignMgrResp = await adminClient.SendAsync(assignMgrReq);
        assignMgrResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Add calendar coverage
        var (csrf3, _) = await GetAntiforgeryTokensAsync(adminClient);
        var coverageReq = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/calendar-coverages")
        {
            Content = JsonContent.Create(new CreateCoverageRequest(
                "CITeS RBS",
                "Term Coverage",
                DateTime.UtcNow.Date.AddDays(-1),
                DateTime.UtcNow.Date.AddDays(30),
                "History preservation test"
            ))
        };
        coverageReq.Headers.Add("X-XSRF-TOKEN", csrf3);
        var covResp = await adminClient.SendAsync(coverageReq);
        covResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // 4. Coordinator submits booking request
        var eventStartUtc = DateTime.UtcNow.Date.AddDays(5).AddHours(9);
        var eventEndUtc = eventStartUtc.AddHours(2);

        var (csrf4, _) = await GetAntiforgeryTokensAsync(coordClient);
        var bookReq = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(new CreateBookingRequest(
                "Symposium on University Architecture",
                "Historical preservation test event",
                80,
                eventStartUtc,
                eventEndUtc,
                30,
                30,
                $"req-hist-{Guid.NewGuid():N}"
            ))
        };
        bookReq.Headers.Add("X-XSRF-TOKEN", csrf4);
        var bookResp = await coordClient.SendAsync(bookReq);
        bookResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookingDto = await bookResp.Content.ReadFromJsonAsync<BookingRequestDto>();

        // 5. Manager approves request
        var managerClient = await CreateAuthenticatedClientAsync(managerEmail, "P@ssword123!", CampusRoles.FacilityAdmin);
        var (csrf5, _) = await GetAntiforgeryTokensAsync(managerClient);
        var reviewReq = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{bookingDto!.Id}/review")
        {
            Content = JsonContent.Create(new ReviewBookingRequest("Approve", "Approved for preservation test.", null))
        };
        reviewReq.Headers.Add("X-XSRF-TOKEN", csrf5);
        var reviewResp = await managerClient.SendAsync(reviewReq);
        reviewResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Coordinator confirms booking
        var (csrf6, _) = await GetAntiforgeryTokensAsync(coordClient);
        var confirmReq = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{bookingDto.Id}/confirm");
        confirmReq.Headers.Add("X-XSRF-TOKEN", csrf6);
        var confirmResp = await coordClient.SendAsync(confirmReq);
        confirmResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmedDto = await confirmResp.Content.ReadFromJsonAsync<BookingRequestDto>();
        confirmedDto!.ReservationReference.Should().NotBeNullOrWhiteSpace();
        var origResRef = confirmedDto.ReservationReference!;
        var origResId = confirmedDto.ConfirmedReservationId!.Value;

        // 7. Admin creates building and assigns venue to building
        var bldCode = $"BLD-H-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var (csrf7, _) = await GetAntiforgeryTokensAsync(adminClient);
        var bldReq = new HttpRequestMessage(HttpMethod.Post, "/api/buildings")
        {
            Content = JsonContent.Create(new CreateBuildingRequest("Heritage Complex", bldCode))
        };
        bldReq.Headers.Add("X-XSRF-TOKEN", csrf7);
        var bld = await (await adminClient.SendAsync(bldReq)).Content.ReadFromJsonAsync<BuildingDto>();

        var (csrf8, _) = await GetAntiforgeryTokensAsync(adminClient);
        var assignReq = new HttpRequestMessage(HttpMethod.Put, $"/api/venues/{venue.Id}/building")
        {
            Content = JsonContent.Create(new AssignVenueBuildingRequest(bld!.Id))
        };
        assignReq.Headers.Add("X-XSRF-TOKEN", csrf8);
        var assignResp = await adminClient.SendAsync(assignReq);
        assignResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 8. VERIFY: Direct DB inspection confirms reservation history is completely preserved
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();

        var resInDb = await db.ConfirmedReservations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == origResId);

        resInDb.Should().NotBeNull();
        resInDb!.ReservationReference.Should().Be(origResRef);
        resInDb.VenueId.Should().Be(venue.Id);
        resInDb.EventTitle.Should().Be("Symposium on University Architecture");
        resInDb.EventStartUtc.Should().Be(eventStartUtc);
        resInDb.EventEndUtc.Should().Be(eventEndUtc);

        var venueInDb = await db.Venues.AsNoTracking().FirstOrDefaultAsync(v => v.Id == venue.Id);
        venueInDb!.BuildingId.Should().Be(bld.Id);
    }

    [Fact]
    public async Task ListBuildings_IncludesActiveVenueCount()
    {
        // Arrange
        var adminEmail = $"admin-bld-cnt-{Guid.NewGuid():N}@campus.internal";
        var adminClient = await CreateAdminWithMfaClientAsync(adminEmail, "P@ssword123!");

        var bldCode = $"BLD-C-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var (csrf1, _) = await GetAntiforgeryTokensAsync(adminClient);
        var bldReq = new HttpRequestMessage(HttpMethod.Post, "/api/buildings")
        {
            Content = JsonContent.Create(new CreateBuildingRequest("Multi-Venue Building", bldCode))
        };
        bldReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        var bld = await (await adminClient.SendAsync(bldReq)).Content.ReadFromJsonAsync<BuildingDto>();

        // Create 2 venues assigned to this building
        for (int i = 1; i <= 2; i++)
        {
            var vCode = $"V-C{i}-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
            var (csrf, _) = await GetAntiforgeryTokensAsync(adminClient);
            var vReq = new HttpRequestMessage(HttpMethod.Post, "/api/venues")
            {
                Content = JsonContent.Create(new CreateVenueRequest(
                    $"Room {i}",
                    vCode,
                    "Departmental",
                    "Dept",
                    40,
                    "08:00",
                    "18:00",
                    24,
                    15,
                    15,
                    4,
                    false,
                    BuildingId: bld!.Id
                ))
            };
            vReq.Headers.Add("X-XSRF-TOKEN", csrf);
            var resp = await adminClient.SendAsync(vReq);
            resp.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Act - List buildings
        var listResp = await adminClient.GetAsync("/api/buildings");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var buildings = await listResp.Content.ReadFromJsonAsync<List<BuildingDto>>();

        // Assert
        var targetBuilding = buildings!.FirstOrDefault(b => b.Id == bld!.Id);
        targetBuilding.Should().NotBeNull();
        targetBuilding!.VenueCount.Should().Be(2);
    }
}
