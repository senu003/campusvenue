using CampusVenue.Api.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace CampusVenue.Api.Modules.Identity.Services;

/// <summary>
/// Ensures all institutional authorization roles exist in the PostgreSQL roles table.
/// Executed during application startup across all environments.
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

                var result = await roleManager.CreateAsync(role);
                if (result.Succeeded)
                {
                    logger.LogInformation("Provisioned default institutional role: {RoleName}", roleName);
                }
                else
                {
                    var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                    logger.LogWarning("Failed to provision institutional role {RoleName}: {Errors}", roleName, errors);
                }
            }
        }
    }
}
