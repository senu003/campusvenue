using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Json;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Modules.Identity.Domain;
using CampusVenue.Api.Modules.Identity.Features.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CampusVenue.Tests;

/// <summary>
/// Helper for resolving the PostgreSQL test database connection and applying pending migrations.
/// Requires an explicit test database connection string via environment variables.
/// </summary>
public static class TestDatabaseHelper
{
    private static readonly Lazy<string> ConnectionStringLazy = new(ResolveConnectionString);
    private static bool _migrated;
    private static readonly object _migrationLock = new();

    public static string ConnectionString => ConnectionStringLazy.Value;

    private static string ResolveConnectionString()
    {
        var envConn = Environment.GetEnvironmentVariable("CAMPUSVENUE_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(envConn))
        {
            return envConn;
        }

        throw new InvalidOperationException(
            "An explicit test database connection string must be configured via the 'CAMPUSVENUE_TEST_CONNECTION_STRING' " +
            "environment variable to run integration tests against PostgreSQL. " +
            "The application connection string ('ConnectionStrings__DefaultConnection') cannot be used for database tests. " +
            "Example: $env:CAMPUSVENUE_TEST_CONNECTION_STRING = \"Host=localhost;Port=5433;Database=campusvenue_dev;Username=campusvenue_user;Password=...\"");
    }

    public static void EnsureMigrated(IServiceProvider services)
    {
        if (_migrated) return;
        lock (_migrationLock)
        {
            if (_migrated) return;
            using var scope = services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CampusVenueDbContext>();
            dbContext.Database.Migrate();
            _migrated = true;
        }
    }
}

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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
            });
        });

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDatabaseHelper.ConnectionString,
                ["RateLimiting:AuthPermitLimit"] = "200",
                ["DataProtection:KeyPath"] = Path.Combine(Path.GetTempPath(), "campusvenue_dpkeys_test")
            });
        });
    }
}

public class ProductionHttpsWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

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
            });
        });

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDatabaseHelper.ConnectionString,
                ["RateLimiting:AuthPermitLimit"] = "200",
                ["DataProtection:KeyPath"] = Path.Combine(Path.GetTempPath(), "campusvenue_dpkeys_prod_test")
            });
        });
    }
}

public class ThrottledWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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
            });
        });

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDatabaseHelper.ConnectionString,
                ["RateLimiting:AuthPermitLimit"] = "2",
                ["DataProtection:KeyPath"] = Path.Combine(Path.GetTempPath(), "campusvenue_dpkeys_throttled_test")
            });
        });
    }
}

public class ShortLivedSessionWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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
            });

            services.Configure<CookieAuthenticationOptions>(
                IdentityConstants.ApplicationScheme,
                options =>
                {
                    options.ExpireTimeSpan = TimeSpan.FromSeconds(2);
                    options.SlidingExpiration = false;
                });
        });

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDatabaseHelper.ConnectionString,
                ["RateLimiting:AuthPermitLimit"] = "200",
                ["DataProtection:KeyPath"] = Path.Combine(Path.GetTempPath(), "campusvenue_dpkeys_shortlived_test")
            });
        });
    }
}

public class AuthIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        TestDatabaseHelper.EnsureMigrated(factory.Services);
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    private async Task<ApplicationUser> EnsureTestUserAsync(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
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
        }
        else
        {
            // Reset lockout counters if user already exists
            await userManager.SetLockoutEndDateAsync(user, null);
            await userManager.ResetAccessFailedCountAsync(user);
        }

        return user;
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

    [Fact]
    public async Task GetCurrentUser_WhenUnauthenticated_Returns401WithoutHtmlRedirect()
    {
        // Arrange: unauthenticated client without session cookies
        var unauthClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        // Act
        var response = await unauthClient.GetAsync("/api/auth/me");

        // Assert: 401 Unauthorized, never 302/HTML redirect
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task SessionExpiry_CorruptedOrInvalidCookie_Returns401Unauthorized()
    {
        // Arrange: client presenting an invalid cookie ticket
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", "CampusVenue.Auth=CfDJ8N_expired_or_tampered_payload_12345");

        // Act
        var response = await client.SendAsync(request);

        // Assert: 401 Unauthorized directly without redirect
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task Login_MissingAntiforgeryToken_Returns400BadRequest()
    {
        // Arrange
        var request = new LoginRequest("user@campus.internal", "Password123!");

        // Act: POST without X-XSRF-TOKEN header
        var response = await _client.PostAsJsonAsync("/api/auth/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Antiforgery Validation Failed");
    }

    [Fact]
    public async Task Login_InvalidAntiforgeryToken_Returns400BadRequest()
    {
        // Arrange: valid cookie but invalid token header
        var (_, cookie) = await GetAntiforgeryTokensAsync(_client);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("user@campus.internal", "Password123!"))
        };
        requestMessage.Headers.Add("X-XSRF-TOKEN", "invalid_tampered_csrf_token_xyz");
        if (!string.IsNullOrEmpty(cookie))
        {
            requestMessage.Headers.Add("Cookie", cookie);
        }

        // Act
        var response = await _client.SendAsync(requestMessage);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Antiforgery Validation Failed");
    }

    [Fact]
    public async Task Logout_MissingAntiforgeryToken_Returns400BadRequest()
    {
        // Arrange: unauthenticated or authenticated client attempting logout without CSRF token
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Antiforgery Validation Failed");
    }

    [Fact]
    public async Task Logout_InvalidAntiforgeryToken_Returns400BadRequest()
    {
        // Arrange: client presenting valid antiforgery cookie but forged header token
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var (_, cookie) = await GetAntiforgeryTokensAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("X-XSRF-TOKEN", "forged_logout_token_123");
        if (!string.IsNullOrEmpty(cookie))
        {
            request.Headers.Add("Cookie", cookie);
        }

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Antiforgery Validation Failed");
    }

    [Fact]
    public async Task AntiforgeryTokens_RefreshAcrossAuthenticationStateChanges_ReturnsDistinctTokens()
    {
        // Arrange
        var testEmail = $"refresh_csrf_{Guid.NewGuid():N}@campus.internal";
        var testPassword = "ValidPassword123!";
        await EnsureTestUserAsync(testEmail, testPassword);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        // Step 1: Initial anonymous token T0
        var (t0, c0) = await GetAntiforgeryTokensAsync(client);
        t0.Should().NotBeNullOrWhiteSpace();

        // Step 2: Login with T0
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(testEmail, testPassword))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", t0);
        if (!string.IsNullOrEmpty(c0)) loginReq.Headers.Add("Cookie", c0);

        var loginRes = await client.SendAsync(loginReq);
        loginRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginData = await loginRes.Content.ReadFromJsonAsync<LoginResponse>();
        var t1 = loginData!.AntiforgeryToken;

        // Assert T1 is non-empty and refreshed (not identical to anonymous T0)
        t1.Should().NotBeNullOrWhiteSpace();
        t1.Should().NotBe(t0);

        // Step 3: Logout using T1
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutReq.Headers.Add("X-XSRF-TOKEN", t1);

        var logoutRes = await client.SendAsync(logoutReq);
        logoutRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var logoutData = await logoutRes.Content.ReadFromJsonAsync<LogoutResponse>();
        var t2 = logoutData!.AntiforgeryToken;

        // Assert T2 is non-empty and refreshed for subsequent anonymous state
        t2.Should().NotBeNullOrWhiteSpace();
        t2.Should().NotBe(t1);
    }

    [Fact]
    public async Task Login_InvalidCredentials_Returns401WithUniformErrorMessage()
    {
        // Arrange: valid antiforgery token
        var (token, cookie) = await GetAntiforgeryTokensAsync(_client);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("nonexistent@campus.internal", "WrongPassword123!"))
        };
        requestMessage.Headers.Add("X-XSRF-TOKEN", token);
        if (!string.IsNullOrEmpty(cookie))
        {
            requestMessage.Headers.Add("Cookie", cookie);
        }

        // Act
        var response = await _client.SendAsync(requestMessage);

        // Assert: 401 Unauthorized with standard problem details
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Authentication Failed");
        problem.GetProperty("detail").GetString().Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task Login_SuccessfulCredentials_EstablishesSessionAndAllowsProtectedAccess()
    {
        // Arrange: provision test user
        var testEmail = $"success_{Guid.NewGuid():N}@campus.internal";
        var testPassword = "ValidPassword123!";
        await EnsureTestUserAsync(testEmail, testPassword);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var (token, cookie) = await GetAntiforgeryTokensAsync(client);

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(testEmail, testPassword))
        };
        loginRequest.Headers.Add("X-XSRF-TOKEN", token);
        if (!string.IsNullOrEmpty(cookie))
        {
            loginRequest.Headers.Add("Cookie", cookie);
        }

        // Act: Sign in
        var loginResponse = await client.SendAsync(loginRequest);

        // Assert login response
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        loginBody.Should().NotBeNull();
        loginBody!.Message.Should().Be("Sign-in successful");
        loginBody.User.Email.Should().Be(testEmail);
        loginBody.AntiforgeryToken.Should().NotBeNullOrWhiteSpace();

        // Verify no-cache headers
        loginResponse.Headers.CacheControl.Should().NotBeNull();
        loginResponse.Headers.CacheControl!.NoStore.Should().BeTrue();

        // Act: Access protected /api/auth/me with established session
        var meResponse = await client.GetAsync("/api/auth/me");

        // Assert protected access
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var meBody = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        meBody.Should().NotBeNull();
        meBody!.Email.Should().Be(testEmail);
        meBody.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task Logout_ClearsSessionAndInvalidatesProtectedAccess()
    {
        // Arrange: login
        var testEmail = $"logout_{Guid.NewGuid():N}@campus.internal";
        var testPassword = "ValidPassword123!";
        await EnsureTestUserAsync(testEmail, testPassword);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var (token, cookie) = await GetAntiforgeryTokensAsync(client);

        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(testEmail, testPassword))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", token);
        if (!string.IsNullOrEmpty(cookie)) loginReq.Headers.Add("Cookie", cookie);

        var loginRes = await client.SendAsync(loginReq);
        loginRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginData = await loginRes.Content.ReadFromJsonAsync<LoginResponse>();

        // Act: Logout using refreshed antiforgery token
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutReq.Headers.Add("X-XSRF-TOKEN", loginData!.AntiforgeryToken);
        var logoutRes = await client.SendAsync(logoutReq);

        // Assert: Logout succeeded
        logoutRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var logoutData = await logoutRes.Content.ReadFromJsonAsync<LogoutResponse>();
        logoutData!.Message.Should().Be("Sign-out successful");

        // Act: Subsequent protected access must be rejected with 401
        var meRes = await client.GetAsync("/api/auth/me");
        meRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Lockout_AfterFiveFailedAttempts_LocksAccountWithSameOutwardMessage()
    {
        // Arrange: unique test user
        var testEmail = $"lockout_{Guid.NewGuid():N}@campus.internal";
        var testPassword = "CorrectPassword123!";
        await EnsureTestUserAsync(testEmail, testPassword);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        // Act: Perform 5 failed attempts
        for (int i = 0; i < 5; i++)
        {
            var (token, cookie) = await GetAntiforgeryTokensAsync(client);
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest(testEmail, "WrongPassword!"))
            };
            req.Headers.Add("X-XSRF-TOKEN", token);
            if (!string.IsNullOrEmpty(cookie)) req.Headers.Add("Cookie", cookie);

            var res = await client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // 6th attempt: account is now locked out
        var (t6, c6) = await GetAntiforgeryTokensAsync(client);
        var req6 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(testEmail, "WrongPassword!"))
        };
        req6.Headers.Add("X-XSRF-TOKEN", t6);
        if (!string.IsNullOrEmpty(c6)) req6.Headers.Add("Cookie", c6);

        var res6 = await client.SendAsync(req6);

        // Assert: Returns the EXACT same outward 401 response without disclosing lockout status
        res6.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await res6.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().Be("Invalid email or password.");

        // Verify in database that lockout is active
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var lockedUser = await userManager.FindByEmailAsync(testEmail);
        (await userManager.IsLockedOutAsync(lockedUser!)).Should().BeTrue();
    }
}

public class SessionExpiryIntegrationTests : IClassFixture<ShortLivedSessionWebApplicationFactory>
{
    private readonly ShortLivedSessionWebApplicationFactory _factory;

    public SessionExpiryIntegrationTests(ShortLivedSessionWebApplicationFactory factory)
    {
        _factory = factory;
        TestDatabaseHelper.EnsureMigrated(factory.Services);
    }

    [Fact]
    public async Task SessionExpiry_AfterConfiguredSessionLifetime_Returns401Unauthorized()
    {
        // Arrange: provision test user
        var testEmail = $"expiry_{Guid.NewGuid():N}@campus.internal";
        var testPassword = "ValidPassword123!";

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = testEmail,
                Email = testEmail,
                EmailConfirmed = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            var createRes = await userManager.CreateAsync(user, testPassword);
            createRes.Succeeded.Should().BeTrue();
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        // Obtain CSRF token
        var csrfRes = await client.GetAsync("/api/auth/antiforgery");
        var csrfData = await csrfRes.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();

        // Login to establish session
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(testEmail, testPassword))
        };
        loginReq.Headers.Add("X-XSRF-TOKEN", csrfData!.RequestToken);

        var loginRes = await client.SendAsync(loginReq);
        loginRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Immediately verify session is active
        var activeRes = await client.GetAsync("/api/auth/me");
        activeRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act: Wait for session ticket to expire (lifetime configured to 2s)
        await Task.Delay(2500);

        // Assert: Subsequent call with expired ticket is rejected with 401 Unauthorized
        var expiredRes = await client.GetAsync("/api/auth/me");
        expiredRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

public class RateLimitingIntegrationTests : IClassFixture<ThrottledWebApplicationFactory>
{
    private readonly ThrottledWebApplicationFactory _factory;

    public RateLimitingIntegrationTests(ThrottledWebApplicationFactory factory)
    {
        _factory = factory;
        TestDatabaseHelper.EnsureMigrated(factory.Services);
    }

    [Fact]
    public async Task RateLimiting_ExcessiveRapidAttempts_Returns429TooManyRequests()
    {
        // Arrange: Factory configured with PermitLimit = 2
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        HttpStatusCode lastStatusCode = HttpStatusCode.OK;

        // Act: Send 4 requests rapidly (limit is 2)
        for (int i = 0; i < 4; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest("flood@campus.internal", "pw"))
            };
            var res = await client.SendAsync(req);
            lastStatusCode = res.StatusCode;
            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        // Assert: Throttled with 429
        lastStatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TrustedProxy_ForwardedHeaders_PartitionsRateLimitsByClientIp()
    {
        // Arrange: Factory configured with PermitLimit = 2
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var ipClientA = "198.51.100.10";
        var ipClientB = "198.51.100.20";

        // Act 1: Exhaust quota for Client A (2 permits)
        for (int i = 0; i < 2; i++)
        {
            var reqA = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest("a@campus.internal", "pw"))
            };
            reqA.Headers.Add("X-Forwarded-For", ipClientA);
            var resA = await client.SendAsync(reqA);
            resA.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        // 3rd attempt for Client A must be rate-limited (429)
        var reqA3 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("a@campus.internal", "pw"))
        };
        reqA3.Headers.Add("X-Forwarded-For", ipClientA);
        var resA3 = await client.SendAsync(reqA3);
        resA3.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // Act 2: Client B sends a request with distinct forwarded IP
        var reqB = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("b@campus.internal", "pw"))
        };
        reqB.Headers.Add("X-Forwarded-For", ipClientB);
        var resB = await client.SendAsync(reqB);

        // Assert: Client B is NOT throttled because proxy forwarded header correctly partitioned the rate limiter
        resB.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }
}

public class ProductionCookieSecurityTests : IClassFixture<ProductionHttpsWebApplicationFactory>
{
    private readonly ProductionHttpsWebApplicationFactory _factory;

    public ProductionCookieSecurityTests(ProductionHttpsWebApplicationFactory factory)
    {
        _factory = factory;
        TestDatabaseHelper.EnsureMigrated(factory.Services);
    }

    [Fact]
    public async Task ProductionCookies_HaveHttpOnly_Secure_And_SameSiteLaxAttributes()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        // Act: Retrieve antiforgery token via HTTPS
        var response = await client.GetAsync("/api/auth/antiforgery");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert: Check Set-Cookie attributes for Antiforgery cookie
        response.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeTrue();
        var cookieHeader = setCookies!.First(c => c.StartsWith("CampusVenue.Antiforgery"));

        cookieHeader.Should().Contain("httponly", Exactly.Once());
        cookieHeader.Should().Contain("secure", Exactly.Once());
        cookieHeader.Should().Contain("samesite=lax", Exactly.Once());
    }

    [Fact]
    public async Task TrustedProxy_ForwardedProtoHttps_AppliesHttpsCookiePolicy()
    {
        // Arrange: Reverse proxy forwarding HTTPS request over HTTP connection
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/antiforgery");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.50");

        // Act
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert: Under production environment with forwarded HTTPS proto, Secure attribute is enforced
        response.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeTrue();
        var cookieHeader = setCookies!.First(c => c.StartsWith("CampusVenue.Antiforgery"));
        cookieHeader.Should().Contain("secure", Exactly.Once());
    }
}
