using System.Security.Claims;
using CampusVenue.Api.Common;
using CampusVenue.Api.Infrastructure.Data;
using CampusVenue.Api.Modules.Identity.Domain;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusVenue.Api.Modules.Identity.Features.Users;

/// <summary>
/// Vertical slice exposing institutional user directory and staff role assignment endpoints.
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
                    u.CreatedAtUtc,
                    u.ConcurrencyStamp
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
                    u.CreatedAtUtc,
                    u.ConcurrencyStamp
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

        group.MapPut("/{id:guid}/roles", async (
            Guid id,
            AssignUserRolesRequest request,
            CampusVenueDbContext dbContext,
            IAntiforgery antiforgery,
            HttpContext context,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken = default) =>
        {
            SetNoCacheHeaders(context);

            // 1. CSRF protection via antiforgery validation
            if (!await TryValidateAntiforgeryAsync(antiforgery, context))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Antiforgery Validation Failed",
                    detail: "The required CSRF header or cookie was missing or invalid.");
            }

            // 2. Derive acting administrator ID from session claims
            var actorIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(actorIdClaim) || !Guid.TryParse(actorIdClaim, out var actorId))
            {
                return Results.Unauthorized();
            }

            // 3. Input validation
            if (request == null || request.Roles == null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "The roles payload is required.");
            }

            // 4. Reject attempts to grant UniversityAdmin through the API
            if (request.Roles.Any(r => string.Equals(r, CampusRoles.UniversityAdmin, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Role Assignment Prohibited",
                    detail: "The UniversityAdmin role cannot be granted through the API. UniversityAdmin elevation is restricted exclusively to the operator CLI tool.");
            }

            // 5. Validate that all requested roles are valid assignable staff roles or Participant
            var allowedRoles = new HashSet<string>(CampusRoles.AssignableStaffRoles, StringComparer.OrdinalIgnoreCase)
            {
                CampusRoles.Participant
            };

            foreach (var role in request.Roles)
            {
                if (!allowedRoles.Contains(role))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid Role",
                        detail: $"Role '{role}' is not a valid institutional role for assignment.");
                }
            }

            // Canonical requested staff roles (Participant is implicit base access and not stored as an elevated role in user_roles)
            var targetStaffRoles = request.Roles
                .Where(r => !string.Equals(r, CampusRoles.Participant, StringComparison.OrdinalIgnoreCase))
                .Select(r => CampusRoles.AssignableStaffRoles.First(a => string.Equals(a, r, StringComparison.OrdinalIgnoreCase)))
                .Distinct()
                .OrderBy(r => r)
                .ToList();

            var effectiveDesiredRoles = targetStaffRoles.Count > 0
                ? (IReadOnlyList<string>)targetStaffRoles
                : [CampusRoles.Participant];

            var logger = loggerFactory.CreateLogger("UserRoleAssignment");

            // 6. Execute role changes, security-stamp update, and audit log in one PostgreSQL transaction
            var strategy = dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    var targetUser = await dbContext.Users
                        .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

                    if (targetUser == null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Results.Problem(
                            statusCode: StatusCodes.Status404NotFound,
                            title: "User Not Found",
                            detail: $"University user with ID '{id}' was not found.");
                    }

                    // Fetch current roles of target user
                    var currentRoleNames = await (
                        from ur in dbContext.UserRoles
                        join r in dbContext.Roles on ur.RoleId equals r.Id
                        where ur.UserId == targetUser.Id
                        select r.Name!
                    ).ToListAsync(cancellationToken);

                    // Disallow modifying existing UniversityAdmin users via this endpoint
                    if (currentRoleNames.Contains(CampusRoles.UniversityAdmin))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Results.Problem(
                            statusCode: StatusCodes.Status400BadRequest,
                            title: "Role Assignment Prohibited",
                            detail: "Roles for UniversityAdmin accounts cannot be modified through the staff role management API.");
                    }

                    var currentStaffRoles = currentRoleNames
                        .Where(r => CampusRoles.AssignableStaffRoles.Contains(r))
                        .OrderBy(r => r)
                        .ToList();

                    var effectiveCurrentRoles = currentStaffRoles.Count > 0
                        ? (IReadOnlyList<string>)currentStaffRoles
                        : [CampusRoles.Participant];

                    var isAlreadyStored = effectiveCurrentRoles.SequenceEqual(effectiveDesiredRoles);

                    // Duplicate submission / no-op check:
                    // "If a retry requests the role set already stored, return a successful no-op without creating a duplicate audit record."
                    if (isAlreadyStored)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Results.Ok(new AssignUserRolesResponse(
                            targetUser.Id,
                            effectiveCurrentRoles,
                            targetUser.ConcurrencyStamp ?? string.Empty,
                            "User roles are already up to date."));
                    }

                    // Stale edit / conflict detection
                    string? clientStamp = request.ConcurrencyStamp;
                    if (string.IsNullOrWhiteSpace(clientStamp) && context.Request.Headers.TryGetValue("If-Match", out var ifMatchHeader))
                    {
                        clientStamp = ifMatchHeader.ToString().Trim('\"', ' ');
                    }

                    // Enforce required concurrency precondition for role-changing requests
                    bool hasPrecondition = !string.IsNullOrWhiteSpace(clientStamp) || request.ExpectedRoles != null;
                    if (!hasPrecondition)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Results.Problem(
                            statusCode: StatusCodes.Status400BadRequest,
                            title: "Precondition Required",
                            detail: "A concurrency precondition (concurrencyStamp, expectedRoles, or If-Match header) is required when modifying user roles to prevent lost updates.");
                    }

                    bool isStale = false;
                    if (!string.IsNullOrWhiteSpace(clientStamp) &&
                        !string.Equals(targetUser.ConcurrencyStamp, clientStamp, StringComparison.Ordinal))
                    {
                        isStale = true;
                    }
                    else if (request.ExpectedRoles != null)
                    {
                        var normalizedExpected = request.ExpectedRoles
                            .Where(r => CampusRoles.AssignableStaffRoles.Contains(r))
                            .OrderBy(r => r)
                            .ToList();
                        if (!currentStaffRoles.SequenceEqual(normalizedExpected))
                        {
                            isStale = true;
                        }
                    }

                    if (isStale)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Results.Problem(
                            statusCode: StatusCodes.Status409Conflict,
                            title: "Conflicting Update",
                            detail: "The user's roles were modified by another administrator. Please reload the current user details and try again.");
                    }

                    // 1. Remove existing staff roles
                    var staffRoleEntities = await dbContext.Roles
                        .Where(r => CampusRoles.AssignableStaffRoles.Contains(r.Name!))
                        .ToListAsync(cancellationToken);

                    var staffRoleIds = staffRoleEntities.Select(r => r.Id).ToList();

                    var existingStaffUserRoles = await dbContext.UserRoles
                        .Where(ur => ur.UserId == targetUser.Id && staffRoleIds.Contains(ur.RoleId))
                        .ToListAsync(cancellationToken);

                    dbContext.UserRoles.RemoveRange(existingStaffUserRoles);

                    // 2. Add requested staff roles
                    foreach (var roleName in targetStaffRoles)
                    {
                        var roleEntity = staffRoleEntities.First(r => string.Equals(r.Name, roleName, StringComparison.OrdinalIgnoreCase));
                        dbContext.UserRoles.Add(new IdentityUserRole<Guid>
                        {
                            UserId = targetUser.Id,
                            RoleId = roleEntity.Id
                        });
                    }

                    // 3. Update target user's security stamp and concurrency stamp
                    targetUser.SecurityStamp = Guid.NewGuid().ToString("D");
                    targetUser.ConcurrencyStamp = Guid.NewGuid().ToString("D");
                    targetUser.UpdatedAtUtc = DateTime.UtcNow;

                    // 4. Persist audit record in the same transaction
                    var audit = new RoleAssignmentAudit
                    {
                        Id = Guid.NewGuid(),
                        ActorId = actorId,
                        TargetUserId = targetUser.Id,
                        PreviousRoles = [.. effectiveCurrentRoles],
                        NewRoles = [.. effectiveDesiredRoles],
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    dbContext.RoleAssignmentAudits.Add(audit);

                    // 5. Commit all modifications together
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    logger.LogInformation(
                        "Role assignment successfully updated for user {TargetUserId} by actor {ActorId}. Previous: [{Prev}], New: [{New}]",
                        targetUser.Id,
                        actorId,
                        string.Join(", ", effectiveCurrentRoles),
                        string.Join(", ", effectiveDesiredRoles));

                    return Results.Ok(new AssignUserRolesResponse(
                        targetUser.Id,
                        effectiveDesiredRoles,
                        targetUser.ConcurrencyStamp,
                        "User roles updated successfully."));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    logger.LogError(ex, "Transaction failed while updating roles for user {UserId}", id);
                    throw;
                }
            });
        })
        .WithName("AssignUserRoles")
        .WithSummary("Assign or remove staff roles for an eligible university user (UniversityAdmin with completed MFA session only)")
        .RequireAuthorization(AuthorizationPolicies.UniversityAdminWithMfa)
        .Produces<AssignUserRolesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static void SetNoCacheHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }

    private static async Task<bool> TryValidateAntiforgeryAsync(IAntiforgery antiforgery, HttpContext context)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
