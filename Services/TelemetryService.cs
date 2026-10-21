using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Africanideasfaceapi.Services;

public class TelemetryService
{
    private readonly TelemetryClient? _telemetryClient;
    private readonly ILogger<TelemetryService> _logger;

    public TelemetryService(TelemetryClient? telemetryClient, ILogger<TelemetryService> logger)
    {
        _telemetryClient = telemetryClient;
        _logger = logger;
    }

    public void TrackFaceRecognition(string operation, string userId, bool success, double? confidence = null, TimeSpan? duration = null)
    {
        var properties = new Dictionary<string, string>
        {
            { "Operation", operation },
            { "UserId", userId },
            { "Success", success.ToString() }
        };

        if (confidence.HasValue)
        {
            properties.Add("Confidence", confidence.Value.ToString("F2"));
        }

        var metrics = new Dictionary<string, double>();
        if (duration.HasValue)
        {
            metrics.Add("Duration", duration.Value.TotalMilliseconds);
        }

        if (_telemetryClient != null)
        {
            _telemetryClient.TrackEvent("FaceRecognition", properties, metrics);
        }
        _logger.LogInformation("Face recognition tracked: {Operation}, UserId: {UserId}, Success: {Success}", 
            operation, userId, success);
    }

    public void TrackUserRegistration(string userId, string registrationMethod)
    {
        var properties = new Dictionary<string, string>
        {
            { "UserId", userId },
            { "RegistrationMethod", registrationMethod }
        };

        if (_telemetryClient != null)
        {
            _telemetryClient.TrackEvent("UserRegistration", properties);
        }
        _logger.LogInformation("User registration tracked: UserId: {UserId}, Method: {Method}", 
            userId, registrationMethod);
    }

    public void TrackLoginAttempt(string userId, bool success, string? reason = null)
    {
        var properties = new Dictionary<string, string>
        {
            { "UserId", userId ?? "unknown" },
            { "Success", success.ToString() }
        };

        if (!string.IsNullOrEmpty(reason))
        {
            properties.Add("Reason", reason);
        }

        if (_telemetryClient != null)
        {
            _telemetryClient.TrackEvent("LoginAttempt", properties);
        }
        _logger.LogInformation("Login attempt tracked: UserId: {UserId}, Success: {Success}, Reason: {Reason}", 
            userId, success, reason);
    }

    public void TrackAdminAction(string adminUserId, string action, string targetUserId)
    {
        var properties = new Dictionary<string, string>
        {
            { "AdminUserId", adminUserId },
            { "Action", action },
            { "TargetUserId", targetUserId }
        };

        if (_telemetryClient != null)
        {
            _telemetryClient.TrackEvent("AdminAction", properties);
        }
        _logger.LogInformation("Admin action tracked: Admin: {AdminUserId}, Action: {Action}, Target: {TargetUserId}", 
            adminUserId, action, targetUserId);
    }

    public void TrackException(Exception exception, Dictionary<string, string>? properties = null)
    {
        if (_telemetryClient != null)
        {
            _telemetryClient.TrackException(exception, properties);
        }
        _logger.LogError(exception, "Exception tracked with properties: {Properties}", properties);
    }

    public void TrackSecurityEvent(string eventName, string resource, string? details = null)
    {
        var properties = new Dictionary<string, string>
        {
            { "Resource", resource }
        };

        if (!string.IsNullOrEmpty(details))
        {
            properties.Add("Details", details);
        }

        if (_telemetryClient != null)
        {
            _telemetryClient.TrackEvent(eventName, properties);
        }
        _logger.LogWarning("Security event tracked: {EventName}, Resource: {Resource}, Details: {Details}", 
            eventName, resource, details);
    }

    public void TrackDependency(string dependencyTypeName, string dependencyName, string data, DateTimeOffset startTime, TimeSpan duration, bool success)
    {
        if (_telemetryClient != null)
        {
            _telemetryClient.TrackDependency(dependencyTypeName, dependencyName, data, startTime, duration, success);
        }
    }

    public void TrackFaceLoginStage(string stage, bool success, int frameCount, long payloadBytes, long durationMs, string? userId = null, string? error = null)
    {
        var properties = new Dictionary<string, string>
        {
            { "Stage", stage },
            { "Success", success.ToString() },
            { "FrameCount", frameCount.ToString() },
            { "UserId", string.IsNullOrWhiteSpace(userId) ? "unknown" : userId }
        };

        if (!string.IsNullOrWhiteSpace(error))
        {
            properties["Error"] = error;
        }

        var metrics = new Dictionary<string, double>
        {
            { "PayloadBytes", payloadBytes },
            { "DurationMs", durationMs }
        };

        if (_telemetryClient != null)
        {
            _telemetryClient.TrackEvent("FaceLoginStage", properties, metrics);
        }

        _logger.LogInformation(
            "Face login stage tracked: Stage={Stage}, Success={Success}, FrameCount={FrameCount}, PayloadBytes={PayloadBytes}, DurationMs={DurationMs}",
            stage,
            success,
            frameCount,
            payloadBytes,
            durationMs);
    }
}
