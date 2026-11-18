namespace CampusVenue.Api.Modules.Identity.Features.Users;

/// <summary>
/// Data contracts for the read-only administrator user directory.
/// Exposes strictly the fields required by the administrative interface.
/// </summary>
public record UserDirectoryItemDto(
    Guid Id,
    string Email,
    IReadOnlyList<string> Roles,
    bool EmailConfirmed,
    bool TwoFactorEnabled,
    DateTime CreatedAtUtc
);

public record UserDirectoryResponse(
    IReadOnlyList<UserDirectoryItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages
);
