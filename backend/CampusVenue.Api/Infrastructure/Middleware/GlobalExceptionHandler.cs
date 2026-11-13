using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CampusVenue.Api.Infrastructure.Middleware;

/// <summary>
/// Centralized global exception handler mapping unhandled exceptions to standardized RFC 7807 Problem Details.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        _logger.LogError(
            exception,
            "Unhandled exception occurred. TraceId: {TraceId}. Message: {Message}",
            traceId,
            exception.Message);

        var (statusCode, title, type) = exception switch
        {
            ArgumentException => (
                StatusCodes.Status400BadRequest,
                "Invalid Argument",
                "https://campusvenue.internal/errors/invalid-argument"
            ),
            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                "Resource Not Found",
                "https://campusvenue.internal/errors/not-found"
            ),
            InvalidOperationException => (
                StatusCodes.Status409Conflict,
                "Conflict / Invalid Operation",
                "https://campusvenue.internal/errors/conflict"
            ),
            UnauthorizedAccessException => (
                StatusCodes.Status403Forbidden,
                "Forbidden Access",
                "https://campusvenue.internal/errors/forbidden"
            ),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "https://campusvenue.internal/errors/internal-server-error"
            )
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = type,
            Detail = _environment.IsDevelopment()
                ? exception.Message
                : "An unexpected error occurred while processing your request.",
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["traceId"] = traceId;

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
