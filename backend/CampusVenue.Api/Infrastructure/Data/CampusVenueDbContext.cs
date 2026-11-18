using CampusVenue.Api.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CampusVenue.Api.Infrastructure.Data;

/// <summary>
/// Primary Entity Framework Core database context for CampusVenue.
/// Hosts Identity and cross-module relational persistence with snake_case naming conventions.
/// </summary>
public class CampusVenueDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public CampusVenueDbContext(DbContextOptions<CampusVenueDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Map ASP.NET Core Identity tables to snake_case plural names
        modelBuilder.Entity<ApplicationUser>(b =>
        {
            b.ToTable("users");
            b.Property(u => u.Id).HasColumnName("id");
            b.Property(u => u.UserName).HasColumnName("user_name").HasMaxLength(256);
            b.Property(u => u.NormalizedUserName).HasColumnName("normalized_user_name").HasMaxLength(256);
            b.Property(u => u.Email).HasColumnName("email").HasMaxLength(256);
            b.Property(u => u.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(256);
            b.Property(u => u.EmailConfirmed).HasColumnName("email_confirmed");
            b.Property(u => u.PasswordHash).HasColumnName("password_hash");
            b.Property(u => u.SecurityStamp).HasColumnName("security_stamp");
            b.Property(u => u.ConcurrencyStamp).HasColumnName("concurrency_stamp");
            b.Property(u => u.PhoneNumber).HasColumnName("phone_number");
            b.Property(u => u.PhoneNumberConfirmed).HasColumnName("phone_number_confirmed");
            b.Property(u => u.TwoFactorEnabled).HasColumnName("two_factor_enabled");
            b.Property(u => u.LockoutEnd).HasColumnName("lockout_end");
            b.Property(u => u.LockoutEnabled).HasColumnName("lockout_enabled");
            b.Property(u => u.AccessFailedCount).HasColumnName("access_failed_count");
            b.Property(u => u.CreatedAtUtc).HasColumnName("created_at_utc");
            b.Property(u => u.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<ApplicationRole>(b =>
        {
            b.ToTable("roles");
            b.Property(r => r.Id).HasColumnName("id");
            b.Property(r => r.Name).HasColumnName("name").HasMaxLength(256);
            b.Property(r => r.NormalizedName).HasColumnName("normalized_name").HasMaxLength(256);
            b.Property(r => r.ConcurrencyStamp).HasColumnName("concurrency_stamp");
            b.Property(r => r.Description).HasColumnName("description").HasMaxLength(500);
            b.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc");
        });

        modelBuilder.Entity<IdentityUserRole<Guid>>(b =>
        {
            b.ToTable("user_roles");
            b.Property(ur => ur.UserId).HasColumnName("user_id");
            b.Property(ur => ur.RoleId).HasColumnName("role_id");
        });

        modelBuilder.Entity<IdentityUserClaim<Guid>>(b =>
        {
            b.ToTable("user_claims");
            b.Property(uc => uc.Id).HasColumnName("id");
            b.Property(uc => uc.UserId).HasColumnName("user_id");
            b.Property(uc => uc.ClaimType).HasColumnName("claim_type");
            b.Property(uc => uc.ClaimValue).HasColumnName("claim_value");
        });

        modelBuilder.Entity<IdentityUserLogin<Guid>>(b =>
        {
            b.ToTable("user_logins");
            b.Property(ul => ul.LoginProvider).HasColumnName("login_provider");
            b.Property(ul => ul.ProviderKey).HasColumnName("provider_key");
            b.Property(ul => ul.ProviderDisplayName).HasColumnName("provider_display_name");
            b.Property(ul => ul.UserId).HasColumnName("user_id");
        });

        modelBuilder.Entity<IdentityRoleClaim<Guid>>(b =>
        {
            b.ToTable("role_claims");
            b.Property(rc => rc.Id).HasColumnName("id");
            b.Property(rc => rc.RoleId).HasColumnName("role_id");
            b.Property(rc => rc.ClaimType).HasColumnName("claim_type");
            b.Property(rc => rc.ClaimValue).HasColumnName("claim_value");
        });

        modelBuilder.Entity<IdentityUserToken<Guid>>(b =>
        {
            b.ToTable("user_tokens");
            b.Property(ut => ut.UserId).HasColumnName("user_id");
            b.Property(ut => ut.LoginProvider).HasColumnName("login_provider");
            b.Property(ut => ut.Name).HasColumnName("name");
            b.Property(ut => ut.Value).HasColumnName("value");
        });

        // Apply entity configurations from all module assemblies
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CampusVenueDbContext).Assembly);
    }
}
