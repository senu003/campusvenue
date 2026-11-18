using CampusVenue.Api.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CampusVenue.Api.Modules.Identity.Services;

/// <summary>
/// Ensures all institutional authorization roles exist in the PostgreSQL roles table.
/// Executed during application startup across all environments.
/// Concurrency-safe for multi-instance or parallel test host startup.
/// </summary>
public static class IdentityRoleSeeder
{
    public static async Task SeedRolesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("IdentityRoleSeeder");

        foreach (var roleName in CampusRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var role = new ApplicationRole
                {
                    Id = Guid.NewGuid(),
                    Name = roleName,
                    NormalizedName = roleName.ToUpperInvariant(),
                    Description = CampusRoles.GetDescription(roleName),
                    CreatedAtUtc = DateTime.UtcNow
                };

                try
                {
                    var result = await roleManager.CreateAsync(role);
                    if (result.Succeeded)
                    {
                        logger.LogInformation("Provisioned default institutional role: {RoleName}", roleName);
                    }
                    else
                    {
                        // Handle Identity-level duplicate role name error if returned without exception
                        if (result.Errors.Any(e => e.Code == "DuplicateRoleName") && await roleManager.RoleExistsAsync(roleName))
                        {
                            logger.LogInformation("Institutional role '{RoleName}' already provisioned concurrently.", roleName);
                            continue;
                        }

                        var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                        logger.LogWarning("Failed to provision institutional role {RoleName}: {Errors}", roleName, errors);
                    }
                }
                catch (DbUpdateException ex) when (IsUniqueRoleConstraintViolation(ex))
                {
                    // Concurrently provisioned by another host/process: verify role now exists
                    if (await roleManager.RoleExistsAsync(roleName))
                    {
                        logger.LogInformation("Institutional role '{RoleName}' was provisioned concurrently by another process.", roleName);
                        continue;
                    }

                    // If the unique constraint was not on this role or role does not exist, let it surface
                    throw;
                }
            }
        }
    }

    private const string RoleNameIndexConstraint = "RoleNameIndex";

    private static bool IsUniqueRoleConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pgEx
            && pgEx.SqlState == PostgresErrorCodes.UniqueViolation
            && string.Equals(pgEx.ConstraintName, RoleNameIndexConstraint, StringComparison.OrdinalIgnoreCase);
    }
}
