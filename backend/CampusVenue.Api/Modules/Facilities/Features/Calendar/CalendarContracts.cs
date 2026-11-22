namespace CampusVenue.Api.Modules.Facilities.Features.Calendar;

public record VenueCalendarCoverageDto(
    Guid Id,
    Guid VenueId,
    string SourceName,
    string? SourceReference,
    DateTime CoversFromUtc,
    DateTime CoversToUtc,
    Guid VerifiedByUserId,
    string VerifiedByUserEmail,
    DateTime VerifiedAtUtc,
    string? Notes,
    DateTime CreatedAtUtc
);

public record CreateCoverageRequest(
    string SourceName,
    string? SourceReference,
    DateTime CoversFromUtc,
    DateTime CoversToUtc,
    string? Notes
);

public record VenueCalendarBlockDto(
    Guid Id,
    Guid VenueId,
    string Title,
    string BlockType,
    DateTime StartUtc,
    DateTime EndUtc,
    Guid CreatedByUserId,
    string CreatedByUserEmail,
    DateTime CreatedAtUtc
);

public record CreateBlockRequest(
    string Title,
    string BlockType,
    DateTime StartUtc,
    DateTime EndUtc
);
