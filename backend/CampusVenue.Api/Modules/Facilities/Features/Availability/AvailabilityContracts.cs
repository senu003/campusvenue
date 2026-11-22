namespace CampusVenue.Api.Modules.Facilities.Features.Availability;

public static class AvailabilityStatus
{
    public const string Available = "Available";
    public const string Unavailable = "Unavailable";
    public const string Unverified = "Availability unverified";
}

public record CheckAvailabilityRequest(
    DateTime EventStartUtc,
    DateTime EventEndUtc,
    int? SetupMinutes = null,
    int? CleanupMinutes = null
);

public record CheckAvailabilityResponse(
    Guid VenueId,
    string VenueName,
    string VenueCode,
    string Status,
    string Reason,
    bool IsAvailable,
    DateTime EventStartUtc,
    DateTime EventEndUtc,
    DateTime OccupiedStartUtc,
    DateTime OccupiedEndUtc,
    int SetupMinutes,
    int CleanupMinutes,
    ConflictDetailsDto? ConflictDetails,
    CoverageDetailsDto? CoverageDetails
);

public record ConflictDetailsDto(
    Guid BlockId,
    string Title,
    string BlockType,
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsBufferConflictOnly,
    string ConflictScope
);

public record CoverageDetailsDto(
    string SourceName,
    string? SourceReference,
    DateTime CoversFromUtc,
    DateTime CoversToUtc,
    string VerifiedBy,
    DateTime VerifiedAtUtc
);
