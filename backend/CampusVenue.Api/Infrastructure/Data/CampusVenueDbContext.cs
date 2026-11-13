using Microsoft.EntityFrameworkCore;

namespace CampusVenue.Api.Infrastructure.Data;

/// <summary>
/// Primary Entity Framework Core database context for CampusVenue.
/// In Phase 0, establishes connection configuration and health verification.
/// Business entity sets (DbSets) will be registered in their respective phased feature milestones.
/// </summary>
public class CampusVenueDbContext : DbContext
{
    public CampusVenueDbContext(DbContextOptions<CampusVenueDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply entity configurations from all module assemblies as business features are added.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CampusVenueDbContext).Assembly);
    }
}
