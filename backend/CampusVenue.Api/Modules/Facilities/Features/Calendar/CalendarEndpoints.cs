using System.Security.Claims;
using CampusVenue.Api.Common;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Modules.Facilities.Domain;
using CampusVenue.Api.Modules.Identity.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CampusVenue.Api.Modules.Facilities.Features.Calendar;

public static class CalendarEndpoints
{
    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/venues/{venueId:guid}")
            .RequireAuthorization();

        // 1. List calendar coverages
        group.MapGet("/calendar-coverages", async (
            Guid venueId,
            CampusVenueDbContext dbContext) =>
        {
            var venueExists = await dbContext.Venues.AnyAsync(v => v.Id == venueId);
            if (!venueExists)
            {
                return Results.NotFound(new { message = $"Venue with ID '{venueId}' was not found." });
            }

            var coverages = await dbContext.VenueCalendarCoverages
                .Include(c => c.VerifiedByUser)
                .Where(c => c.VenueId == venueId)
                .OrderByDescending(c => c.CoversToUtc)
                .AsNoTracking()
                .ToListAsync();

            var dtos = coverages.Select(c => new VenueCalendarCoverageDto(
                c.Id,
                c.VenueId,
                c.SourceName,
                c.SourceReference,
                c.CoversFromUtc,
                c.CoversToUtc,
                c.VerifiedByUserId,
                c.VerifiedByUser.Email ?? string.Empty,
                c.VerifiedAtUtc,
                c.Notes,
                c.CreatedAtUtc
            )).ToList();

            return Results.Ok(dtos);
        })
        .WithName("ListVenueCalendarCoverages")
        .WithSummary("List verified calendar coverages for a venue")
        .Produces<List<VenueCalendarCoverageDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // 2. Add verified calendar coverage (scoped to assigned facility staff or admin with MFA)
        group.MapPost("/calendar-coverages", async (
            Guid venueId,
            CreateCoverageRequest request,
            CampusVenueDbContext dbContext,
            HttpContext httpContext,
            IAuthorizationService authorizationService) =>
        {
            var venue = await dbContext.Venues.FindAsync(venueId);
            if (venue == null)
            {
                return Results.NotFound(new { message = $"Venue with ID '{venueId}' was not found." });
            }

            if (!await CanManageVenueCalendarAsync(httpContext.User, venueId, dbContext, authorizationService))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "You are not authorized to maintain calendar verification coverage for this venue. Only assigned facility staff or university administrators can perform this action."
                );
            }

            if (string.IsNullOrWhiteSpace(request.SourceName))
                return Results.BadRequest(new { message = "Source name cannot be empty." });

            if (request.CoversToUtc <= request.CoversFromUtc)
                return Results.BadRequest(new { message = "CoversToUtc must be after CoversFromUtc." });

            var currentUserId = GetCurrentUserId(httpContext.User);
            if (currentUserId == null)
                return Results.Unauthorized();

            var coverage = new VenueCalendarCoverage
            {
                Id = Guid.NewGuid(),
                VenueId = venueId,
                SourceName = request.SourceName.Trim(),
                SourceReference = request.SourceReference?.Trim(),
                CoversFromUtc = DateTime.SpecifyKind(request.CoversFromUtc, DateTimeKind.Utc),
                CoversToUtc = DateTime.SpecifyKind(request.CoversToUtc, DateTimeKind.Utc),
                VerifiedByUserId = currentUserId.Value,
                VerifiedAtUtc = DateTime.UtcNow,
                Notes = request.Notes?.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            };

            dbContext.VenueCalendarCoverages.Add(coverage);
            await dbContext.SaveChangesAsync();

            var user = await dbContext.Users.FindAsync(currentUserId.Value);

            var dto = new VenueCalendarCoverageDto(
                coverage.Id,
                coverage.VenueId,
                coverage.SourceName,
                coverage.SourceReference,
                coverage.CoversFromUtc,
                coverage.CoversToUtc,
                coverage.VerifiedByUserId,
                user?.Email ?? string.Empty,
                coverage.VerifiedAtUtc,
                coverage.Notes,
                coverage.CreatedAtUtc
            );

            return Results.Created($"/api/venues/{venueId}/calendar-coverages/{coverage.Id}", dto);
        })
        .WithName("AddVenueCalendarCoverage")
        .WithSummary("Record verified calendar coverage period for a venue")
        .Produces<VenueCalendarCoverageDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // 3. List calendar blocks for a venue
        group.MapGet("/calendar-blocks", async (
            Guid venueId,
            DateTime? fromUtc,
            DateTime? toUtc,
            CampusVenueDbContext dbContext) =>
        {
            var venueExists = await dbContext.Venues.AnyAsync(v => v.Id == venueId);
            if (!venueExists)
            {
                return Results.NotFound(new { message = $"Venue with ID '{venueId}' was not found." });
            }

            var query = dbContext.VenueCalendarBlocks
                .Include(b => b.CreatedByUser)
                .Where(b => b.VenueId == venueId)
                .AsNoTracking();

            if (fromUtc.HasValue)
            {
                var from = DateTime.SpecifyKind(fromUtc.Value, DateTimeKind.Utc);
                query = query.Where(b => b.EndUtc >= from);
            }

            if (toUtc.HasValue)
            {
                var to = DateTime.SpecifyKind(toUtc.Value, DateTimeKind.Utc);
                query = query.Where(b => b.StartUtc <= to);
            }

            var blocks = await query
                .OrderBy(b => b.StartUtc)
                .ToListAsync();

            var dtos = blocks.Select(b => new VenueCalendarBlockDto(
                b.Id,
                b.VenueId,
                b.Title,
                b.BlockType,
                b.StartUtc,
                b.EndUtc,
                b.CreatedByUserId,
                b.CreatedByUser.Email ?? string.Empty,
                b.CreatedAtUtc
            )).ToList();

            return Results.Ok(dtos);
        })
        .WithName("ListVenueCalendarBlocks")
        .WithSummary("List calendar blocks (classes, exams, maintenance, outside bookings) for a venue")
        .Produces<List<VenueCalendarBlockDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // 4. Add calendar block (scoped to assigned facility staff or admin with MFA)
        group.MapPost("/calendar-blocks", async (
            Guid venueId,
            CreateBlockRequest request,
            CampusVenueDbContext dbContext,
            HttpContext httpContext,
            IAuthorizationService authorizationService) =>
        {
            var venue = await dbContext.Venues.FindAsync(venueId);
            if (venue == null)
            {
                return Results.NotFound(new { message = $"Venue with ID '{venueId}' was not found." });
            }

            if (!await CanManageVenueCalendarAsync(httpContext.User, venueId, dbContext, authorizationService))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "You are not authorized to maintain calendar blocks for this venue. Only assigned facility staff or university administrators can perform this action."
                );
            }

            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest(new { message = "Block title cannot be empty." });

            if (string.IsNullOrWhiteSpace(request.BlockType))
                return Results.BadRequest(new { message = "Block type cannot be empty." });

            if (request.EndUtc <= request.StartUtc)
                return Results.BadRequest(new { message = "EndUtc must be after StartUtc." });

            var currentUserId = GetCurrentUserId(httpContext.User);
            if (currentUserId == null)
                return Results.Unauthorized();

            // Lock venue row to ensure concurrent confirmations cannot run on stale calendar data
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM venues WHERE id = {venueId} FOR UPDATE");

            venue.UpdatedAtUtc = DateTime.UtcNow;

            var block = new VenueCalendarBlock
            {
                Id = Guid.NewGuid(),
                VenueId = venueId,
                Title = request.Title.Trim(),
                BlockType = request.BlockType.Trim(),
                StartUtc = DateTime.SpecifyKind(request.StartUtc, DateTimeKind.Utc),
                EndUtc = DateTime.SpecifyKind(request.EndUtc, DateTimeKind.Utc),
                CreatedByUserId = currentUserId.Value,
                CreatedAtUtc = DateTime.UtcNow
            };

            dbContext.VenueCalendarBlocks.Add(block);
            await dbContext.SaveChangesAsync();

            var user = await dbContext.Users.FindAsync(currentUserId.Value);

            var dto = new VenueCalendarBlockDto(
                block.Id,
                block.VenueId,
                block.Title,
                block.BlockType,
                block.StartUtc,
                block.EndUtc,
                block.CreatedByUserId,
                user?.Email ?? string.Empty,
                block.CreatedAtUtc
            );

            return Results.Created($"/api/venues/{venueId}/calendar-blocks/{block.Id}", dto);
        })
        .WithName("AddVenueCalendarBlock")
        .WithSummary("Add a calendar block for classes, exams, maintenance, or external bookings")
        .Produces<VenueCalendarBlockDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // 5. Delete calendar block (scoped to assigned facility staff or admin with MFA)
        group.MapDelete("/calendar-blocks/{blockId:guid}", async (
            Guid venueId,
            Guid blockId,
            CampusVenueDbContext dbContext,
            HttpContext httpContext,
            IAuthorizationService authorizationService) =>
        {
            var block = await dbContext.VenueCalendarBlocks
                .FirstOrDefaultAsync(b => b.Id == blockId && b.VenueId == venueId);

            if (block == null)
            {
                return Results.NotFound(new { message = "Calendar block not found." });
            }

            if (!await CanManageVenueCalendarAsync(httpContext.User, venueId, dbContext, authorizationService))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "You are not authorized to delete calendar blocks for this venue. Only assigned facility staff or university administrators can perform this action."
                );
            }

            // Lock venue row to ensure concurrent confirmations cannot run on stale calendar data
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM venues WHERE id = {venueId} FOR UPDATE");

            var venue = await dbContext.Venues.FindAsync(venueId);
            if (venue != null)
            {
                venue.UpdatedAtUtc = DateTime.UtcNow;
            }

            dbContext.VenueCalendarBlocks.Remove(block);
            await dbContext.SaveChangesAsync();

            return Results.NoContent();
        })
        .WithName("DeleteVenueCalendarBlock")
        .WithSummary("Delete a calendar block")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<bool> CanManageVenueCalendarAsync(
        ClaimsPrincipal user,
        Guid venueId,
        CampusVenueDbContext dbContext,
        IAuthorizationService authorizationService)
    {
        // 1. UniversityAdmin with completed MFA has university-wide governance (evaluated via standard policy)
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
                var isManager = await dbContext.VenueManagers
                    .AnyAsync(vm => vm.VenueId == venueId && vm.UserId == currentUserId.Value);

                if (isManager)
                {
                    return true;
                }
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
