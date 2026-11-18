using CampusVenue.Api.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace CampusVenue.Api.Modules.Identity.Services;

/// <summary>
/// Development-only utility to seed a local test user using credentials supplied locally.
/// Strictly disabled outside Development environment. No committed passwords or public endpoints.
/// </summary>
public static class DevUserSeeder
{
    public static async Task SeedDevUserAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        string[] args,
        IConfiguration configuration,
        ILogger logger)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        // Check environment variables or command-line parameters
        string? email = configuration["SEED_DEV_USER_EMAIL"];
        string? password = configuration["SEED_DEV_USER_PASSWORD"];

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--seed-dev-user-email" && i + 1 < args.Length)
            {
                email = args[i + 1];
            }
            else if (args[i] == "--seed-dev-user-password" && i + 1 < args.Length)
            {
                password = args[i + 1];
            }
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var existing = await userManager.FindByEmailAsync(email);
        if (existing == null)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                logger.LogInformation("Development test user successfully provisioned: {Email}", email);
            }
            else
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                logger.LogWarning("Failed to provision development test user: {Errors}", errors);
            }
        }
        else
        {
            // If the test user already exists, synchronize password and clear lockout so local development can proceed smoothly
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(existing);
            var resetResult = await userManager.ResetPasswordAsync(existing, resetToken, password);
            await userManager.SetLockoutEndDateAsync(existing, null);
            await userManager.ResetAccessFailedCountAsync(existing);

            if (resetResult.Succeeded)
            {
                logger.LogInformation("Development test user credentials refreshed and lockout reset: {Email}", email);
            }
            else
            {
                var errors = string.Join("; ", resetResult.Errors.Select(e => e.Description));
                logger.LogWarning("Failed to refresh development test user credentials: {Errors}", errors);
            }
        }
    }
}
