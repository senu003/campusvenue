using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Infrastructure.Email;
using CampusVenue.Api.Modules.Facilities.Domain;
using CampusVenue.Api.Modules.Facilities.Features.Availability;
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

public class BookingRequestIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BookingRequestIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        TestDatabaseHelper.EnsureMigrated();
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
            Content = JsonContent.Create(new LoginRequest(email, password, RememberMe: false))
        };
        loginRequest.Headers.Add("X-XSRF-TOKEN", csrf);

        var loginResp = await client.SendAsync(loginRequest);
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        return client;
    }

    private async Task<HttpClient> CreateAdminWithMfaClientAsync(string email, string password)
    {
        var user = await EnsureUserWithRoleAsync(email, password, CampusRoles.UniversityAdmin);

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await userManager.SetTwoFactorEnabledAsync(user, true);
            await userManager.ResetAuthenticatorKeyAsync(user);
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var (csrf, _) = await GetAntiforgeryTokensAsync(client);
        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password, RememberMe: false))
        };
        loginRequest.Headers.Add("X-XSRF-TOKEN", csrf);

        var loginResp = await client.SendAsync(loginRequest);
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var code = await userManager.GenerateTwoFactorTokenAsync(user, "Authenticator");

            var (mfaCsrf, _) = await GetAntiforgeryTokensAsync(client);
            var mfaReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/two-factor/challenge")
            {
                Content = JsonContent.Create(new TwoFactorChallengeRequest(code, IsRecoveryCode: false, RememberMe: false))
            };
            mfaReq.Headers.Add("X-XSRF-TOKEN", mfaCsrf);

            var mfaResp = await client.SendAsync(mfaReq);
            mfaResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        return client;
    }

    private async Task<Venue> CreateTestVenueAsync(
        string codePrefix,
        ApplicationUser? assignedManager = null,
        bool withCoverage = true,
        int minNoticeHours = 24)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();

        var venue = new Venue
        {
            Id = Guid.NewGuid(),
            Name = $"Test Venue {codePrefix}",
            Code = $"{codePrefix}-{Guid.NewGuid():N}"[..12].ToUpper(),
            Type = "Central",
            ManagingUnit = "Facilities Dept",
            Capacity = 200,
            OperatingHoursStart = new TimeOnly(8, 0),
            OperatingHoursEnd = new TimeOnly(20, 0),
            MinNoticeHours = minNoticeHours,
            DefaultSetupMinutes = 30,
            DefaultCleanupMinutes = 30,
            MaxBookingHours = 8,
            RequiresEventApproval = true,
            GeneralRules = "Standard university venue policy. Clean after use.",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        db.Venues.Add(venue);

        if (assignedManager != null)
        {
            db.VenueManagers.Add(new VenueManager
            {
                VenueId = venue.Id,
                UserId = assignedManager.Id,
                AssignedAtUtc = DateTime.UtcNow
            });
        }

        if (withCoverage && assignedManager != null)
        {
            db.VenueCalendarCoverages.Add(new VenueCalendarCoverage
            {
                Id = Guid.NewGuid(),
                VenueId = venue.Id,
                SourceName = "Master Timetable",
                CoversFromUtc = DateTime.UtcNow.Date.AddDays(-10),
                CoversToUtc = DateTime.UtcNow.Date.AddDays(90),
                VerifiedByUserId = assignedManager.Id,
                VerifiedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return venue;
    }

    // =========================================================================
    // 1. Permission Boundaries: EventCoordinator can request; Participant cannot
    // =========================================================================
    [Fact]
    public async Task BookingRequest_Permissions_CoordinatorSucceeds_ParticipantForbidden()
    {
        var managerEmail = $"mgr_{Guid.NewGuid():N}@campus.internal";
        var manager = await EnsureUserWithRoleAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var venue = await CreateTestVenueAsync("PERM", manager, withCoverage: true, minNoticeHours: 0);

        var coordEmail = $"coord_{Guid.NewGuid():N}@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "CoordPass2026!", CampusRoles.EventCoordinator);

        var partEmail = $"part_{Guid.NewGuid():N}@campus.internal";
        var partClient = await CreateAuthenticatedClientAsync(partEmail, "PartPass2026!", CampusRoles.Participant);

        // Target slot tomorrow 10:00 - 12:00 local (04:30 - 06:30 UTC)
        var startUtc = DateTime.UtcNow.Date.AddDays(2).AddHours(4).AddMinutes(30);
        var endUtc = startUtc.AddHours(2);

        var req = new CreateBookingRequest(
            EventTitle: "Annual Debate Championship",
            Description: "Inter-faculty student debate",
            ExpectedAttendance: 50,
            EventStartUtc: startUtc,
            EventEndUtc: endUtc,
            SetupMinutes: 30,
            CleanupMinutes: 30,
            IdempotencyKey: Guid.NewGuid().ToString("N")
        );

        // 1. Participant tries to submit -> 403 Forbidden
        var (partCsrf, _) = await GetAntiforgeryTokensAsync(partClient);
        var partHttpReq = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(req)
        };
        partHttpReq.Headers.Add("X-XSRF-TOKEN", partCsrf);
        var partResp = await partClient.SendAsync(partHttpReq);
        partResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. EventCoordinator submits -> 201 Created
        var (coordCsrf, _) = await GetAntiforgeryTokensAsync(coordClient);
        var coordHttpReq = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(req)
        };
        coordHttpReq.Headers.Add("X-XSRF-TOKEN", coordCsrf);
        var coordResp = await coordClient.SendAsync(coordHttpReq);
        coordResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await coordResp.Content.ReadFromJsonAsync<BookingRequestDto>();
        created.Should().NotBeNull();
        created!.Status.Should().Be(BookingRequestStatus.PendingReview);
        created.EventTitle.Should().Be("Annual Debate Championship");
    }

    // =========================================================================
    // 2. Unavailable & Unverified Slots: Create NEITHER request NOR notifications
    // =========================================================================
    [Fact]
    public async Task BookingRequest_UnavailableOrUnverified_CreatesNeitherRequestNorNotification()
    {
        var managerEmail = $"mgr_{Guid.NewGuid():N}@campus.internal";
        var manager = await EnsureUserWithRoleAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);

        // Venue WITHOUT verified coverage
        var venue = await CreateTestVenueAsync("NOCOV", manager, withCoverage: false, minNoticeHours: 0);

        var coordEmail = $"coord_{Guid.NewGuid():N}@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "CoordPass2026!", CampusRoles.EventCoordinator);

        var startUtc = DateTime.UtcNow.Date.AddDays(2).AddHours(4).AddMinutes(30);
        var endUtc = startUtc.AddHours(2);

        var req = new CreateBookingRequest(
            EventTitle: "Unverified Slot Event",
            Description: "Should be rejected because coverage is missing",
            ExpectedAttendance: 50,
            EventStartUtc: startUtc,
            EventEndUtc: endUtc,
            SetupMinutes: 30,
            CleanupMinutes: 30,
            IdempotencyKey: Guid.NewGuid().ToString("N")
        );

        var (csrf, _) = await GetAntiforgeryTokensAsync(coordClient);
        var httpReq = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(req)
        };
        httpReq.Headers.Add("X-XSRF-TOKEN", csrf);
        var resp = await coordClient.SendAsync(httpReq);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await resp.Content.ReadAsStringAsync();
        content.Should().Contain("Availability unverified");

        // Verify database: ZERO requests and ZERO notifications created
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();

        var requestCount = await db.VenueBookingRequests.CountAsync(r => r.VenueId == venue.Id);
        requestCount.Should().Be(0);

        var notifCount = await db.BookingNotifications.CountAsync(n => n.RecipientUserId == manager.Id);
        notifCount.Should().Be(0);
    }

    // =========================================================================
    // 3. Venue with No Configured Manager: Fails submission clearly
    // =========================================================================
    [Fact]
    public async Task BookingRequest_NoVenueManagerConfigured_ReturnsBadRequest()
    {
        // Venue with NO assigned manager
        var venue = await CreateTestVenueAsync("NOMGR", assignedManager: null, withCoverage: false);

        var coordEmail = $"coord_{Guid.NewGuid():N}@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "CoordPass2026!", CampusRoles.EventCoordinator);

        var startUtc = DateTime.UtcNow.Date.AddDays(2).AddHours(4).AddMinutes(30);
        var endUtc = startUtc.AddHours(2);

        var req = new CreateBookingRequest(
            EventTitle: "Orphan Venue Event",
            Description: null,
            ExpectedAttendance: 20,
            EventStartUtc: startUtc,
            EventEndUtc: endUtc,
            SetupMinutes: 30,
            CleanupMinutes: 30,
            IdempotencyKey: Guid.NewGuid().ToString("N")
        );

        var (csrf, _) = await GetAntiforgeryTokensAsync(coordClient);
        var httpReq = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(req)
        };
        httpReq.Headers.Add("X-XSRF-TOKEN", csrf);
        var resp = await coordClient.SendAsync(httpReq);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await resp.Content.ReadAsStringAsync();
        content.Should().Contain("No venue manager is configured");
    }

    // =========================================================================
    // 4. Atomic Notifications & Dev Mailbox Delivery & Idempotency
    // =========================================================================
    [Fact]
    public async Task BookingRequest_AtomicNotification_And_Idempotency_Preserved()
    {
        var managerEmail = $"mgr_{Guid.NewGuid():N}@campus.internal";
        var manager = await EnsureUserWithRoleAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var venue = await CreateTestVenueAsync("ATOM", manager, withCoverage: true, minNoticeHours: 0);

        var coordEmail = $"coord_{Guid.NewGuid():N}@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "CoordPass2026!", CampusRoles.EventCoordinator);

        var idempotencyKey = $"idemp_{Guid.NewGuid():N}";
        var startUtc = DateTime.UtcNow.Date.AddDays(3).AddHours(4).AddMinutes(30);
        var endUtc = startUtc.AddHours(2);

        var req = new CreateBookingRequest(
            EventTitle: "Idempotent Tech Symposium",
            Description: "Testing single transaction & idempotency",
            ExpectedAttendance: 60,
            EventStartUtc: startUtc,
            EventEndUtc: endUtc,
            SetupMinutes: 30,
            CleanupMinutes: 30,
            IdempotencyKey: idempotencyKey
        );

        // First submission
        var (csrf1, _) = await GetAntiforgeryTokensAsync(coordClient);
        var httpReq1 = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(req)
        };
        httpReq1.Headers.Add("X-XSRF-TOKEN", csrf1);
        var resp1 = await coordClient.SendAsync(httpReq1);
        resp1.StatusCode.Should().Be(HttpStatusCode.Created);

        var firstResult = await resp1.Content.ReadFromJsonAsync<BookingRequestDto>();
        firstResult.Should().NotBeNull();

        // Check database: 1 request, 1 notification
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var reqCount = await db.VenueBookingRequests.CountAsync(r => r.IdempotencyKey == idempotencyKey);
            reqCount.Should().Be(1);

            var notifCount = await db.BookingNotifications.CountAsync(n => n.BookingRequestId == firstResult!.Id);
            notifCount.Should().Be(1);
        }

        // Second submission with same IdempotencyKey -> returns existing request (200 OK) without duplicates
        var (csrf2, _) = await GetAntiforgeryTokensAsync(coordClient);
        var httpReq2 = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(req)
        };
        httpReq2.Headers.Add("X-XSRF-TOKEN", csrf2);
        var resp2 = await coordClient.SendAsync(httpReq2);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondResult = await resp2.Content.ReadFromJsonAsync<BookingRequestDto>();
        secondResult.Should().NotBeNull();
        secondResult!.Id.Should().Be(firstResult!.Id);

        // Verify count remains 1 request and 1 notification
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var reqCount = await db.VenueBookingRequests.CountAsync(r => r.IdempotencyKey == idempotencyKey);
            reqCount.Should().Be(1);

            var notifCount = await db.BookingNotifications.CountAsync(n => n.BookingRequestId == firstResult!.Id);
            notifCount.Should().Be(1);
        }
    }

    // =========================================================================
    // 5. Manager Review: Segregation of Duties (Cannot review own request)
    // =========================================================================
    [Fact]
    public async Task BookingRequest_SegregationOfDuties_ReviewerCannotReviewOwnRequest()
    {
        // User who is BOTH EventCoordinator AND FacilityAdmin assigned to this venue
        var dualUserEmail = $"dual_{Guid.NewGuid():N}@campus.internal";
        var dualUser = await EnsureUserWithRoleAsync(dualUserEmail, "DualPass2026!", CampusRoles.FacilityAdmin);
        // Also grant EventCoordinator role
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var userInScope = await userManager.FindByIdAsync(dualUser.Id.ToString());
            await userManager.AddToRoleAsync(userInScope!, CampusRoles.EventCoordinator);
        }

        var venue = await CreateTestVenueAsync("SOD", dualUser, withCoverage: true, minNoticeHours: 0);

        var dualClient = await CreateAuthenticatedClientAsync(dualUserEmail, "DualPass2026!", CampusRoles.EventCoordinator);

        var startUtc = DateTime.UtcNow.Date.AddDays(4).AddHours(4).AddMinutes(30);
        var endUtc = startUtc.AddHours(2);

        var req = new CreateBookingRequest(
            EventTitle: "Self-Review Conflict Event",
            Description: null,
            ExpectedAttendance: 30,
            EventStartUtc: startUtc,
            EventEndUtc: endUtc,
            SetupMinutes: 30,
            CleanupMinutes: 30,
            IdempotencyKey: Guid.NewGuid().ToString("N")
        );

        var (csrf1, _) = await GetAntiforgeryTokensAsync(dualClient);
        var createReq = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(req)
        };
        createReq.Headers.Add("X-XSRF-TOKEN", csrf1);
        var createResp = await dualClient.SendAsync(createReq);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResp.Content.ReadFromJsonAsync<BookingRequestDto>();

        // Now dual user attempts to review their OWN request -> 403 Forbidden (Segregation of Duties)
        var reviewPayload = new ReviewBookingRequest(
            Decision: "Approve",
            Notes: "Self approval attempt",
            Conditions: null
        );

        var (csrf2, _) = await GetAntiforgeryTokensAsync(dualClient);
        var reviewReq = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{created!.Id}/review")
        {
            Content = JsonContent.Create(reviewPayload)
        };
        reviewReq.Headers.Add("X-XSRF-TOKEN", csrf2);
        var reviewResp = await dualClient.SendAsync(reviewReq);

        reviewResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var reviewBody = await reviewResp.Content.ReadAsStringAsync();
        reviewBody.Should().Contain("Segregation of duties violation");
    }

    // =========================================================================
    // 6. Conditional Approval Workflow: Propose conditions -> Accept -> AwaitingConfirmation
    // =========================================================================
    [Fact]
    public async Task BookingRequest_ApproveWithConditions_CoordinatorAccepts_TransitionsToAwaitingConfirmation()
    {
        var managerEmail = $"mgr_{Guid.NewGuid():N}@campus.internal";
        var manager = await EnsureUserWithRoleAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var venue = await CreateTestVenueAsync("COND-ACC", manager, withCoverage: true, minNoticeHours: 0);

        var coordEmail = $"coord_{Guid.NewGuid():N}@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "CoordPass2026!", CampusRoles.EventCoordinator);
        var managerClient = await CreateAuthenticatedClientAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);

        var startUtc = DateTime.UtcNow.Date.AddDays(5).AddHours(4).AddMinutes(30);
        var endUtc = startUtc.AddHours(2);

        // 1. Submit Request
        var req = new CreateBookingRequest(
            EventTitle: "Conditional Tech Showcase",
            Description: null,
            ExpectedAttendance: 45,
            EventStartUtc: startUtc,
            EventEndUtc: endUtc,
            SetupMinutes: 30,
            CleanupMinutes: 30,
            IdempotencyKey: Guid.NewGuid().ToString("N")
        );

        var (cCsrf, _) = await GetAntiforgeryTokensAsync(coordClient);
        var subReq = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(req)
        };
        subReq.Headers.Add("X-XSRF-TOKEN", cCsrf);
        var subResp = await coordClient.SendAsync(subReq);
        subResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await subResp.Content.ReadFromJsonAsync<BookingRequestDto>();

        // 2. Manager reviews: Approve with conditions
        var (mCsrf, _) = await GetAntiforgeryTokensAsync(managerClient);
        var reviewPayload = new ReviewBookingRequest(
            Decision: "ApproveWithConditions",
            Notes: "High attendance requires security guard",
            Conditions: "1. Hire 2 campus security guards.\n2. No sound system after 18:00."
        );
        var revReq = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{created!.Id}/review")
        {
            Content = JsonContent.Create(reviewPayload)
        };
        revReq.Headers.Add("X-XSRF-TOKEN", mCsrf);
        var revResp = await managerClient.SendAsync(revReq);
        revResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var reviewed = await revResp.Content.ReadFromJsonAsync<BookingRequestDto>();
        reviewed!.Status.Should().Be(BookingRequestStatus.ApprovedWithConditions);
        reviewed.Conditions.Should().Contain("Hire 2 campus security guards");
        reviewed.ReviewedByEmail.Should().Be(managerEmail);

        // 3. Coordinator accepts conditions
        var (respCsrf, _) = await GetAntiforgeryTokensAsync(coordClient);
        var acceptReq = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{created.Id}/respond-conditions")
        {
            Content = JsonContent.Create(new RespondConditionsRequest(Accept: true))
        };
        acceptReq.Headers.Add("X-XSRF-TOKEN", respCsrf);
        var acceptResp = await coordClient.SendAsync(acceptReq);
        acceptResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var accepted = await acceptResp.Content.ReadFromJsonAsync<BookingRequestDto>();
        accepted!.Status.Should().Be(BookingRequestStatus.AwaitingConfirmation);
        accepted.ConditionsAccepted.Should().BeTrue();
    }

    // =========================================================================
    // 7. Conditional Approval Workflow: Propose conditions -> Decline -> Cancelled
    // =========================================================================
    [Fact]
    public async Task BookingRequest_ApproveWithConditions_CoordinatorDeclines_TransitionsToCancelled()
    {
        var managerEmail = $"mgr_{Guid.NewGuid():N}@campus.internal";
        var manager = await EnsureUserWithRoleAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var venue = await CreateTestVenueAsync("COND-DEC", manager, withCoverage: true, minNoticeHours: 0);

        var coordEmail = $"coord_{Guid.NewGuid():N}@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "CoordPass2026!", CampusRoles.EventCoordinator);
        var managerClient = await CreateAuthenticatedClientAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);

        var startUtc = DateTime.UtcNow.Date.AddDays(6).AddHours(4).AddMinutes(30);
        var endUtc = startUtc.AddHours(2);

        // 1. Submit Request
        var req = new CreateBookingRequest(
            EventTitle: "Expensive Security Event",
            Description: null,
            ExpectedAttendance: 80,
            EventStartUtc: startUtc,
            EventEndUtc: endUtc,
            SetupMinutes: 30,
            CleanupMinutes: 30,
            IdempotencyKey: Guid.NewGuid().ToString("N")
        );

        var (cCsrf, _) = await GetAntiforgeryTokensAsync(coordClient);
        var subReq = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(req)
        };
        subReq.Headers.Add("X-XSRF-TOKEN", cCsrf);
        var subResp = await coordClient.SendAsync(subReq);
        var created = await subResp.Content.ReadFromJsonAsync<BookingRequestDto>();

        // 2. Manager reviews: Approve with conditions
        var (mCsrf, _) = await GetAntiforgeryTokensAsync(managerClient);
        var revReq = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{created!.Id}/review")
        {
            Content = JsonContent.Create(new ReviewBookingRequest(
                Decision: "ApproveWithConditions",
                Notes: "Unacceptable sound risk",
                Conditions: "Deposit Rs 50,000 security fee"
            ))
        };
        revReq.Headers.Add("X-XSRF-TOKEN", mCsrf);
        var revResp = await managerClient.SendAsync(revReq);
        revResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Coordinator declines conditions
        var (respCsrf, _) = await GetAntiforgeryTokensAsync(coordClient);
        var declineReq = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{created.Id}/respond-conditions")
        {
            Content = JsonContent.Create(new RespondConditionsRequest(Accept: false))
        };
        declineReq.Headers.Add("X-XSRF-TOKEN", respCsrf);
        var declineResp = await coordClient.SendAsync(declineReq);
        declineResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var declined = await declineResp.Content.ReadFromJsonAsync<BookingRequestDto>();
        declined!.Status.Should().Be(BookingRequestStatus.Cancelled);
        declined.ConditionsAccepted.Should().BeFalse();
    }

    // =========================================================================
    // 8. Standard Approval: Reviewer approves directly without conditions -> AwaitingConfirmation
    // =========================================================================
    [Fact]
    public async Task BookingRequest_ApproveStandard_TransitionsDirectlyToAwaitingConfirmation()
    {
        var managerEmail = $"mgr_{Guid.NewGuid():N}@campus.internal";
        var manager = await EnsureUserWithRoleAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var venue = await CreateTestVenueAsync("STD-APP", manager, withCoverage: true, minNoticeHours: 0);

        var coordEmail = $"coord_{Guid.NewGuid():N}@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "CoordPass2026!", CampusRoles.EventCoordinator);
        var managerClient = await CreateAuthenticatedClientAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);

        var startUtc = DateTime.UtcNow.Date.AddDays(7).AddHours(4).AddMinutes(30);
        var endUtc = startUtc.AddHours(2);

        // 1. Submit Request
        var req = new CreateBookingRequest(
            EventTitle: "Direct Approval Showcase",
            Description: "Testing standard approval without conditions",
            ExpectedAttendance: 35,
            EventStartUtc: startUtc,
            EventEndUtc: endUtc,
            SetupMinutes: 30,
            CleanupMinutes: 30,
            IdempotencyKey: Guid.NewGuid().ToString("N")
        );

        var (cCsrf, _) = await GetAntiforgeryTokensAsync(coordClient);
        var subReq = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(req)
        };
        subReq.Headers.Add("X-XSRF-TOKEN", cCsrf);
        var subResp = await coordClient.SendAsync(subReq);
        subResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await subResp.Content.ReadFromJsonAsync<BookingRequestDto>();

        // 2. Manager reviews: Standard Approve
        var (mCsrf, _) = await GetAntiforgeryTokensAsync(managerClient);
        var revReq = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{created!.Id}/review")
        {
            Content = JsonContent.Create(new ReviewBookingRequest(
                Decision: "Approve",
                Notes: "Standard approval granted. Ready for booking confirmation.",
                Conditions: null
            ))
        };
        revReq.Headers.Add("X-XSRF-TOKEN", mCsrf);
        var revResp = await managerClient.SendAsync(revReq);
        revResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var approved = await revResp.Content.ReadFromJsonAsync<BookingRequestDto>();
        approved!.Status.Should().Be(BookingRequestStatus.AwaitingConfirmation);
        approved.Conditions.Should().BeNull();
        approved.ReviewedByEmail.Should().Be(managerEmail);
        approved.ReviewNotes.Should().Be("Standard approval granted. Ready for booking confirmation.");
    }

    // =========================================================================
    // 9. Venue Rules Management: Assigned FacilityAdmin updates rules; Unassigned forbidden
    // =========================================================================
    [Fact]
    public async Task VenueRules_AssignedFacilityAdmin_CanUpdate_UnassignedForbidden()
    {
        var assignedMgrEmail = $"assigned_{Guid.NewGuid():N}@campus.internal";
        var assignedMgr = await EnsureUserWithRoleAsync(assignedMgrEmail, "MgrPass2026!", CampusRoles.FacilityAdmin);

        var unassignedMgrEmail = $"unassigned_{Guid.NewGuid():N}@campus.internal";
        var unassignedClient = await CreateAuthenticatedClientAsync(unassignedMgrEmail, "MgrPass2026!", CampusRoles.FacilityAdmin);

        var venue = await CreateTestVenueAsync("RULES", assignedMgr, withCoverage: true);

        var assignedClient = await CreateAuthenticatedClientAsync(assignedMgrEmail, "MgrPass2026!", CampusRoles.FacilityAdmin);

        var updateRulesReq = new UpdateVenueRulesRequest(
            OperatingHoursStart: "07:30",
            OperatingHoursEnd: "21:30",
            Capacity: 220,
            MinNoticeHours: 48,
            DefaultSetupMinutes: 60,
            DefaultCleanupMinutes: 45,
            MaxBookingHours: 10,
            RequiresEventApproval: true,
            GeneralRules: "Updated rules: No loud drumming after 19:00. Prior lab consent required."
        );

        // 1. Unassigned FacilityAdmin tries to update -> 403 Forbidden
        var (unCsrf, _) = await GetAntiforgeryTokensAsync(unassignedClient);
        var unReq = new HttpRequestMessage(HttpMethod.Put, $"/api/venues/{venue.Id}/rules")
        {
            Content = JsonContent.Create(updateRulesReq)
        };
        unReq.Headers.Add("X-XSRF-TOKEN", unCsrf);
        var unResp = await unassignedClient.SendAsync(unReq);
        unResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Assigned FacilityAdmin updates -> 200 OK
        var (asCsrf, _) = await GetAntiforgeryTokensAsync(assignedClient);
        var asReq = new HttpRequestMessage(HttpMethod.Put, $"/api/venues/{venue.Id}/rules")
        {
            Content = JsonContent.Create(updateRulesReq)
        };
        asReq.Headers.Add("X-XSRF-TOKEN", asCsrf);
        var asResp = await assignedClient.SendAsync(asReq);
        asResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await asResp.Content.ReadFromJsonAsync<VenueDto>();
        updated.Should().NotBeNull();
        updated!.Capacity.Should().Be(220);
        updated.OperatingHoursStart.Should().Be("07:30");
        updated.GeneralRules.Should().Contain("No loud drumming after 19:00");
    }
}
