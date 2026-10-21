using Africanideasfaceapi.Helpers;
using Africanideasfaceapi.Data;
using Africanideasfaceapi.Hubs;
using Africanideasfaceapi.Models;
using Africanideasfaceapi.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace Africanideasfaceapi.Services;

public class FaceLogService : IFaceLogService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FaceLogService> _logger;
    private readonly IHubContext<AnalyticsHub>? _hubContext;

    public FaceLogService(IServiceProvider serviceProvider, ILogger<FaceLogService> logger, IHubContext<AnalyticsHub>? hubContext = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _hubContext = hubContext;
    }

    public async Task LogFaceActivityAsync(
        string action, 
        string? userId, 
        bool success, 
        double confidence, 
        string? errorMessage, 
        string? category = null, 
        byte[]? imageBytes = null, 
        string? recognizedPersonId = null, 
        string? recognizedEmail = null)
    {
        try
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var fileStorageService = scope.ServiceProvider.GetService<FileStorageService>();
                var auditService = scope.ServiceProvider.GetService<AuditService>();
                
                string? imagePath = null;
                if (imageBytes != null && fileStorageService != null)
                {
                    using (var ms = new MemoryStream(imageBytes))
                    {
                        imagePath = await fileStorageService.SaveAttemptImageAsync(ms, action);
                    }
                }

                var log = new FaceLog
                {
                    EmployeeId = userId ?? "Unknown",
                    Action = action,
                    ConfidenceScore = confidence,
                    Success = success,
                    ErrorMessage = errorMessage,
                    FailureCategory = category ?? (success ? null : MapErrorToCategory(errorMessage)),
                    FaceImagePath = imagePath,
                    RecognizedUserId = recognizedPersonId,
                    RecognizedUserEmail = recognizedEmail,
                    Timestamp = TimeHelper.GetNow()
                };

                context.FaceLogs.Add(log);
                await context.SaveChangesAsync();
                
                auditService?.LogFaceRecognition(action, userId, success, confidence);

                if (_hubContext != null)
                {
                    _ = _hubContext.Clients.All.SendAsync("AnalyticsUpdated", "Biometric", $"Face activity: {action} ({(success ? "Success" : "Failed")})");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist FaceLog");
        }
    }

    public string MapErrorToCategory(string? error)
    {
        if (string.IsNullOrEmpty(error)) return "Unknown";
        
        var lowerError = error.ToLower();
        if (lowerError.Contains("no face")) return "NoFace";
        if (lowerError.Contains("multiple face")) return "MultipleFaces";
        if (lowerError.Contains("confidence")) return "LowConfidence";
        if (lowerError.Contains("angle") || lowerError.Contains("pose") || lowerError.Contains("face the camera")) return "Pose";
        if (lowerError.Contains("covered") || lowerError.Contains("occlude")) return "Occlusion";
        if (lowerError.Contains("lighting") || lowerError.Contains("dark") || lowerError.Contains("bright")) return "Lighting";
        if (lowerError.Contains("not recognized") || lowerError.Contains("no account")) return "WrongUser";
        
        return "SystemError";
    }
}
