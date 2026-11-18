using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CampusVenue.Api.Modules.Identity.Features.PasswordRecovery;

/// <summary>
/// Constants for the dedicated password reset token provider.
/// </summary>
public static class PasswordResetTokenProviderDefaults
{
    public const string ProviderName = "CampusVenuePasswordResetTokenProvider";
}

/// <summary>
/// Options for dedicated password reset token provider with a scoped 2-hour lifespan.
/// </summary>
public class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public PasswordResetTokenProviderOptions()
    {
        Name = PasswordResetTokenProviderDefaults.ProviderName;
        TokenLifespan = TimeSpan.FromHours(2);
    }
}

/// <summary>
/// Dedicated token provider for password reset tokens, isolating password reset expiration (2 hours)
/// from the default email confirmation token provider (24 hours).
/// </summary>
public class PasswordResetTokenProvider<TUser> : DataProtectorTokenProvider<TUser> where TUser : class
{
    public PasswordResetTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<PasswordResetTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<TUser>> logger)
        : base(dataProtectionProvider, options, logger)
    {
    }
}
