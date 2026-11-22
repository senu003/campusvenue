using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Modules.Facilities.Domain;
using CampusVenue.Api.Modules.Facilities.Features.Availability;
using CampusVenue.Api.Modules.Facilities.Features.Calendar;
using CampusVenue.Api.Modules.Facilities.Features.Venues;
using CampusVenue.Api.Modules.Identity.Domain;
using CampusVenue.Api.Modules.Identity.Features.Auth;
using CampusVenue.Api.Modules.Identity.Features.TwoFactor;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CampusVenue.Tests;

public class VenueAvailabilityIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public VenueAvailabilityIntegrationTests(TestWebApplicationFactory factory)
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

    private async Task<Venue> CreateTestVenueAsync(
        string code,
        string name,
        string type = "Departmental",
        int minNoticeHours = 24,
        int defaultSetupMinutes = 30,
        int defaultCleanupMinutes = 30)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();

        var existing = await db.Venues.FirstOrDefaultAsync(v => v.Code == code);
        if (existing != null)
        {
            return existing;
        }

        var venue = new Venue
        {
            Id = Guid.NewGuid(),
            Name = name,
            Code = code,
            Type = type,
            ManagingUnit = "Test Unit",
            Capacity = 100,
            OperatingHoursStart = new TimeOnly(8, 0),
            OperatingHoursEnd = new TimeOnly(20, 0),
            MinNoticeHours = minNoticeHours,
            DefaultSetupMinutes = defaultSetupMinutes,
            DefaultCleanupMinutes = defaultCleanupMinutes,
            MaxBookingHours = 8,
            RequiresEventApproval = false,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        db.Venues.Add(venue);
        await db.SaveChangesAsync();
        return venue;
    }

    private async Task AddCoverageAsync(Guid venueId, DateTime fromUtc, DateTime toUtc, string sourceName = "Test Source")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
        var admin = await db.Users.FirstAsync();

        db.VenueCalendarCoverages.Add(new VenueCalendarCoverage
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            SourceName = sourceName,
            SourceReference = "Ref 1.0",
            CoversFromUtc = fromUtc,
            CoversToUtc = toUtc,
            VerifiedByUserId = admin.Id,
            VerifiedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private async Task AddBlockAsync(Guid venueId, string title, string blockType, DateTime startUtc, DateTime endUtc)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
        var admin = await db.Users.FirstAsync();

        db.VenueCalendarBlocks.Add(new VenueCalendarBlock
        {
            Id = Guid.NewGuid(),
            VenueId = venueId,
            Title = title,
            BlockType = blockType,
            StartUtc = startUtc,
            EndUtc = endUtc,
            CreatedByUserId = admin.Id,
            CreatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    // =========================================================================
    // 1. Test a verified free slot -> Returns Available
    // =========================================================================
    [Fact]
    public async Task CheckAvailability_VerifiedFreeSlot_ReturnsAvailable()
    {
        var coordEmail = "coord_free@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "CoordP@ss2026!", CampusRoles.EventCoordinator);

        var venue = await CreateTestVenueAsync("FREE-01", "Free Slot Test Venue");
        var baseDate = DateTime.UtcNow.Date.AddDays(5);
        await AddCoverageAsync(venue.Id, baseDate.AddDays(-5), baseDate.AddDays(10), "Verified Timetable 2026");

        // Event: 09:00 - 11:00 UTC (14:30 - 16:30 SLT, well within 08:00 - 20:00 operating hours)
        var eventStart = baseDate.AddHours(9);
        var eventEnd = baseDate.AddHours(11);

        var request = new CheckAvailabilityRequest(eventStart, eventEnd, SetupMinutes: 15, CleanupMinutes: 15);
        var response = await coordClient.PostAsJsonAsync($"/api/venues/{venue.Id}/check-availability", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CheckAvailabilityResponse>();

        result.Should().NotBeNull();
        result!.Status.Should().Be(AvailabilityStatus.Available);
        result.IsAvailable.Should().BeTrue();
        result.Reason.Should().Contain("available");
        result.CoverageDetails.Should().NotBeNull();
        result.CoverageDetails!.SourceName.Should().Be("Verified Timetable 2026");
        result.ConflictDetails.Should().BeNull();
    }

    // =========================================================================
    // 2. Test a calendar block -> Returns Unavailable
    // =========================================================================
    [Fact]
    public async Task CheckAvailability_CalendarBlock_ReturnsUnavailable()
    {
        var coordEmail = "coord_block@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "CoordP@ss2026!", CampusRoles.EventCoordinator);

        var venue = await CreateTestVenueAsync("BLOCK-01", "Blocked Test Venue");
        var baseDate = DateTime.UtcNow.Date.AddDays(6);
        await AddCoverageAsync(venue.Id, baseDate.AddDays(-5), baseDate.AddDays(10));

        // Add calendar block: 09:00 - 12:00 UTC
        await AddBlockAsync(venue.Id, "Mid-Semester Exam CS2012", "Exam", baseDate.AddHours(9), baseDate.AddHours(12));

        // Request overlapping slot: 10:00 - 11:30 UTC
        var request = new CheckAvailabilityRequest(baseDate.AddHours(10), baseDate.AddHours(11).AddMinutes(30));
        var response = await coordClient.PostAsJsonAsync($"/api/venues/{venue.Id}/check-availability", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CheckAvailabilityResponse>();

        result.Should().NotBeNull();
        result!.Status.Should().Be(AvailabilityStatus.Unavailable);
        result.IsAvailable.Should().BeFalse();
        result.Reason.Should().Contain("Mid-Semester Exam CS2012");
        result.ConflictDetails.Should().NotBeNull();
        result.ConflictDetails!.Title.Should().Be("Mid-Semester Exam CS2012");
        result.ConflictDetails.IsBufferConflictOnly.Should().BeFalse();
        result.ConflictDetails.ConflictScope.Should().Be("Event");
    }

    // =========================================================================
    // 3. Test a conflict caused ONLY by the setup/cleanup buffer -> Returns Unavailable with buffer explanation
    // =========================================================================
    [Fact]
    public async Task CheckAvailability_ConflictCausedOnlyByBuffer_ReturnsUnavailable_WithBufferExplanation()
    {
        var coordEmail = "coord_buffer@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "CoordP@ss2026!", CampusRoles.EventCoordinator);

        var venue = await CreateTestVenueAsync("BUF-01", "Buffer Conflict Test Venue");
        var baseDate = DateTime.UtcNow.Date.AddDays(8);
        await AddCoverageAsync(venue.Id, baseDate.AddDays(-5), baseDate.AddDays(10));

        // Existing lecture block: 07:00 - 08:45 UTC
        await AddBlockAsync(venue.Id, "Morning Lecture EE1020", "Class", baseDate.AddHours(7), baseDate.AddHours(8).AddMinutes(45));

        // Event itself is 09:00 - 11:00 UTC. It does NOT overlap the lecture (09:00 > 08:45).
        // But setup buffer is 30 minutes! Occupied start is 08:30 UTC.
        // Therefore setup buffer (08:30 - 09:00) overlaps the lecture block (ending at 08:45) by 15 minutes!
        var request = new CheckAvailabilityRequest(
            EventStartUtc: baseDate.AddHours(9),
            EventEndUtc: baseDate.AddHours(11),
            SetupMinutes: 30,
            CleanupMinutes: 15
        );

        var response = await coordClient.PostAsJsonAsync($"/api/venues/{venue.Id}/check-availability", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CheckAvailabilityResponse>();

        result.Should().NotBeNull();
        result!.Status.Should().Be(AvailabilityStatus.Unavailable);
        result.IsAvailable.Should().BeFalse();
        result.ConflictDetails.Should().NotBeNull();
        result.ConflictDetails!.IsBufferConflictOnly.Should().BeTrue();
        result.ConflictDetails.ConflictScope.Should().Be("SetupBuffer");
        result.ConflictDetails.Title.Should().Be("Morning Lecture EE1020");
        result.Reason.Should().Contain("setup buffer");
    }

    // =========================================================================
    // 4. Test an unverified period -> Returns Availability unverified
    // =========================================================================
    [Fact]
    public async Task CheckAvailability_UnverifiedPeriod_OutsideCoverage_ReturnsAvailabilityUnverified()
    {
        var coordEmail = "coord_unverified@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "CoordP@ss2026!", CampusRoles.EventCoordinator);

        var venue = await CreateTestVenueAsync("UNVER-01", "Unverified Coverage Test Venue");
        var baseDate = DateTime.UtcNow.Date.AddDays(4);

        // Coverage only spans baseDate to baseDate + 3 days
        await AddCoverageAsync(venue.Id, baseDate, baseDate.AddDays(3), "Short Period Coverage");

        // Requested event is 30 days into the future (outside coverage)
        var futureDate = baseDate.AddDays(30);
        var request = new CheckAvailabilityRequest(futureDate.AddHours(9), futureDate.AddHours(11));

        var response = await coordClient.PostAsJsonAsync($"/api/venues/{venue.Id}/check-availability", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CheckAvailabilityResponse>();

        result.Should().NotBeNull();
        result!.Status.Should().Be(AvailabilityStatus.Unverified);
        result.IsAvailable.Should().BeFalse();
        result.Reason.Should().Contain("Availability unverified");
        result.Reason.Should().Contain("falls outside verified calendar coverage");
    }

    // =========================================================================
    // 5. Test facility staff attempting to edit another venue’s calendar -> Returns 403 Forbidden
    // =========================================================================
    [Fact]
    public async Task CalendarBlocks_FacilityStaff_AttemptingToEditAnotherVenue_Returns403Forbidden()
    {
        var staffEmail = "facility_staff_scoped@campus.internal";
        var staffPassword = "StaffPassword2026!";
        var staffUser = await EnsureUserWithRoleAsync(staffEmail, staffPassword, CampusRoles.FacilityAdmin);

        var venue1 = await CreateTestVenueAsync("V1-ASSIGNED", "Assigned Venue 1");
        var venue2 = await CreateTestVenueAsync("V2-UNASSIGNED", "Unassigned Venue 2");

        // Assign staffUser strictly to venue1
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            if (!await db.VenueManagers.AnyAsync(vm => vm.VenueId == venue1.Id && vm.UserId == staffUser.Id))
            {
                db.VenueManagers.Add(new VenueManager
                {
                    VenueId = venue1.Id,
                    UserId = staffUser.Id,
                    AssignedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
        }

        var staffClient = await CreateAuthenticatedClientAsync(staffEmail, staffPassword, CampusRoles.FacilityAdmin);

        // Attempt 1: Add calendar block to venue2 (unassigned) -> strictly 403 Forbidden
        var baseDate = DateTime.UtcNow.Date.AddDays(5);
        var blockReq = new CreateBlockRequest("Unauthorized Block", "Maintenance", baseDate.AddHours(9), baseDate.AddHours(11));

        var forbiddenResponse = await staffClient.PostAsJsonAsync($"/api/venues/{venue2.Id}/calendar-blocks", blockReq);
        forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Attempt 2: Add calendar block to venue1 (assigned) -> succeeds 201 Created
        var allowedBlockReq = new CreateBlockRequest("Authorized Maintenance", "Maintenance", baseDate.AddHours(9), baseDate.AddHours(11));
        var allowedResponse = await staffClient.PostAsJsonAsync($"/api/venues/{venue1.Id}/calendar-blocks", allowedBlockReq);
        allowedResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // =========================================================================
    // 6. Test UniversityAdmin with completed MFA can manage any venue's calendar
    // =========================================================================
    [Fact]
    public async Task CalendarBlocks_AdminWithMfa_CanManageAnyVenue()
    {
        var adminEmail = $"super_admin_mfa_{Guid.NewGuid():N}@campus.internal";
        var adminPassword = "AdminPassword2026!";
        var adminClient = await CreateAdminWithMfaClientAsync(adminEmail, adminPassword);

        var venue = await CreateTestVenueAsync("ADMIN-V1", "Admin Managed Venue");
        var baseDate = DateTime.UtcNow.Date.AddDays(6);

        var blockReq = new CreateBlockRequest("Admin Block", "Maintenance", baseDate.AddHours(9), baseDate.AddHours(11));
        var response = await adminClient.PostAsJsonAsync($"/api/venues/{venue.Id}/calendar-blocks", blockReq);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CalendarBlocks_AdminWithoutMfa_IsForbiddenFromManagingCalendar()
    {
        // Arrange: Admin with UniversityAdmin role but signed in with password only (no MFA challenge)
        var adminEmail = $"admin_nomfa_{Guid.NewGuid():N}@campus.internal";
        var adminPassword = "AdminPassword2026!";
        var adminClient = await CreateAuthenticatedClientAsync(adminEmail, adminPassword, CampusRoles.UniversityAdmin);

        var venue = await CreateTestVenueAsync("ADMIN-NOMFA", "Admin No MFA Venue");
        var baseDate = DateTime.UtcNow.Date.AddDays(6);

        var blockReq = new CreateBlockRequest("Forbidden Block", "Maintenance", baseDate.AddHours(9), baseDate.AddHours(11));

        // Act: Attempt to add calendar block without completed MFA session
        var response = await adminClient.PostAsJsonAsync($"/api/venues/{venue.Id}/calendar-blocks", blockReq);

        // Assert: Strictly 403 Forbidden
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CalendarBlocks_AdminWithMfa_CanLoadAndAddBlock_ForUnassignedVenue()
    {
        // Arrange: Admin with UniversityAdmin role and completed MFA
        var adminEmail = $"admin_unassigned_{Guid.NewGuid():N}@campus.internal";
        var adminPassword = "AdminPassword2026!";
        var adminClient = await CreateAdminWithMfaClientAsync(adminEmail, adminPassword);

        // Venue has no assigned managers
        var venue = await CreateTestVenueAsync("UNASSIGNED-V1", "Unassigned Pilot Venue");
        var baseDate = DateTime.UtcNow.Date.AddDays(7);

        // Act 1: GET calendar blocks
        var getResp = await adminClient.GetAsync($"/api/venues/{venue.Id}/calendar-blocks");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act 2: POST calendar block without needing a venue assignment
        var blockReq = new CreateBlockRequest("Governance Inspection", "Maintenance", baseDate.AddHours(8), baseDate.AddHours(10));
        var postResp = await adminClient.PostAsJsonAsync($"/api/venues/{venue.Id}/calendar-blocks", blockReq);

        // Assert: 201 Created
        postResp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // =========================================================================
    // 7. Test Venue Catalog CRUD requires UniversityAdminWithMfa
    // =========================================================================
    [Fact]
    public async Task Venues_CatalogManagement_AdminWithMfa_Succeeds_AndCoordinatorForbidden()
    {
        var adminEmail = $"catalog_admin_mfa_{Guid.NewGuid():N}@campus.internal";
        var adminPassword = "AdminPassword2026!";
        var adminClient = await CreateAdminWithMfaClientAsync(adminEmail, adminPassword);

        var coordEmail = $"catalog_coord_{Guid.NewGuid():N}@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "CoordP@ss2026!", CampusRoles.EventCoordinator);

        var createReq = new CreateVenueRequest(
            Name: "New Faculty Hall",
            Code: "NFH-" + Guid.NewGuid().ToString("N")[..4].ToUpper(),
            Type: "Departmental",
            ManagingUnit: "Faculty of Architecture",
            Capacity: 150,
            OperatingHoursStart: "08:00",
            OperatingHoursEnd: "19:00",
            MinNoticeHours: 48,
            DefaultSetupMinutes: 30,
            DefaultCleanupMinutes: 30,
            MaxBookingHours: 6,
            RequiresEventApproval: false
        );

        // Coordinator is forbidden from creating venues
        var coordResp = await coordClient.PostAsJsonAsync("/api/venues", createReq);
        coordResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Admin with MFA can create venue
        var adminResp = await adminClient.PostAsJsonAsync("/api/venues", createReq);
        adminResp.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
