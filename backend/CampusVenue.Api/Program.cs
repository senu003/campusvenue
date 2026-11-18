using System.Threading.RateLimiting;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Infrastructure.Email;
using CampusVenue.Api.Infrastructure.Health;
using CampusVenue.Api.Infrastructure.Middleware;
using CampusVenue.Api.Modules.Identity.Domain;
using CampusVenue.Api.Modules.Identity.Features.Auth;
using CampusVenue.Api.Modules.Identity.Features.ChangePassword;
using CampusVenue.Api.Modules.Identity.Features.PasswordRecovery;
using CampusVenue.Api.Modules.Identity.Features.SignOutEverywhere;
using CampusVenue.Api.Modules.Identity.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Structured Logging Configuration
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// 2. Configuration & Connection Strings
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=campusvenue_dev;Username=campusvenue_user;Password=campusvenue_dev_secret_change_me";

// 3. Database Context (PostgreSQL via Npgsql)
builder.Services.AddDbContext<CampusVenueDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.MigrationsAssembly(typeof(CampusVenueDbContext).Assembly.FullName);
        npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
    });

    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging(false);
        options.EnableDetailedErrors();
    }
});

// 4. Data Protection Key Ring Persistence
var dpKeysPath = builder.Configuration["DataProtection:KeyPath"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aspnet", "DataProtection-Keys");
Directory.CreateDirectory(dpKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath))
    .SetApplicationName("CampusVenue");

// 5. ASP.NET Core Identity & Cookie Authentication
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Tokens.PasswordResetTokenProvider = PasswordResetTokenProviderDefaults.ProviderName;
})
.AddEntityFrameworkStores<CampusVenueDbContext>()
.AddDefaultTokenProviders()
.AddTokenProvider<PasswordResetTokenProvider<ApplicationUser>>(PasswordResetTokenProviderDefaults.ProviderName);

builder.Services.Configure<PasswordResetTokenProviderOptions>(options =>
{
    // Dedicated 2-hour token lifespan scoped exclusively to password reset
    options.TokenLifespan = TimeSpan.FromHours(2);
});

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    // Re-verify session security stamp against database every 5 minutes to reject invalidated sessions
    options.ValidationInterval = TimeSpan.FromMinutes(5);
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "CampusVenue.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsProduction()
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false; // Finite session lifetime with no indefinite renewal
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization();

// 6. Antiforgery Configuration
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "CampusVenue.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsProduction()
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
});

// 7. Rate Limiter Configuration (Brute Force Defense & Proxy-Aware IP Limiting)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("AuthLimiter", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var limit = config.GetValue<int>("RateLimiting:AuthPermitLimit", 10);
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous_client";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: partitionKey,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

// 8. Forwarded Headers for Proxy Support
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// 9. Centralized Exception Handling & Problem Details (RFC 7807)
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// 10. Health Checks
builder.Services.AddCampusVenueHealthChecks(builder.Configuration);

// 11. CORS Policy
var allowedOrigins = builder.Configuration.GetValue<string>("AllowedCorsOrigins")
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? ["http://localhost:3000", "http://127.0.0.1:3000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("CampusVenueCorsPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// 12. OpenAPI / Swagger Documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CampusVenue API",
        Version = "v1",
        Description = "Enterprise University Venue Reservation & Event Management Platform API."
    });
});

// 13. Email Service (Local in-memory mailbox for development)
builder.Services.AddSingleton<DevMailboxService>();
builder.Services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<DevMailboxService>());

var app = builder.Build();

// Pipeline Configuration
app.UseForwardedHeaders();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CampusVenue API v1");
        c.RoutePrefix = "swagger";
    });
    app.MapDevMailboxEndpoints();
}

app.UseCors("CampusVenueCorsPolicy");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Seed dev user if explicitly supplied in Development
await DevUserSeeder.SeedDevUserAsync(app.Services, app.Environment, args, app.Configuration, app.Logger);

// Root information endpoint
app.MapGet("/", () => Results.Ok(new
{
    name = "CampusVenue API",
    version = "1.0.0-phase1a.1",
    status = "Active",
    documentation = "/swagger",
    health = "/health"
}))
.WithName("GetApiRoot")
.WithSummary("API Root Metadata")
.Produces(StatusCodes.Status200OK);

// Map Health Check Endpoints
app.MapCampusVenueHealthEndpoint();

// Map Module Endpoints
app.MapAuthEndpoints();
app.MapPasswordRecoveryEndpoints();
app.MapChangePasswordEndpoints();
app.MapSignOutEverywhereEndpoints();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
