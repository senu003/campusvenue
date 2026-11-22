namespace CampusVenue.Api.Modules.Facilities.Domain;

/// <summary>
/// Represents a university physical building or facility complex.
/// </summary>
public class Building
{
    public Guid Id { get; set; }

    /// <summary>
    /// Human-readable building name (e.g., "Sumanadasa Building", "Civil Engineering Complex").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Unique normalized institutional code identifier (e.g., "SUM", "CIVIL-ENG").
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation collection
    public ICollection<Venue> Venues { get; set; } = new List<Venue>();
}
