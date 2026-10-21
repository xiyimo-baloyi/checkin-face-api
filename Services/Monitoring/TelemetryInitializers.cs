using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Newtonsoft.Json;
using Africanideasfaceapi.Models;

namespace Africanideasfaceapi.Services.Monitoring;

/// <summary>
/// Telemetry initializer to add BranchId and UserId to every Application Insights telemetry item.
/// This allows for easy filtering in the Azure Portal.
/// </summary>
public class BranchTelemetryInitializer : ITelemetryInitializer
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const string UserSessionKey = "CurrentUser";

    public BranchTelemetryInitializer(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void Initialize(ITelemetry telemetry)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return;

        // Try to get UserId and BranchId from Session or Claims
        string? userId = null;
        string? branchId = null;

        try
        {
            // 1. Try Claims (for API requests)
            if (httpContext.User?.Identity?.IsAuthenticated == true)
            {
                userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                branchId = httpContext.User.FindFirst("BranchId")?.Value;
            }

            // 2. Try Session (for UI/Blazor requests)
            if (string.IsNullOrEmpty(userId) && httpContext.Session != null)
            {
                if (httpContext.Session.TryGetValue(UserSessionKey, out byte[]? userBytes))
                {
                    var userJson = System.Text.Encoding.UTF8.GetString(userBytes);
                    var user = JsonConvert.DeserializeObject<User>(userJson);
                    if (user != null)
                    {
                        userId = user.Id;
                        branchId = user.BranchId;
                    }
                }
            }
        }
        catch
        {
            // Fail silently to ensure telemetry is still captured
        }

        // Add custom properties to the telemetry item
        if (!string.IsNullOrEmpty(userId))
        {
            if (!telemetry.Context.GlobalProperties.ContainsKey("UserId"))
            {
                telemetry.Context.GlobalProperties.Add("UserId", userId);
            }
        }

        if (!string.IsNullOrEmpty(branchId))
        {
            if (!telemetry.Context.GlobalProperties.ContainsKey("BranchId"))
            {
                telemetry.Context.GlobalProperties.Add("BranchId", branchId);
            }
        }
    }
}
