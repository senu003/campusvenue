using CampusVenue.Api.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace CampusVenue.Api.Modules.Identity.Services;

/// <summary>
/// Operator-only utility to grant UniversityAdmin role to an existing confirmed test account.
/// Operates strictly as an explicit one-time CLI command (--grant-admin-email) that executes and then exits.
/// Does not elevate accounts during regular API startup, contains no hardcoded administrator credentials,
/// and exposes no public web bootstrap endpoints.
/// </summary>
public static class OperatorRoleProvisioner
{
    /// <summary>
    /// Processes explicit operator CLI commands.
    /// Returns true if an operator command was executed and the process should immediately exit;
    /// false if normal application startup should continue.
    /// </summary>
    public static async Task<bool> ProcessOperatorCommandsAsync(
        IServiceProvider services,
        string[] args,
        ILogger logger)
    {
        string? grantEmail = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--grant-admin-email", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    grantEmail = args[i + 1];
                }
                else
                {
                    grantEmail = string.Empty;
                }
                break;
            }
        }

        if (grantEmail == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(grantEmail))
        {
            const string emptyMsg = "OPERATOR ERROR: --grant-admin-email was specified without a valid email address.";
            logger.LogError(emptyMsg);
            Console.Error.WriteLine(emptyMsg);
            Environment.ExitCode = 1;
            return true;
        }

        var result = await GrantUniversityAdminAsync(services, grantEmail.Trim());
        if (result.Succeeded)
        {
            logger.LogInformation(
                "OPERATOR AUDIT: UniversityAdmin role successfully granted to confirmed account: {Email}",
                grantEmail.Trim());
            Console.WriteLine($"[OPERATOR CLI] UniversityAdmin role successfully granted to '{grantEmail.Trim()}'. Exiting.");
            Environment.ExitCode = 0;
        }
        else
        {
            logger.LogError(
                "OPERATOR AUDIT: Failed to grant UniversityAdmin role to account: {Email}. Reason: {Reason}",
                grantEmail.Trim(),
                result.Error);
            Console.Error.WriteLine($"[OPERATOR CLI ERROR] Failed to grant UniversityAdmin role to '{grantEmail.Trim()}': {result.Error}");
            Environment.ExitCode = 1;
        }

        return true;
    }

    public static async Task<(bool Succeeded, string? Error)> GrantUniversityAdminAsync(
        IServiceProvider services,
        string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return (false, "Email address is required.");
        }

        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        // Ensure UniversityAdmin role exists
        if (!await roleManager.RoleExistsAsync(CampusRoles.UniversityAdmin))
        {
            var roleResult = await roleManager.CreateAsync(new ApplicationRole
            {
                Id = Guid.NewGuid(),
                Name = CampusRoles.UniversityAdmin,
                NormalizedName = CampusRoles.UniversityAdmin.ToUpperInvariant(),
                Description = CampusRoles.GetDescription(CampusRoles.UniversityAdmin),
                CreatedAtUtc = DateTime.UtcNow
            });

            if (!roleResult.Succeeded)
            {
                return (false, "Failed to initialize UniversityAdmin role.");
            }
        }

        // Locate target account
        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user == null)
        {
            return (false, $"Account '{email}' does not exist. Elevation requires an existing confirmed account.");
        }

        // Verify account is confirmed
        if (!await userManager.IsEmailConfirmedAsync(user))
        {
            return (false, $"Account '{email}' has not confirmed its email address. Elevation requires a confirmed account.");
        }

        // Check if already in role
        if (await userManager.IsInRoleAsync(user, CampusRoles.UniversityAdmin))
        {
            return (true, null);
        }

        // Grant UniversityAdmin role
        var addResult = await userManager.AddToRoleAsync(user, CampusRoles.UniversityAdmin);
        if (!addResult.Succeeded)
        {
            var errors = string.Join("; ", addResult.Errors.Select(e => e.Description));
            return (false, $"Failed to assign UniversityAdmin role: {errors}");
        }

        return (true, null);
    }
}
