using CampusVenue.Api.Common;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Modules.Facilities.Domain;
using Microsoft.EntityFrameworkCore;

namespace CampusVenue.Api.Modules.Facilities.Features.Buildings;

public static class BuildingEndpoints
{
    public static IEndpointRouteBuilder MapBuildingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/buildings")
            .RequireAuthorization();

        // 1. List all buildings with active venue counts
        group.MapGet("/", async (CampusVenueDbContext dbContext) =>
        {
            var buildings = await dbContext.Buildings
                .Include(b => b.Venues)
                .AsNoTracking()
                .OrderBy(b => b.Name)
                .ToListAsync();

            var dtos = buildings.Select(b => new BuildingDto(
                b.Id,
                b.Name,
                b.Code,
                b.Venues.Count(v => v.IsActive),
                b.CreatedAtUtc,
                b.UpdatedAtUtc
            )).ToList();

            return Results.Ok(dtos);
        })
        .WithName("ListBuildings")
        .WithSummary("List university physical buildings")
        .Produces<List<BuildingDto>>(StatusCodes.Status200OK);

        // 2. Get single building by Id
        group.MapGet("/{id:guid}", async (Guid id, CampusVenueDbContext dbContext) =>
        {
            var building = await dbContext.Buildings
                .Include(b => b.Venues)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id);

            if (building == null)
            {
                return Results.NotFound(new { message = $"Building with ID '{id}' was not found." });
            }

            var dto = new BuildingDto(
                building.Id,
                building.Name,
                building.Code,
                building.Venues.Count(v => v.IsActive),
                building.CreatedAtUtc,
                building.UpdatedAtUtc
            );

            return Results.Ok(dto);
        })
        .WithName("GetBuildingById")
        .WithSummary("Get detailed building information by Id")
        .Produces<BuildingDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // 3. Create building (UniversityAdmin with MFA)
        group.MapPost("/", async (
            CreateBuildingRequest request,
            CampusVenueDbContext dbContext) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { message = "Building name cannot be empty." });
            }

            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Results.BadRequest(new { message = "Building code cannot be empty." });
            }

            var normalizedCode = request.Code.Trim().ToUpperInvariant();

            var codeExists = await dbContext.Buildings
                .AnyAsync(b => b.Code == normalizedCode);

            if (codeExists)
            {
                return Results.Conflict(new { message = $"A building with code '{normalizedCode}' already exists." });
            }

            var building = new Building
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Code = normalizedCode,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            dbContext.Buildings.Add(building);
            await dbContext.SaveChangesAsync();

            var dto = new BuildingDto(
                building.Id,
                building.Name,
                building.Code,
                0,
                building.CreatedAtUtc,
                building.UpdatedAtUtc
            );

            return Results.Created($"/api/buildings/{building.Id}", dto);
        })
        .RequireAuthorization(AuthorizationPolicies.UniversityAdminWithMfa)
        .WithName("CreateBuilding")
        .WithSummary("Create a new university building")
        .Produces<BuildingDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);

        // 4. Update building (UniversityAdmin with MFA)
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateBuildingRequest request,
            CampusVenueDbContext dbContext) =>
        {
            var building = await dbContext.Buildings
                .Include(b => b.Venues)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (building == null)
            {
                return Results.NotFound(new { message = $"Building with ID '{id}' was not found." });
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { message = "Building name cannot be empty." });
            }

            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Results.BadRequest(new { message = "Building code cannot be empty." });
            }

            var normalizedCode = request.Code.Trim().ToUpperInvariant();

            var codeExists = await dbContext.Buildings
                .AnyAsync(b => b.Code == normalizedCode && b.Id != id);

            if (codeExists)
            {
                return Results.Conflict(new { message = $"A building with code '{normalizedCode}' already exists." });
            }

            building.Name = request.Name.Trim();
            building.Code = normalizedCode;
            building.UpdatedAtUtc = DateTime.UtcNow;

            await dbContext.SaveChangesAsync();

            var dto = new BuildingDto(
                building.Id,
                building.Name,
                building.Code,
                building.Venues.Count(v => v.IsActive),
                building.CreatedAtUtc,
                building.UpdatedAtUtc
            );

            return Results.Ok(dto);
        })
        .RequireAuthorization(AuthorizationPolicies.UniversityAdminWithMfa)
        .WithName("UpdateBuilding")
        .WithSummary("Update existing building name and code")
        .Produces<BuildingDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return app;
    }
}
