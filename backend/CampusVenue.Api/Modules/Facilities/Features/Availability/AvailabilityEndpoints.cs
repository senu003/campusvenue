using CampusVenue.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CampusVenue.Api.Modules.Facilities.Features.Availability;

public static class AvailabilityEndpoints
{
    public static IEndpointRouteBuilder MapAvailabilityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/venues/{id:guid}/check-availability", async (
            Guid id,
            CheckAvailabilityRequest request,
            AvailabilityService availabilityService,
            CampusVenueDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var venue = await dbContext.Venues
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

            if (venue == null || !venue.IsActive)
            {
                return Results.NotFound(new { message = $"Venue with ID '{id}' was not found or is currently inactive." });
            }

            try
            {
                var result = await availabilityService.CheckAvailabilityAsync(
                    venue,
                    request,
                    dbContext,
                    cancellationToken
                );

                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .RequireAuthorization()
        .WithName("CheckVenueAvailability")
        .WithSummary("Check verified venue availability for a requested event window including setup and cleanup buffers")
        .Produces<CheckAvailabilityResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
