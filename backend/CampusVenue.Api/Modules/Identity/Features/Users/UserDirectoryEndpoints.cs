using CampusVenue.Api.Common;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Modules.Identity.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusVenue.Api.Modules.Identity.Features.Users;

/// <summary>
/// Vertical slice exposing the read-only, paginated institutional user directory.
/// Protected by the UniversityAdminWithMfa policy requiring administrator privileges and a completed MFA session.
/// </summary>
public static class UserDirectoryEndpoints
{
    public static IEndpointRouteBuilder MapUserDirectoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/users")
            .WithTags("Users");

        group.MapGet("/", async (
            CampusVenueDbContext dbContext,
            HttpContext context,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null,
            CancellationToken cancellationToken = default) =>
        {
            SetNoCacheHeaders(context);

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 50) pageSize = 50;

            var query = dbContext.Users.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var trimmed = search.Trim();
                query = query.Where(u => u.Email != null && EF.Functions.ILike(u.Email, $"%{trimmed}%"));
            }

            var totalCount = await query.CountAsync(cancellationToken);

            var users = await query
                .OrderBy(u => u.Email)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new
                {
                    u.Id,
                    Email = u.Email ?? string.Empty,
                    u.EmailConfirmed,
                    u.TwoFactorEnabled,
                    u.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);

            var userIds = users.Select(u => u.Id).ToList();

            var userRoles = await (
                from ur in dbContext.UserRoles.AsNoTracking()
                join r in dbContext.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where userIds.Contains(ur.UserId)
                select new { ur.UserId, RoleName = r.Name }
            ).ToListAsync(cancellationToken);

            var rolesByUserId = userRoles
                .GroupBy(ur => ur.UserId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.RoleName!).Where(r => r != null).ToList());

            var items = users.Select(u =>
            {
                var roles = rolesByUserId.TryGetValue(u.Id, out var rList) && rList.Count > 0
                    ? (IReadOnlyList<string>)rList
                    : [CampusRoles.Participant];

                return new UserDirectoryItemDto(
                    u.Id,
                    u.Email,
                    roles,
                    u.EmailConfirmed,
                    u.TwoFactorEnabled,
                    u.CreatedAtUtc
                );
            }).ToList();

            var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);

            return Results.Ok(new UserDirectoryResponse(
                items,
                page,
                pageSize,
                totalCount,
                totalPages
            ));
        })
        .WithName("GetUserDirectory")
        .WithSummary("Retrieve paginated list of university users (UniversityAdmin with completed MFA session only)")
        .RequireAuthorization(AuthorizationPolicies.UniversityAdminWithMfa)
        .Produces<UserDirectoryResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static void SetNoCacheHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }
}
