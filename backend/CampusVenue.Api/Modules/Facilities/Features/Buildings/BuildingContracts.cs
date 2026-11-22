namespace CampusVenue.Api.Modules.Facilities.Features.Buildings;

public record BuildingDto(
    Guid Id,
    string Name,
    string Code,
    int VenueCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
);

public record CreateBuildingRequest(
    string Name,
    string Code
);

public record UpdateBuildingRequest(
    string Name,
    string Code
);
