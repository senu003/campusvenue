namespace CampusVenue.Api.Modules.Identity.Features.Users;

/// <summary>
/// Data contracts for the administrator user directory and staff role assignment.
/// Exposes strictly the fields required by the administrative interface.
/// </summary>
public record UserDirectoryItemDto(
    Guid Id,
    string Email,
    IReadOnlyList<string> Roles,
    bool EmailConfirmed,
    bool TwoFactorEnabled,
    DateTime CreatedAtUtc,
    string? ConcurrencyStamp = null
);

public record UserDirectoryResponse(
    IReadOnlyList<UserDirectoryItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages
);

/// <summary>
/// Request contract for granting or removing staff roles.
/// Supports ConcurrencyStamp or ExpectedRoles to detect stale edits.
/// </summary>
public record AssignUserRolesRequest(
    IReadOnlyList<string> Roles,
    string? ConcurrencyStamp = null,
    IReadOnlyList<string>? ExpectedRoles = null
);

/// <summary>
/// Response contract returned upon successful or no-op role assignment.
/// </summary>
public record AssignUserRolesResponse(
    Guid UserId,
    IReadOnlyList<string> Roles,
    string ConcurrencyStamp,
    string Message
);
