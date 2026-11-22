using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Infrastructure.Email;
using CampusVenue.Api.Modules.Facilities.Domain;
using CampusVenue.Api.Modules.Facilities.Features.Availability;
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
using Npgsql;
using NpgsqlTypes;
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

    // =========================================================================
    // 10. Phase 1C.3: Successful Confirmation & Calendar Integration
    // =========================================================================
    [Fact]
    public async Task ConfirmBookingRequest_SuccessfulConfirmation_CreatesReservationAndUpdatesStatus()
    {
        var managerEmail = $"mgr_{Guid.NewGuid():N}@campus.internal";
        var manager = await EnsureUserWithRoleAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var managerClient = await CreateAuthenticatedClientAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var venue = await CreateTestVenueAsync("CONF1", manager, withCoverage: true, minNoticeHours: 0);

        var coordEmail = $"coord_{Guid.NewGuid():N}@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "CoordPass2026!", CampusRoles.EventCoordinator);

        var startUtc = DateTime.UtcNow.Date.AddDays(2).AddHours(4).AddMinutes(30);
        var endUtc = startUtc.AddHours(2);

        // 1. Submit request
        var (cCsrf, _) = await GetAntiforgeryTokensAsync(coordClient);
        var subReq = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(new CreateBookingRequest(
                EventTitle: "AI Symposium 2026",
                Description: "Keynotes and research panels",
                ExpectedAttendance: 80,
                EventStartUtc: startUtc,
                EventEndUtc: endUtc,
                SetupMinutes: 30,
                CleanupMinutes: 30,
                IdempotencyKey: Guid.NewGuid().ToString("N")
            ))
        };
        subReq.Headers.Add("X-XSRF-TOKEN", cCsrf);
        var subResp = await coordClient.SendAsync(subReq);
        subResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await subResp.Content.ReadFromJsonAsync<BookingRequestDto>();

        // 2. Manager approves
        var (mCsrf, _) = await GetAntiforgeryTokensAsync(managerClient);
        var revReq = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{created!.Id}/review")
        {
            Content = JsonContent.Create(new ReviewBookingRequest("Approve", "Approved without conditions", null))
        };
        revReq.Headers.Add("X-XSRF-TOKEN", mCsrf);
        var revResp = await managerClient.SendAsync(revReq);
        revResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Coordinator confirms
        var (confCsrf, _) = await GetAntiforgeryTokensAsync(coordClient);
        var confReq = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{created.Id}/confirm");
        confReq.Headers.Add("X-XSRF-TOKEN", confCsrf);
        var confResp = await coordClient.SendAsync(confReq);
        confResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var confirmed = await confResp.Content.ReadFromJsonAsync<BookingRequestDto>();
        confirmed.Should().NotBeNull();
        confirmed!.Status.Should().Be(BookingRequestStatus.Confirmed);
        confirmed.ReservationReference.Should().NotBeNullOrWhiteSpace();
        confirmed.ReservationReference.Should().StartWith("RES-");
        confirmed.ConfirmedReservationId.Should().NotBeNull();
        confirmed.ConfirmedAtUtc.Should().NotBeNull();

        // 4. Verify Database Persistence
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();

            var reqInDb = await db.VenueBookingRequests
                .Include(r => r.ConfirmedReservation)
                .Include(r => r.Notifications)
                .FirstOrDefaultAsync(r => r.Id == created.Id);

            reqInDb.Should().NotBeNull();
            reqInDb!.Status.Should().Be(BookingRequestStatus.Confirmed);
            reqInDb.ConfirmedReservation.Should().NotBeNull();
            reqInDb.ConfirmedReservation!.ReservationReference.Should().Be(confirmed.ReservationReference);
            reqInDb.ConfirmedReservation.VenueId.Should().Be(venue.Id);

            // Check Notifications: BookingConfirmed to coordinator, BookingConfirmedManagerNotice to manager
            reqInDb.Notifications.Should().Contain(n => n.NotificationType == "BookingConfirmed" && n.RecipientEmail == coordEmail);
            reqInDb.Notifications.Should().Contain(n => n.NotificationType == "BookingConfirmedManagerNotice" && n.RecipientEmail == managerEmail);

            // 5. Verify availability checker now reports this venue as Unavailable for the occupied window
            var availService = scope.ServiceProvider.GetRequiredService<AvailabilityService>();
            var availCheck = await availService.CheckAvailabilityAsync(
                venue,
                new CheckAvailabilityRequest(startUtc, endUtc, 30, 30),
                db);

            availCheck.Status.Should().Be(AvailabilityStatus.Unavailable);
            availCheck.ConflictDetails.Should().NotBeNull();
            availCheck.ConflictDetails!.BlockType.Should().Be("ConfirmedReservation");
            availCheck.Reason.Should().Contain(confirmed.ReservationReference);
        }
    }

    // =========================================================================
    // 11. Phase 1C.3: Calendar Block Added After Approval -> Conflict & Stays AwaitingConfirmation
    // =========================================================================
    [Fact]
    public async Task ConfirmBookingRequest_CalendarBlockAddedAfterApproval_Returns409Conflict_RequestStaysAwaitingConfirmation()
    {
        var managerEmail = $"mgr_{Guid.NewGuid():N}@campus.internal";
        var manager = await EnsureUserWithRoleAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var managerClient = await CreateAuthenticatedClientAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var venue = await CreateTestVenueAsync("CONF2", manager, withCoverage: true, minNoticeHours: 0);

        var coordEmail = $"coord_{Guid.NewGuid():N}@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "CoordPass2026!", CampusRoles.EventCoordinator);

        var startUtc = DateTime.UtcNow.Date.AddDays(3).AddHours(4).AddMinutes(30);
        var endUtc = startUtc.AddHours(2);

        // 1. Submit request
        var (cCsrf, _) = await GetAntiforgeryTokensAsync(coordClient);
        var subReq = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(new CreateBookingRequest(
                EventTitle: "Robotics Exhibition",
                Description: null,
                ExpectedAttendance: 50,
                EventStartUtc: startUtc,
                EventEndUtc: endUtc,
                SetupMinutes: 30,
                CleanupMinutes: 30,
                IdempotencyKey: Guid.NewGuid().ToString("N")
            ))
        };
        subReq.Headers.Add("X-XSRF-TOKEN", cCsrf);
        var subResp = await coordClient.SendAsync(subReq);
        subResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await subResp.Content.ReadFromJsonAsync<BookingRequestDto>();

        // 2. Manager reviews & approves -> AwaitingConfirmation
        var (mCsrf, _) = await GetAntiforgeryTokensAsync(managerClient);
        var revReq = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{created!.Id}/review")
        {
            Content = JsonContent.Create(new ReviewBookingRequest("Approve", "Approved", null))
        };
        revReq.Headers.Add("X-XSRF-TOKEN", mCsrf);
        var revResp = await managerClient.SendAsync(revReq);
        revResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Manager inserts a sudden calendar block (e.g. emergency maintenance) overlapping the event
        var (bCsrf, _) = await GetAntiforgeryTokensAsync(managerClient);
        var blockReq = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/calendar-blocks")
        {
            Content = JsonContent.Create(new CreateBlockRequest(
                Title: "Emergency HVAC Duct Repair",
                BlockType: "Maintenance",
                StartUtc: startUtc.AddMinutes(-10),
                EndUtc: startUtc.AddHours(1)
            ))
        };
        blockReq.Headers.Add("X-XSRF-TOKEN", bCsrf);
        var blockResp = await managerClient.SendAsync(blockReq);
        blockResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // 4. Coordinator attempts to confirm -> Availability recheck fails -> 409 Conflict
        var (confCsrf, _) = await GetAntiforgeryTokensAsync(coordClient);
        var confReq = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{created.Id}/confirm");
        confReq.Headers.Add("X-XSRF-TOKEN", confCsrf);
        var confResp = await coordClient.SendAsync(confReq);
        confResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 5. Verify database: Request stays in AwaitingConfirmation, zero confirmed reservations created
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();

            var reqInDb = await db.VenueBookingRequests
                .Include(r => r.ConfirmedReservation)
                .FirstOrDefaultAsync(r => r.Id == created.Id);

            reqInDb.Should().NotBeNull();
            reqInDb!.Status.Should().Be(BookingRequestStatus.AwaitingConfirmation);
            reqInDb.ConfirmedReservation.Should().BeNull();

            var anyReservation = await db.ConfirmedReservations
                .AnyAsync(r => r.BookingRequestId == created.Id);
            anyReservation.Should().BeFalse();
        }
    }

    // =========================================================================
    // 12. Phase 1C.3: Overlapping Buffers -> 409 Conflict & Stays AwaitingConfirmation
    // =========================================================================
    [Fact]
    public async Task ConfirmBookingRequest_OverlappingBuffers_Returns409Conflict_RequestStaysAwaitingConfirmation()
    {
        var managerEmail = $"mgr_{Guid.NewGuid():N}@campus.internal";
        var manager = await EnsureUserWithRoleAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var managerClient = await CreateAuthenticatedClientAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var venue = await CreateTestVenueAsync("CONF3", manager, withCoverage: true, minNoticeHours: 0);

        var coord1Email = $"coord1_{Guid.NewGuid():N}@campus.internal";
        var coord1Client = await CreateAuthenticatedClientAsync(coord1Email, "CoordPass2026!", CampusRoles.EventCoordinator);

        var coord2Email = $"coord2_{Guid.NewGuid():N}@campus.internal";
        var coord2Client = await CreateAuthenticatedClientAsync(coord2Email, "CoordPass2026!", CampusRoles.EventCoordinator);

        // Req 1: event 04:30 - 06:00 UTC, 30m setup (04:00), 30m cleanup (06:30) -> Occupied: 04:00 - 06:30
        var start1 = DateTime.UtcNow.Date.AddDays(4).AddHours(4).AddMinutes(30);
        var end1 = start1.AddHours(1).AddMinutes(30);

        // Req 2: event 06:15 - 08:00 UTC, 30m setup (05:45), 30m cleanup (08:30) -> Occupied: 05:45 - 08:30
        // Events do NOT overlap (04:30-06:00 vs 06:15-08:00)
        // BUT buffers overlap: Req 1 cleanup buffer (06:00-06:30) overlaps Req 2 setup buffer (05:45-06:15)
        var start2 = end1.AddMinutes(15);
        var end2 = start2.AddHours(1).AddMinutes(45);

        // Submit and approve Req 1
        var (c1Csrf, _) = await GetAntiforgeryTokensAsync(coord1Client);
        var sub1 = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(new CreateBookingRequest("Morning Workshop", null, 40, start1, end1, 30, 30, Guid.NewGuid().ToString("N")))
        };
        sub1.Headers.Add("X-XSRF-TOKEN", c1Csrf);
        var sub1Resp = await coord1Client.SendAsync(sub1);
        var req1 = await sub1Resp.Content.ReadFromJsonAsync<BookingRequestDto>();

        // Submit and approve Req 2
        var (c2Csrf, _) = await GetAntiforgeryTokensAsync(coord2Client);
        var sub2 = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(new CreateBookingRequest("Afternoon Seminar", null, 40, start2, end2, 30, 30, Guid.NewGuid().ToString("N")))
        };
        sub2.Headers.Add("X-XSRF-TOKEN", c2Csrf);
        var sub2Resp = await coord2Client.SendAsync(sub2);
        var req2 = await sub2Resp.Content.ReadFromJsonAsync<BookingRequestDto>();

        var (mCsrf, _) = await GetAntiforgeryTokensAsync(managerClient);
        var rev1 = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{req1!.Id}/review")
        {
            Content = JsonContent.Create(new ReviewBookingRequest("Approve", "Approved", null))
        };
        rev1.Headers.Add("X-XSRF-TOKEN", mCsrf);
        await managerClient.SendAsync(rev1);

        var (mCsrf2, _) = await GetAntiforgeryTokensAsync(managerClient);
        var rev2 = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{req2!.Id}/review")
        {
            Content = JsonContent.Create(new ReviewBookingRequest("Approve", "Approved", null))
        };
        rev2.Headers.Add("X-XSRF-TOKEN", mCsrf2);
        await managerClient.SendAsync(rev2);

        // 1. Confirm Req 1 -> 200 OK
        var (conf1Csrf, _) = await GetAntiforgeryTokensAsync(coord1Client);
        var conf1 = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{req1.Id}/confirm");
        conf1.Headers.Add("X-XSRF-TOKEN", conf1Csrf);
        var conf1Resp = await coord1Client.SendAsync(conf1);
        conf1Resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Confirm Req 2 -> Buffers collide -> 409 Conflict
        var (conf2Csrf, _) = await GetAntiforgeryTokensAsync(coord2Client);
        var conf2 = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{req2.Id}/confirm");
        conf2.Headers.Add("X-XSRF-TOKEN", conf2Csrf);
        var conf2Resp = await coord2Client.SendAsync(conf2);
        conf2Resp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 3. Verify Req 2 stays in AwaitingConfirmation
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var req2InDb = await db.VenueBookingRequests.FindAsync(req2.Id);
            req2InDb!.Status.Should().Be(BookingRequestStatus.AwaitingConfirmation);
        }
    }

    // =========================================================================
    // 13. Phase 1C.3: Adjacent Occupied Ranges -> Both Confirm Successfully
    // =========================================================================
    [Fact]
    public async Task ConfirmBookingRequest_AdjacentOccupiedRanges_BothConfirmSuccessfully()
    {
        var managerEmail = $"mgr_{Guid.NewGuid():N}@campus.internal";
        var manager = await EnsureUserWithRoleAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var managerClient = await CreateAuthenticatedClientAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var venue = await CreateTestVenueAsync("CONF4", manager, withCoverage: true, minNoticeHours: 0);

        var coord1Email = $"coord1_{Guid.NewGuid():N}@campus.internal";
        var coord1Client = await CreateAuthenticatedClientAsync(coord1Email, "CoordPass2026!", CampusRoles.EventCoordinator);

        var coord2Email = $"coord2_{Guid.NewGuid():N}@campus.internal";
        var coord2Client = await CreateAuthenticatedClientAsync(coord2Email, "CoordPass2026!", CampusRoles.EventCoordinator);

        // Req 1: event 04:30 - 05:30 UTC, 30m setup (04:00), 30m cleanup (06:00) -> Occupied: [04:00, 06:00)
        var start1 = DateTime.UtcNow.Date.AddDays(5).AddHours(4).AddMinutes(30);
        var end1 = start1.AddHours(1);

        // Req 2: event 06:30 - 07:30 UTC, 30m setup (06:00), 30m cleanup (08:00) -> Occupied: [06:00, 08:00)
        // Req 2 occupied interval begins EXACTLY at 06:00 when Req 1 occupied interval ends at 06:00!
        var start2 = end1.AddHours(1);
        var end2 = start2.AddHours(1);

        // Submit Req 1
        var (c1Csrf, _) = await GetAntiforgeryTokensAsync(coord1Client);
        var sub1 = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(new CreateBookingRequest("Session 1", null, 30, start1, end1, 30, 30, Guid.NewGuid().ToString("N")))
        };
        sub1.Headers.Add("X-XSRF-TOKEN", c1Csrf);
        var sub1Resp = await coord1Client.SendAsync(sub1);
        var req1 = await sub1Resp.Content.ReadFromJsonAsync<BookingRequestDto>();

        // Submit Req 2
        var (c2Csrf, _) = await GetAntiforgeryTokensAsync(coord2Client);
        var sub2 = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(new CreateBookingRequest("Session 2", null, 30, start2, end2, 30, 30, Guid.NewGuid().ToString("N")))
        };
        sub2.Headers.Add("X-XSRF-TOKEN", c2Csrf);
        var sub2Resp = await coord2Client.SendAsync(sub2);
        var req2 = await sub2Resp.Content.ReadFromJsonAsync<BookingRequestDto>();

        // Manager approves both
        var (mCsrf, _) = await GetAntiforgeryTokensAsync(managerClient);
        var rev1 = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{req1!.Id}/review")
        {
            Content = JsonContent.Create(new ReviewBookingRequest("Approve", "Approved", null))
        };
        rev1.Headers.Add("X-XSRF-TOKEN", mCsrf);
        await managerClient.SendAsync(rev1);

        var (mCsrf2, _) = await GetAntiforgeryTokensAsync(managerClient);
        var rev2 = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{req2!.Id}/review")
        {
            Content = JsonContent.Create(new ReviewBookingRequest("Approve", "Approved", null))
        };
        rev2.Headers.Add("X-XSRF-TOKEN", mCsrf2);
        await managerClient.SendAsync(rev2);

        // 1. Confirm Req 1 -> 200 OK
        var (conf1Csrf, _) = await GetAntiforgeryTokensAsync(coord1Client);
        var conf1 = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{req1.Id}/confirm");
        conf1.Headers.Add("X-XSRF-TOKEN", conf1Csrf);
        var conf1Resp = await coord1Client.SendAsync(conf1);
        conf1Resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Confirm Req 2 -> Adjacent range [06:00, 08:00) touches [04:00, 06:00) without overlap -> 200 OK!
        var (conf2Csrf, _) = await GetAntiforgeryTokensAsync(coord2Client);
        var conf2 = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{req2.Id}/confirm");
        conf2.Headers.Add("X-XSRF-TOKEN", conf2Csrf);
        var conf2Resp = await coord2Client.SendAsync(conf2);
        conf2Resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Both must be confirmed in database with distinct reservation references
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();

            var r1 = await db.ConfirmedReservations.FirstOrDefaultAsync(r => r.BookingRequestId == req1.Id);
            var r2 = await db.ConfirmedReservations.FirstOrDefaultAsync(r => r.BookingRequestId == req2.Id);

            r1.Should().NotBeNull();
            r2.Should().NotBeNull();
            r1!.ReservationReference.Should().NotBe(r2!.ReservationReference);
            r1.OccupiedEndUtc.Should().Be(r2.OccupiedStartUtc);
        }
    }

    // =========================================================================
    // 14. Phase 1C.3: Unauthorized Caller -> 403 Forbidden
    // =========================================================================
    [Fact]
    public async Task ConfirmBookingRequest_UnauthorizedCaller_Returns403Forbidden()
    {
        var managerEmail = $"mgr_{Guid.NewGuid():N}@campus.internal";
        var manager = await EnsureUserWithRoleAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var managerClient = await CreateAuthenticatedClientAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var venue = await CreateTestVenueAsync("CONF5", manager, withCoverage: true, minNoticeHours: 0);

        var coord1Email = $"coord1_{Guid.NewGuid():N}@campus.internal";
        var coord1Client = await CreateAuthenticatedClientAsync(coord1Email, "CoordPass2026!", CampusRoles.EventCoordinator);

        var coord2Email = $"coord2_{Guid.NewGuid():N}@campus.internal";
        var coord2Client = await CreateAuthenticatedClientAsync(coord2Email, "CoordPass2026!", CampusRoles.EventCoordinator);

        var startUtc = DateTime.UtcNow.Date.AddDays(6).AddHours(4).AddMinutes(30);
        var endUtc = startUtc.AddHours(2);

        // Coord 1 submits request
        var (c1Csrf, _) = await GetAntiforgeryTokensAsync(coord1Client);
        var sub = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(new CreateBookingRequest("Cultural Gala", null, 100, startUtc, endUtc, 30, 30, Guid.NewGuid().ToString("N")))
        };
        sub.Headers.Add("X-XSRF-TOKEN", c1Csrf);
        var subResp = await coord1Client.SendAsync(sub);
        var req = await subResp.Content.ReadFromJsonAsync<BookingRequestDto>();

        // Manager approves
        var (mCsrf, _) = await GetAntiforgeryTokensAsync(managerClient);
        var rev = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{req!.Id}/review")
        {
            Content = JsonContent.Create(new ReviewBookingRequest("Approve", "Approved", null))
        };
        rev.Headers.Add("X-XSRF-TOKEN", mCsrf);
        await managerClient.SendAsync(rev);

        // Coord 2 (not the owner) attempts to confirm -> 403 Forbidden
        var (c2Csrf, _) = await GetAntiforgeryTokensAsync(coord2Client);
        var conf = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{req.Id}/confirm");
        conf.Headers.Add("X-XSRF-TOKEN", c2Csrf);
        var confResp = await coord2Client.SendAsync(conf);
        confResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Verify status in DB remains AwaitingConfirmation
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            var reqInDb = await db.VenueBookingRequests.FindAsync(req.Id);
            reqInDb!.Status.Should().Be(BookingRequestStatus.AwaitingConfirmation);
        }
    }

    // =========================================================================
    // 15. Phase 1C.3: Concurrency Test — Simultaneous Confirmation of Overlapping Requests
    // =========================================================================
    [Fact]
    public async Task ConfirmBookingRequest_SimultaneousConfirmation_OnlyOneWins_LoserStaysAwaitingConfirmation()
    {
        var managerEmail = $"mgr_{Guid.NewGuid():N}@campus.internal";
        var manager = await EnsureUserWithRoleAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var managerClient = await CreateAuthenticatedClientAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var venue = await CreateTestVenueAsync("CONF6", manager, withCoverage: true, minNoticeHours: 0);

        var coordAEmail = $"coordA_{Guid.NewGuid():N}@campus.internal";
        var coordAClient = await CreateAuthenticatedClientAsync(coordAEmail, "CoordPass2026!", CampusRoles.EventCoordinator);

        var coordBEmail = $"coordB_{Guid.NewGuid():N}@campus.internal";
        var coordBClient = await CreateAuthenticatedClientAsync(coordBEmail, "CoordPass2026!", CampusRoles.EventCoordinator);

        // Overlapping intervals
        var startA = DateTime.UtcNow.Date.AddDays(7).AddHours(4).AddMinutes(30);
        var endA = startA.AddHours(2);

        var startB = startA.AddMinutes(30);
        var endB = endA.AddMinutes(30);

        // Submit both requests
        var (caCsrf, _) = await GetAntiforgeryTokensAsync(coordAClient);
        var subA = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(new CreateBookingRequest("Tech Summit Track A", null, 50, startA, endA, 30, 30, Guid.NewGuid().ToString("N")))
        };
        subA.Headers.Add("X-XSRF-TOKEN", caCsrf);
        var subAResp = await coordAClient.SendAsync(subA);
        var reqA = await subAResp.Content.ReadFromJsonAsync<BookingRequestDto>();

        var (cbCsrf, _) = await GetAntiforgeryTokensAsync(coordBClient);
        var subB = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(new CreateBookingRequest("Tech Summit Track B", null, 50, startB, endB, 30, 30, Guid.NewGuid().ToString("N")))
        };
        subB.Headers.Add("X-XSRF-TOKEN", cbCsrf);
        var subBResp = await coordBClient.SendAsync(subB);
        var reqB = await subBResp.Content.ReadFromJsonAsync<BookingRequestDto>();

        // Manager approves both to AwaitingConfirmation
        var (mCsrf1, _) = await GetAntiforgeryTokensAsync(managerClient);
        var revA = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{reqA!.Id}/review")
        {
            Content = JsonContent.Create(new ReviewBookingRequest("Approve", "Approved", null))
        };
        revA.Headers.Add("X-XSRF-TOKEN", mCsrf1);
        await managerClient.SendAsync(revA);

        var (mCsrf2, _) = await GetAntiforgeryTokensAsync(managerClient);
        var revB = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{reqB!.Id}/review")
        {
            Content = JsonContent.Create(new ReviewBookingRequest("Approve", "Approved", null))
        };
        revB.Headers.Add("X-XSRF-TOKEN", mCsrf2);
        await managerClient.SendAsync(revB);

        // Prepare simultaneous confirmation requests
        var (csrfConfA, _) = await GetAntiforgeryTokensAsync(coordAClient);
        var confA = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{reqA.Id}/confirm");
        confA.Headers.Add("X-XSRF-TOKEN", csrfConfA);

        var (csrfConfB, _) = await GetAntiforgeryTokensAsync(coordBClient);
        var confB = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{reqB.Id}/confirm");
        confB.Headers.Add("X-XSRF-TOKEN", csrfConfB);

        // Execute simultaneously against PostgreSQL
        var taskA = coordAClient.SendAsync(confA);
        var taskB = coordBClient.SendAsync(confB);
        var responses = await Task.WhenAll(taskA, taskB);

        var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        // Core concurrency invariant: exactly ONE wins (200 OK), exactly ONE loses (409 Conflict)
        okCount.Should().Be(1, "Exactly one overlapping request must be confirmed under concurrent load");
        conflictCount.Should().Be(1, "The losing request must receive 409 Conflict");

        // Verify database state: one confirmed, one stays in AwaitingConfirmation
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();

            var reqAInDb = await db.VenueBookingRequests.Include(r => r.ConfirmedReservation).FirstOrDefaultAsync(r => r.Id == reqA.Id);
            var reqBInDb = await db.VenueBookingRequests.Include(r => r.ConfirmedReservation).FirstOrDefaultAsync(r => r.Id == reqB.Id);

            var confirmedCount = (reqAInDb!.Status == BookingRequestStatus.Confirmed ? 1 : 0) +
                                 (reqBInDb!.Status == BookingRequestStatus.Confirmed ? 1 : 0);
            var awaitingCount = (reqAInDb.Status == BookingRequestStatus.AwaitingConfirmation ? 1 : 0) +
                                (reqBInDb.Status == BookingRequestStatus.AwaitingConfirmation ? 1 : 0);

            confirmedCount.Should().Be(1);
            awaitingCount.Should().Be(1);

            // Exactly 1 ConfirmedReservation exists for this venue across both requests
            var totalConfirmed = await db.ConfirmedReservations
                .CountAsync(r => r.BookingRequestId == reqA.Id || r.BookingRequestId == reqB.Id);
            totalConfirmed.Should().Be(1);
        }
    }

    // =========================================================================
    // 16. Phase 1C.3: Retrying Already Confirmed Request -> Returns Existing Reservation
    // =========================================================================
    [Fact]
    public async Task ConfirmBookingRequest_RetryAlreadyConfirmed_ReturnsExistingReservationWithoutDuplicates()
    {
        var managerEmail = $"mgr_{Guid.NewGuid():N}@campus.internal";
        var manager = await EnsureUserWithRoleAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var managerClient = await CreateAuthenticatedClientAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var venue = await CreateTestVenueAsync("CONF7", manager, withCoverage: true, minNoticeHours: 0);

        var coordEmail = $"coord_{Guid.NewGuid():N}@campus.internal";
        var coordClient = await CreateAuthenticatedClientAsync(coordEmail, "CoordPass2026!", CampusRoles.EventCoordinator);

        var startUtc = DateTime.UtcNow.Date.AddDays(8).AddHours(4).AddMinutes(30);
        var endUtc = startUtc.AddHours(2);

        // Submit and approve
        var (cCsrf, _) = await GetAntiforgeryTokensAsync(coordClient);
        var subReq = new HttpRequestMessage(HttpMethod.Post, $"/api/venues/{venue.Id}/booking-requests")
        {
            Content = JsonContent.Create(new CreateBookingRequest("Idempotent Test Event", null, 30, startUtc, endUtc, 30, 30, Guid.NewGuid().ToString("N")))
        };
        subReq.Headers.Add("X-XSRF-TOKEN", cCsrf);
        var subResp = await coordClient.SendAsync(subReq);
        var created = await subResp.Content.ReadFromJsonAsync<BookingRequestDto>();

        var (mCsrf, _) = await GetAntiforgeryTokensAsync(managerClient);
        var revReq = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{created!.Id}/review")
        {
            Content = JsonContent.Create(new ReviewBookingRequest("Approve", "Approved", null))
        };
        revReq.Headers.Add("X-XSRF-TOKEN", mCsrf);
        await managerClient.SendAsync(revReq);

        // 1. Initial confirmation
        var (confCsrf1, _) = await GetAntiforgeryTokensAsync(coordClient);
        var confReq1 = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{created.Id}/confirm");
        confReq1.Headers.Add("X-XSRF-TOKEN", confCsrf1);
        var confResp1 = await coordClient.SendAsync(confReq1);
        confResp1.StatusCode.Should().Be(HttpStatusCode.OK);

        var initialDto = await confResp1.Content.ReadFromJsonAsync<BookingRequestDto>();
        initialDto!.ReservationReference.Should().NotBeNullOrWhiteSpace();

        // 2. Retry confirmation for the same already-confirmed request
        var (confCsrf2, _) = await GetAntiforgeryTokensAsync(coordClient);
        var confReq2 = new HttpRequestMessage(HttpMethod.Post, $"/api/booking-requests/{created.Id}/confirm");
        confReq2.Headers.Add("X-XSRF-TOKEN", confCsrf2);
        var confResp2 = await coordClient.SendAsync(confReq2);
        confResp2.StatusCode.Should().Be(HttpStatusCode.OK);

        var retryDto = await confResp2.Content.ReadFromJsonAsync<BookingRequestDto>();
        retryDto.Should().NotBeNull();
        retryDto!.Status.Should().Be(BookingRequestStatus.Confirmed);
        retryDto.ReservationReference.Should().Be(initialDto.ReservationReference);
        retryDto.ConfirmedReservationId.Should().Be(initialDto.ConfirmedReservationId);

        // 3. Database verification: Exactly 1 reservation row exists
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();

            var count = await db.ConfirmedReservations
                .CountAsync(r => r.BookingRequestId == created.Id);
            count.Should().Be(1, "Idempotent retry must not create duplicate ConfirmedReservation records");
        }
    }

    // =========================================================================
    // 17. Phase 1C.3: Direct Database Exclusion Constraint Enforcement Test
    // =========================================================================
    [Fact]
    public async Task ConfirmedReservation_PostgreSQLGiSTExclusionConstraint_DirectlyRejectsOverlappingInserts()
    {
        // 1. Arrange: Create a venue, user, and two distinct booking requests in the database
        var managerEmail = $"mgr_{Guid.NewGuid():N}@campus.internal";
        var manager = await EnsureUserWithRoleAsync(managerEmail, "ManagerPass2026!", CampusRoles.FacilityAdmin);
        var venue = await CreateTestVenueAsync("EXCL", manager, withCoverage: true, minNoticeHours: 0);

        var coordEmail = $"coord_{Guid.NewGuid():N}@campus.internal";
        var coord = await EnsureUserWithRoleAsync(coordEmail, "CoordPass2026!", CampusRoles.EventCoordinator);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();

        var startUtc = DateTime.UtcNow.Date.AddDays(10).AddHours(4).AddMinutes(30);
        var endUtc = startUtc.AddHours(2);

        var req1 = new VenueBookingRequest
        {
            Id = Guid.NewGuid(),
            VenueId = venue.Id,
            RequesterId = coord.Id,
            EventTitle = "Direct DB Constraint Test 1",
            ExpectedAttendance = 50,
            EventStartUtc = startUtc,
            EventEndUtc = endUtc,
            SetupMinutes = 30,
            CleanupMinutes = 30,
            Status = BookingRequestStatus.AwaitingConfirmation,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var req2 = new VenueBookingRequest
        {
            Id = Guid.NewGuid(),
            VenueId = venue.Id,
            RequesterId = coord.Id,
            EventTitle = "Direct DB Constraint Test 2",
            ExpectedAttendance = 50,
            EventStartUtc = startUtc.AddMinutes(30),
            EventEndUtc = endUtc.AddMinutes(30),
            SetupMinutes = 30,
            CleanupMinutes = 30,
            Status = BookingRequestStatus.AwaitingConfirmation,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        db.VenueBookingRequests.AddRange(req1, req2);
        await db.SaveChangesAsync();

        // 2. Insert first confirmed reservation occupying [04:00, 07:00) UTC
        var occ1Start = DateTime.SpecifyKind(req1.EventStartUtc.AddMinutes(-req1.SetupMinutes), DateTimeKind.Utc);
        var occ1End = DateTime.SpecifyKind(req1.EventEndUtc.AddMinutes(req1.CleanupMinutes), DateTimeKind.Utc);
        var res1 = new ConfirmedReservation
        {
            Id = Guid.NewGuid(),
            BookingRequestId = req1.Id,
            VenueId = venue.Id,
            ReservationReference = $"RES-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20],
            EventTitle = req1.EventTitle,
            EventStartUtc = req1.EventStartUtc,
            EventEndUtc = req1.EventEndUtc,
            SetupMinutes = req1.SetupMinutes,
            CleanupMinutes = req1.CleanupMinutes,
            OccupiedStartUtc = occ1Start,
            OccupiedEndUtc = occ1End,
            OccupiedRange = new NpgsqlRange<DateTime>(occ1Start, true, occ1End, false),
            ConfirmedByUserId = coord.Id,
            ConfirmedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.ConfirmedReservations.Add(res1);
        await db.SaveChangesAsync();

        // 3. Act: Attempt to directly insert a second confirmed reservation for the SAME venue with overlapping occupied range [04:30, 07:30)
        var occ2Start = DateTime.SpecifyKind(req2.EventStartUtc.AddMinutes(-req2.SetupMinutes), DateTimeKind.Utc);
        var occ2End = DateTime.SpecifyKind(req2.EventEndUtc.AddMinutes(req2.CleanupMinutes), DateTimeKind.Utc);
        var res2 = new ConfirmedReservation
        {
            Id = Guid.NewGuid(),
            BookingRequestId = req2.Id,
            VenueId = venue.Id,
            ReservationReference = $"RES-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20],
            EventTitle = req2.EventTitle,
            EventStartUtc = req2.EventStartUtc,
            EventEndUtc = req2.EventEndUtc,
            SetupMinutes = req2.SetupMinutes,
            CleanupMinutes = req2.CleanupMinutes,
            OccupiedStartUtc = occ2Start,
            OccupiedEndUtc = occ2End,
            OccupiedRange = new NpgsqlRange<DateTime>(occ2Start, true, occ2End, false),
            ConfirmedByUserId = coord.Id,
            ConfirmedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.ConfirmedReservations.Add(res2);

        // 4. Assert: Direct SaveChangesAsync fails at the PostgreSQL database engine level independently of any API/service code
        var act = async () => await db.SaveChangesAsync();

        var exception = await act.Should().ThrowAsync<DbUpdateException>();
        exception.Which.InnerException.Should().BeOfType<PostgresException>();
        var pgEx = (PostgresException)exception.Which.InnerException!;
        pgEx.SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);
        pgEx.ConstraintName.Should().Be("ex_confirmed_reservations_venue_occupied_range");
    }
}
