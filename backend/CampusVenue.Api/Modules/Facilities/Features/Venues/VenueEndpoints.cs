using System.Security.Claims;
using CampusVenue.Api.Common;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Modules.Facilities.Domain;
using CampusVenue.Api.Modules.Identity.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CampusVenue.Api.Modules.Facilities.Features.Venues;

public static class VenueEndpoints
{
    public static IEndpointRouteBuilder MapVenueEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/venues")
            .RequireAuthorization();

        // 1. List all active venues (or all for admins)
        group.MapGet("/", async (CampusVenueDbContext dbContext, HttpContext httpContext) =>
        {
            var isAdmin = httpContext.User.IsInRole(CampusRoles.UniversityAdmin);
            var query = dbContext.Venues
                .Include(v => v.Building)
                .Include(v => v.Managers)
                    .ThenInclude(m => m.User)
                .AsNoTracking();

            if (!isAdmin)
            {
                query = query.Where(v => v.IsActive);
            }

            var venues = await query
                .OrderBy(v => v.Type)
                .ThenBy(v => v.Name)
                .ToListAsync();

            var dtos = venues.Select(MapToVenueDto).ToList();

            return Results.Ok(dtos);
        })
        .WithName("ListVenues")
        .WithSummary("List university venues in the catalog")
        .Produces<List<VenueDto>>(StatusCodes.Status200OK);

        // 2. Get single venue by Id
        group.MapGet("/{id:guid}", async (Guid id, CampusVenueDbContext dbContext) =>
        {
            var venue = await dbContext.Venues
                .Include(v => v.Building)
                .Include(v => v.Managers)
                    .ThenInclude(m => m.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == id);

            if (venue == null)
            {
                return Results.NotFound(new { message = $"Venue with ID '{id}' was not found." });
            }

            var dto = MapToVenueDto(venue);

            return Results.Ok(dto);
        })
        .WithName("GetVenueById")
        .WithSummary("Get detailed venue information by Id")
        .Produces<VenueDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // 3. Create venue (UniversityAdmin with MFA)
        group.MapPost("/", async (
            CreateVenueRequest request,
            CampusVenueDbContext dbContext) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { message = "Venue name cannot be empty." });

            if (string.IsNullOrWhiteSpace(request.Code))
                return Results.BadRequest(new { message = "Venue code cannot be empty." });

            if (request.Capacity <= 0)
                return Results.BadRequest(new { message = "Capacity must be greater than zero." });

            if (!TimeOnly.TryParse(request.OperatingHoursStart, out var opStart) ||
                !TimeOnly.TryParse(request.OperatingHoursEnd, out var opEnd))
            {
                return Results.BadRequest(new { message = "Operating hours must be valid times (HH:mm)." });
            }

            if (opEnd <= opStart)
                return Results.BadRequest(new { message = "Operating hours end time must be after start time." });

            var existingCode = await dbContext.Venues
                .AnyAsync(v => v.Code.ToUpper() == request.Code.Trim().ToUpper());

            if (existingCode)
                return Results.Conflict(new { message = $"A venue with code '{request.Code}' already exists." });

            if (request.BuildingId.HasValue)
            {
                var buildingExists = await dbContext.Buildings.AnyAsync(b => b.Id == request.BuildingId.Value);
                if (!buildingExists)
                {
                    return Results.BadRequest(new { message = $"Building with ID '{request.BuildingId.Value}' was not found." });
                }
            }

            var venue = new Venue
            {
                Id = Guid.NewGuid(),
                BuildingId = request.BuildingId,
                Name = request.Name.Trim(),
                Code = request.Code.Trim().ToUpperInvariant(),
                Type = request.Type.Trim(),
                ManagingUnit = request.ManagingUnit.Trim(),
                Capacity = request.Capacity,
                OperatingHoursStart = opStart,
                OperatingHoursEnd = opEnd,
                MinNoticeHours = Math.Max(0, request.MinNoticeHours),
                DefaultSetupMinutes = Math.Max(0, request.DefaultSetupMinutes),
                DefaultCleanupMinutes = Math.Max(0, request.DefaultCleanupMinutes),
                MaxBookingHours = Math.Max(1, request.MaxBookingHours),
                RequiresEventApproval = request.RequiresEventApproval,
                GeneralRules = request.GeneralRules?.Trim(),
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            dbContext.Venues.Add(venue);
            await dbContext.SaveChangesAsync();

            // Load building navigation for response DTO if assigned
            if (venue.BuildingId.HasValue)
            {
                await dbContext.Entry(venue).Reference(v => v.Building).LoadAsync();
            }

            var dto = MapToVenueDto(venue);

            return Results.Created($"/api/venues/{venue.Id}", dto);
        })
        .RequireAuthorization(AuthorizationPolicies.UniversityAdminWithMfa)
        .WithName("CreateVenue")
        .WithSummary("Create a new university venue")
        .Produces<VenueDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);

        // 4. Update venue (UniversityAdmin with MFA)
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateVenueRequest request,
            CampusVenueDbContext dbContext) =>
        {
            var venue = await dbContext.Venues
                .Include(v => v.Building)
                .Include(v => v.Managers)
                    .ThenInclude(m => m.User)
                .FirstOrDefaultAsync(v => v.Id == id);

            if (venue == null)
            {
                return Results.NotFound(new { message = $"Venue with ID '{id}' was not found." });
            }

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { message = "Venue name cannot be empty." });

            if (request.Capacity <= 0)
                return Results.BadRequest(new { message = "Capacity must be greater than zero." });

            if (!TimeOnly.TryParse(request.OperatingHoursStart, out var opStart) ||
                !TimeOnly.TryParse(request.OperatingHoursEnd, out var opEnd))
            {
                return Results.BadRequest(new { message = "Operating hours must be valid times (HH:mm)." });
            }

            if (opEnd <= opStart)
                return Results.BadRequest(new { message = "Operating hours end time must be after start time." });

            if (request.BuildingId.HasValue)
            {
                var building = await dbContext.Buildings.FindAsync(request.BuildingId.Value);
                if (building == null)
                {
                    return Results.BadRequest(new { message = $"Building with ID '{request.BuildingId.Value}' was not found." });
                }

                venue.BuildingId = building.Id;
                venue.Building = building;
            }

            venue.Name = request.Name.Trim();
            venue.Type = request.Type.Trim();
            venue.ManagingUnit = request.ManagingUnit.Trim();
            venue.Capacity = request.Capacity;
            venue.OperatingHoursStart = opStart;
            venue.OperatingHoursEnd = opEnd;
            venue.MinNoticeHours = Math.Max(0, request.MinNoticeHours);
            venue.DefaultSetupMinutes = Math.Max(0, request.DefaultSetupMinutes);
            venue.DefaultCleanupMinutes = Math.Max(0, request.DefaultCleanupMinutes);
            venue.MaxBookingHours = Math.Max(1, request.MaxBookingHours);
            venue.RequiresEventApproval = request.RequiresEventApproval;
            venue.GeneralRules = request.GeneralRules?.Trim();
            venue.IsActive = request.IsActive;
            venue.UpdatedAtUtc = DateTime.UtcNow;

            await dbContext.SaveChangesAsync();

            var dto = MapToVenueDto(venue);

            return Results.Ok(dto);
        })
        .RequireAuthorization(AuthorizationPolicies.UniversityAdminWithMfa)
        .WithName("UpdateVenue")
        .WithSummary("Update existing venue details and rules")
        .Produces<VenueDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest);

        // 5. Update venue rules (UniversityAdmin with MFA OR assigned FacilityAdmin)
        group.MapPut("/{id:guid}/rules", async (
            Guid id,
            UpdateVenueRulesRequest request,
            CampusVenueDbContext dbContext,
            HttpContext httpContext,
            IAuthorizationService authorizationService) =>
        {
            var venue = await dbContext.Venues
                .Include(v => v.Managers)
                    .ThenInclude(m => m.User)
                .FirstOrDefaultAsync(v => v.Id == id);

            if (venue == null)
            {
                return Results.NotFound(new { message = $"Venue with ID '{id}' was not found." });
            }

            if (!await CanManageVenueRulesAsync(httpContext.User, id, dbContext, authorizationService))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "You are not authorized to manage venue rules for this venue. Only assigned facility staff or university administrators with completed MFA can perform this action."
                );
            }

            if (request.Capacity <= 0)
                return Results.BadRequest(new { message = "Capacity must be greater than zero." });

            if (!TimeOnly.TryParse(request.OperatingHoursStart, out var opStart) ||
                !TimeOnly.TryParse(request.OperatingHoursEnd, out var opEnd))
            {
                return Results.BadRequest(new { message = "Operating hours must be valid times (HH:mm)." });
            }

            if (opEnd <= opStart)
                return Results.BadRequest(new { message = "Operating hours end time must be after start time." });

            venue.Capacity = request.Capacity;
            venue.OperatingHoursStart = opStart;
            venue.OperatingHoursEnd = opEnd;
            venue.MinNoticeHours = Math.Max(0, request.MinNoticeHours);
            venue.DefaultSetupMinutes = Math.Max(0, request.DefaultSetupMinutes);
            venue.DefaultCleanupMinutes = Math.Max(0, request.DefaultCleanupMinutes);
            venue.MaxBookingHours = Math.Max(1, request.MaxBookingHours);
            venue.RequiresEventApproval = request.RequiresEventApproval;
            venue.GeneralRules = request.GeneralRules?.Trim();
            venue.UpdatedAtUtc = DateTime.UtcNow;

            await dbContext.SaveChangesAsync();

            var dto = MapToVenueDto(venue);

            return Results.Ok(dto);
        })
        .WithName("UpdateVenueRules")
        .WithSummary("Update operational rules, hours, buffers, and general policies for a venue")
        .Produces<VenueDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // 6. Assign manager to venue (UniversityAdmin with MFA)
        group.MapPost("/{id:guid}/managers", async (
            Guid id,
            AssignVenueManagerRequest request,
            CampusVenueDbContext dbContext,
            UserManager<ApplicationUser> userManager) =>
        {
            var venue = await dbContext.Venues.FindAsync(id);
            if (venue == null)
            {
                return Results.NotFound(new { message = $"Venue with ID '{id}' was not found." });
            }

            var user = await userManager.FindByIdAsync(request.UserId.ToString());
            if (user == null)
            {
                return Results.NotFound(new { message = $"User with ID '{request.UserId}' was not found." });
            }

            var isFacilityAdmin = await userManager.IsInRoleAsync(user, CampusRoles.FacilityAdmin);
            var isUniAdmin = await userManager.IsInRoleAsync(user, CampusRoles.UniversityAdmin);

            if (!isFacilityAdmin && !isUniAdmin)
            {
                return Results.BadRequest(new
                {
                    message = $"Only users with '{CampusRoles.FacilityAdmin}' or '{CampusRoles.UniversityAdmin}' role can be assigned as venue managers."
                });
            }

            var alreadyAssigned = await dbContext.VenueManagers
                .AnyAsync(vm => vm.VenueId == id && vm.UserId == request.UserId);

            if (!alreadyAssigned)
            {
                dbContext.VenueManagers.Add(new VenueManager
                {
                    VenueId = id,
                    UserId = request.UserId,
                    AssignedAtUtc = DateTime.UtcNow
                });

                await dbContext.SaveChangesAsync();
            }

            return Results.Ok(new { message = $"User '{user.Email}' assigned to manage venue '{venue.Name}'." });
        })
        .RequireAuthorization(AuthorizationPolicies.UniversityAdminWithMfa)
        .WithName("AssignVenueManager")
        .WithSummary("Assign a facility staff manager to a venue")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // 7. Remove manager from venue (UniversityAdmin with MFA)
        group.MapDelete("/{id:guid}/managers/{userId:guid}", async (
            Guid id,
            Guid userId,
            CampusVenueDbContext dbContext) =>
        {
            var assignment = await dbContext.VenueManagers
                .FirstOrDefaultAsync(vm => vm.VenueId == id && vm.UserId == userId);

            if (assignment == null)
            {
                return Results.NotFound(new { message = "Venue manager assignment not found." });
            }

            dbContext.VenueManagers.Remove(assignment);
            await dbContext.SaveChangesAsync();

            return Results.NoContent();
        })
        .RequireAuthorization(AuthorizationPolicies.UniversityAdminWithMfa)
        .WithName("RemoveVenueManager")
        .WithSummary("Remove a facility staff manager from a venue")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

        // 8. Assign or reassign venue to a physical building (UniversityAdmin with MFA)
        group.MapPut("/{id:guid}/building", async (
            Guid id,
            AssignVenueBuildingRequest request,
            CampusVenueDbContext dbContext) =>
        {
            var venue = await dbContext.Venues
                .Include(v => v.Building)
                .Include(v => v.Managers)
                    .ThenInclude(m => m.User)
                .FirstOrDefaultAsync(v => v.Id == id);

            if (venue == null)
            {
                return Results.NotFound(new { message = $"Venue with ID '{id}' was not found." });
            }

            if (request.BuildingId.HasValue)
            {
                var building = await dbContext.Buildings.FindAsync(request.BuildingId.Value);
                if (building == null)
                {
                    return Results.BadRequest(new { message = $"Building with ID '{request.BuildingId.Value}' was not found." });
                }

                venue.BuildingId = building.Id;
                venue.Building = building;
            }
            else
            {
                venue.BuildingId = null;
                venue.Building = null;
            }

            venue.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();

            var dto = MapToVenueDto(venue);

            return Results.Ok(dto);
        })
        .RequireAuthorization(AuthorizationPolicies.UniversityAdminWithMfa)
        .WithName("AssignVenueBuilding")
        .WithSummary("Assign, reassign, or unassign a venue to a physical building")
        .Produces<VenueDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static VenueDto MapToVenueDto(Venue venue)
    {
        return new VenueDto(
            venue.Id,
            venue.Name,
            venue.Code,
            venue.Type,
            venue.ManagingUnit,
            venue.Capacity,
            venue.OperatingHoursStart.ToString("HH:mm"),
            venue.OperatingHoursEnd.ToString("HH:mm"),
            venue.MinNoticeHours,
            venue.DefaultSetupMinutes,
            venue.DefaultCleanupMinutes,
            venue.MaxBookingHours,
            venue.RequiresEventApproval,
            venue.GeneralRules,
            venue.IsActive,
            venue.CreatedAtUtc,
            venue.UpdatedAtUtc,
            venue.Managers?.Select(m => new VenueManagerDto(
                m.UserId,
                m.User?.Email ?? string.Empty,
                m.User?.UserName,
                m.AssignedAtUtc
            )).ToList() ?? [],
            venue.BuildingId,
            venue.Building?.Name,
            venue.Building?.Code
        );
    }

    private static async Task<bool> CanManageVenueRulesAsync(
        ClaimsPrincipal user,
        Guid venueId,
        CampusVenueDbContext dbContext,
        IAuthorizationService authorizationService)
    {
        // 1. UniversityAdmin with completed MFA has university-wide authority
        var authResult = await authorizationService.AuthorizeAsync(user, AuthorizationPolicies.UniversityAdminWithMfa);
        if (authResult.Succeeded)
        {
            return true;
        }

        // 2. FacilityAdmin must be assigned to this specific venue
        if (user.IsInRole(CampusRoles.FacilityAdmin))
        {
            var currentUserId = GetCurrentUserId(user);
            if (currentUserId.HasValue)
            {
                return await dbContext.VenueManagers
                    .AnyAsync(vm => vm.VenueId == venueId && vm.UserId == currentUserId.Value);
            }
        }

        return false;
    }

    private static Guid? GetCurrentUserId(ClaimsPrincipal user)
    {
        var idStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idStr, out var id) ? id : null;
    }
}
