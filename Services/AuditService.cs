using Africanideasfaceapi.Helpers;
using Africanideasfaceapi.Models;
using Africanideasfaceapi.Data;
using Microsoft.EntityFrameworkCore;

namespace Africanideasfaceapi.Services;

public class AuditService
{
    private readonly ILogger<AuditService> _logger;
    private readonly TelemetryService? _telemetryService;
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly EmailService? _emailService;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTime LastFailure)> _failureTracker = new();

    public AuditService(ILogger<AuditService> logger, IDbContextFactory<AppDbContext> factory, EmailService? emailService = null, TelemetryService? telemetryService = null)
    {
        _logger = logger;
        _factory = factory;
        _emailService = emailService;
        _telemetryService = telemetryService;
    }

    public void LogUserRegistration(string userId, string email, string registrationMethod)
    {
        _logger.LogInformation("AUDIT: User Registration - UserId: {UserId}, Email: {Email}, Method: {Method}", 
            userId, email, registrationMethod);
        
        _telemetryService?.TrackUserRegistration(userId, registrationMethod);
    }

    public void LogLoginAttempt(string? userId, string email, bool success, string? reason = null, double? latitude = null, double? longitude = null)
    {
        _logger.LogInformation("AUDIT: Login Attempt - UserId: {UserId}, Email: {Email}, Success: {Success}, Reason: {Reason}, Loc: {Lat},{Lon}", 
            userId ?? "unknown", email, success, reason ?? "N/A", latitude, longitude);
        
        _telemetryService?.TrackLoginAttempt(userId ?? "unknown", success, reason);

        // Persist to Database
        try
        {
            Task.Run(async () => {
                try 
                {
                    using var context = await _factory.CreateDbContextAsync();
                    var log = new AuditLog
                    {
                        ActionType = "Login",
                        UserId = userId,
                        Email = email,
                        Result = success ? "Success" : "Failed",
                        ErrorMessage = reason,
                        Latitude = latitude,
                        Longitude = longitude,
                        Timestamp = TimeHelper.GetNow()
                    };
                    context.AuditLogs.Add(log);
                    await context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to persist audit log: {Message}", ex.Message);
                }
            });

            // Security Alert Logic
            if (!success && !string.IsNullOrEmpty(email))
            {
                var now = DateTime.UtcNow;
                var emailKey = email.ToLower().Trim();
                _failureTracker.AddOrUpdate(emailKey, (1, now), (key, old) => 
                {
                    // Reset if last failure was more than 10 mins ago
                    if ((now - old.LastFailure).TotalMinutes > 10) return (1, now);
                    return (old.Count + 1, now);
                });

                if (_failureTracker.TryGetValue(emailKey, out var tracking) && tracking.Count >= 3)
                {
                    _logger.LogWarning("SECURITY ALERT: Multiple failed login attempts for {Email}", email);
                    
                    // Fire-and-forget email alert
                    if (_emailService != null)
                    {
                        Task.Run(async () => {
                            try {
                                await _emailService.SendEmailAsync("test@development.africanideas.co.za", 
                                    $"SECURITY ALERT: Repeated Login Failures for {email}", 
                                    $"<div style='font-family: Arial; padding: 20px; border: 2px solid #dc3545; border-radius: 10px;'>" +
                                    $"<h2 style='color: #dc3545;'>Security Alert</h2>" +
                                    $"<p>Multiple failed login attempts were detected for: <strong>{email}</strong></p>" +
                                    $"<p><strong>Attempt Count:</strong> {tracking.Count}</p>" +
                                    $"<p><strong>Last Attempt:</strong> {now:yyyy-MM-dd HH:mm:ss} UTC</p>" +
                                    $"<p><strong>Reason:</strong> {reason ?? "Biometric mismatch or unauthorized device"}</p>" +
                                    $"<hr/><p style='color: #666; font-size: 0.8em;'>This is an automated security notification.</p></div>");
                            } catch (Exception ex) {
                                _logger.LogError(ex, "Failed to send security alert email.");
                            }
                        });
                    }
                    
                    // Reset counter after alert to avoid spamming
                    _failureTracker.TryRemove(emailKey, out _);
                }
            }
            else if (success && !string.IsNullOrEmpty(email))
            {
                // Successful login - clear failures
                _failureTracker.TryRemove(email.ToLower().Trim(), out _);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to trigger audit log task: {Message}", ex.Message);
        }
    }

    public void LogAdminAction(string adminUserId, string adminEmail, string action, string targetUserId, string? targetEmail = null)
    {
        _logger.LogInformation("AUDIT: Admin Action - Admin: {AdminUserId} ({AdminEmail}), Action: {Action}, Target: {TargetUserId} ({TargetEmail})", 
            adminUserId, adminEmail, action, targetUserId, targetEmail ?? "N/A");
        
        _telemetryService?.TrackAdminAction(adminUserId, action, targetUserId);
    }

    public void LogFaceRecognition(string operation, string? userId, bool success, double? confidence = null, TimeSpan? duration = null)
    {
        _logger.LogInformation("AUDIT: Face Recognition - Operation: {Operation}, UserId: {UserId}, Success: {Success}, Confidence: {Confidence}, Duration: {Duration}ms", 
            operation, userId ?? "unknown", success, confidence?.ToString("F2") ?? "N/A", duration?.TotalMilliseconds ?? 0);
        
        if (userId != null)
        {
            _telemetryService?.TrackFaceRecognition(operation, userId, success, confidence, duration);
        }
    }

    public void LogUserDeletion(string adminUserId, string deletedUserId, string deletedUserEmail)
    {
        LogAdminAction(adminUserId, "unknown", "DeleteUser", deletedUserId, deletedUserEmail);
    }

    public void LogRoleChange(string adminUserId, string targetUserId, string oldRole, string newRole)
    {
        LogAdminAction(adminUserId, "unknown", $"ChangeRole_{oldRole}_to_{newRole}", targetUserId);
    }
    public void LogConsentAction(string userId, string email, string action)
    {
        _logger.LogInformation("AUDIT: Consent - UserId: {UserId}, Email: {Email}, Action: {Action}, Time: {Time}", 
            userId, email, action, TimeHelper.GetNow());
    }

    public void LogCorrectionRequest(string userId, string attendanceId, string action)
    {
        _logger.LogInformation("AUDIT: Attendance Correction - User: {UserId}, AttId: {AttId}, Action: {Action}", 
            userId, attendanceId, action);
    }
}
