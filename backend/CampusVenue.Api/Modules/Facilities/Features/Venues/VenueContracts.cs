namespace CampusVenue.Api.Modules.Facilities.Features.Venues;

public record VenueDto(
    Guid Id,
    string Name,
    string Code,
    string Type,
    string ManagingUnit,
    int Capacity,
    string OperatingHoursStart,
    string OperatingHoursEnd,
    int MinNoticeHours,
    int DefaultSetupMinutes,
    int DefaultCleanupMinutes,
    int MaxBookingHours,
    bool RequiresEventApproval,
    string? GeneralRules,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<VenueManagerDto> Managers
);

public record VenueManagerDto(
    Guid UserId,
    string Email,
    string? DisplayName,
    DateTime AssignedAtUtc
);

public record CreateVenueRequest(
    string Name,
    string Code,
    string Type,
    string ManagingUnit,
    int Capacity,
    string OperatingHoursStart,
    string OperatingHoursEnd,
    int MinNoticeHours,
    int DefaultSetupMinutes,
    int DefaultCleanupMinutes,
    int MaxBookingHours,
    bool RequiresEventApproval,
    string? GeneralRules = null
);

public record UpdateVenueRequest(
    string Name,
    string Type,
    string ManagingUnit,
    int Capacity,
    string OperatingHoursStart,
    string OperatingHoursEnd,
    int MinNoticeHours,
    int DefaultSetupMinutes,
    int DefaultCleanupMinutes,
    int MaxBookingHours,
    bool RequiresEventApproval,
    bool IsActive,
    string? GeneralRules = null
);

public record UpdateVenueRulesRequest(
    string OperatingHoursStart,
    string OperatingHoursEnd,
    int Capacity,
    int MinNoticeHours,
    int DefaultSetupMinutes,
    int DefaultCleanupMinutes,
    int MaxBookingHours,
    bool RequiresEventApproval,
    string? GeneralRules
);

public record AssignVenueManagerRequest(
    Guid UserId
);
