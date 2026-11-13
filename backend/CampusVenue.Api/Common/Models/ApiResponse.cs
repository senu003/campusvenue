namespace CampusVenue.Api.Common.Models;

/// <summary>
/// Standard envelope for successful API responses.
/// </summary>
/// <typeparam name="T">Payload type</typeparam>
public sealed record ApiResponse<T>(
    bool Success,
    T? Data,
    string? Message = null,
    DateTimeOffset Timestamp = default)
{
    public DateTimeOffset Timestamp { get; init; } = Timestamp == default ? DateTimeOffset.UtcNow : Timestamp;

    public static ApiResponse<T> Ok(T data, string? message = null) =>
        new(true, data, message);

    public static ApiResponse<T> Fail(string message) =>
        new(false, default, message);
}
