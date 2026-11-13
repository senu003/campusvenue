using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Infrastructure.Health;
using CampusVenue.Api.Infrastructure.Middleware;
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

// 4. Centralized Exception Handling & Problem Details (RFC 7807)
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// 5. Health Checks
builder.Services.AddCampusVenueHealthChecks(builder.Configuration);

// 6. CORS Policy
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

// 7. OpenAPI / Swagger Documentation
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

var app = builder.Build();

// Pipeline Configuration
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CampusVenue API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors("CampusVenueCorsPolicy");

// Root information endpoint
app.MapGet("/", () => Results.Ok(new
{
    name = "CampusVenue API",
    version = "1.0.0-phase0",
    status = "Active",
    documentation = "/swagger",
    health = "/health"
}))
.WithName("GetApiRoot")
.WithSummary("API Root Metadata")
.Produces(StatusCodes.Status200OK);

// Map Health Check Endpoints
app.MapCampusVenueHealthEndpoint();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
