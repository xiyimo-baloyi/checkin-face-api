using Microsoft.Extensions.Configuration;
using Africanideasfaceapi.Services;

namespace Africanideasfaceapi.Middleware;

/// <summary>
/// Middleware for API key authentication
/// </summary>
public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly TelemetryService _telemetryService;
    private const string API_KEY_HEADER = "X-API-Key";

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration, TelemetryService telemetryService)
    {
        _next = next;
        _configuration = configuration;
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip API key check for Swagger and health endpoints
        var path = context.Request.Path.Value?.ToLower() ?? "";
        if (path.StartsWith("/swagger") || 
            path.StartsWith("/health") || 
            path.StartsWith("/_framework") ||
            path.Contains("/_blazor") ||
            path.Contains("/hubs/") ||
            path.StartsWith("/css") ||
            path.StartsWith("/js") ||
            path.StartsWith("/lib") ||
            path.StartsWith("/files") ||
            path.StartsWith("/api/leave/approve") ||
            path.StartsWith("/api/leave/reject") ||
            path.StartsWith("/api/whatsapp/webhook") ||
            !path.StartsWith("/api/"))
        {
            await _next(context);
            return;
        }

        // Check for API key in header
        if (!context.Request.Headers.TryGetValue(API_KEY_HEADER, out var extractedApiKey))
        {
            _telemetryService.TrackSecurityEvent("UnauthorizedApiAttempt", context.Request.Path, "API Key missing");
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("API Key was not provided.");
            return;
        }

        var apiKey = _configuration["ApiSettings:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            // If no API key configured, fail the request (Senior security recommendation)
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Server Configuration Error: API Key is missing.");
            return;
        }

        if (!apiKey.Equals(extractedApiKey))
        {
            _telemetryService.TrackSecurityEvent("UnauthorizedApiAttempt", context.Request.Path, "Invalid API Key provided");
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized. Invalid API Key.");
            return;
        }

        await _next(context);
    }
}
