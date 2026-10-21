using Africanideasfaceapi.Helpers;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using Africanideasfaceapi.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Africanideasfaceapi.Data;
using Microsoft.EntityFrameworkCore;
using Africanideasfaceapi.Hubs;
using Africanideasfaceapi.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Africanideasfaceapi.Models.Configuration;
using System.Diagnostics;
using Africanideasfaceapi.Services.FaceApi;

namespace Africanideasfaceapi.Services;

public partial class FaceApiService
{
    private async Task LogFaceActivityAsync(
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
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fileStorageService = scope.ServiceProvider.GetService<FileStorageService>();
            var auditService = scope.ServiceProvider.GetService<AuditService>();

            string? imagePath = null;
            if (imageBytes != null && fileStorageService != null && !success)
            {
                using var ms = new MemoryStream(imageBytes);
                imagePath = await fileStorageService.SaveAttemptImageAsync(ms, action);
            }

            var log = new FaceLog
            {
                EmployeeId = userId ?? "Unknown",
                Action = action,
                ConfidenceScore = confidence,
                Success = success,
                ErrorMessage = errorMessage,
                FailureCategory = category ?? (success ? null : FaceRecognitionErrorMapper.MapErrorToCategory(errorMessage)),
                FaceImagePath = PathHelper.FixImagePath(imagePath),
                RecognizedUserId = recognizedPersonId,
                RecognizedUserEmail = recognizedEmail,
                Timestamp = TimeHelper.GetNow()
            };

            context.FaceLogs.Add(log);
            await context.SaveChangesAsync();

            // Also log to audit service if available
            auditService?.LogFaceRecognition(action, userId, success, confidence);

            // Broadcast real-time update
            if (_hubContext != null)
            {
                // Use fire-and-forget to not block the main flow
                _ = _hubContext.Clients.All.SendAsync(
                    "AnalyticsUpdated",
                    "Biometric",
                    $"Face activity: {action} ({(success ? "Success" : "Failed")})");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist FaceLog");
        }
    }

    /// <summary>
    /// Attempts to recognize a user from multiple captured frames.
    /// Succeeds if any frame contains a face that identifies to a known user
    /// with confidence above the configured threshold.
    /// </summary>

    private async Task<double> GetConfidenceThresholdAsync()
    {
        if (_cachedConfidenceThreshold.HasValue && _confidenceThresholdExpiresAt > DateTime.UtcNow)
        {
            return _cachedConfidenceThreshold.Value;
        }

        await _settingsCacheLock.WaitAsync();
        try
        {
            if (_cachedConfidenceThreshold.HasValue && _confidenceThresholdExpiresAt > DateTime.UtcNow)
            {
                return _cachedConfidenceThreshold.Value;
            }

            using var scope = _serviceProvider.CreateScope();
            var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
            var settings = await settingsService.GetSettingsAsync();
            var t = settings.FaceRecognitionConfidenceThreshold ?? 0.6;
            var threshold = Math.Clamp(t, 0.4, 0.95);
            _cachedConfidenceThreshold = threshold;
            _confidenceThresholdExpiresAt = DateTime.UtcNow.Add(FaceApiConstants.ConfidenceThresholdCacheTtl);
            return threshold;
        }
        catch
        {
            return 0.6;
        }
        finally
        {
            _settingsCacheLock.Release();
        }
    }

    private void EmitRecognitionTelemetry(
        string operationName,
        DateTimeOffset startedAt,
        long totalMs,
        long detectMs,
        long identifyMs,
        long userLookupMs)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var telemetryService = scope.ServiceProvider.GetService<TelemetryService>();
            if (telemetryService == null)
            {
                return;
            }

            telemetryService.TrackDependency("FaceApi", $"{operationName}.Detect", "DetectWithStreamAsync", startedAt, TimeSpan.FromMilliseconds(detectMs), detectMs > 0);
            telemetryService.TrackDependency("FaceApi", $"{operationName}.Identify", "IdentifyAsync", startedAt, TimeSpan.FromMilliseconds(identifyMs), identifyMs > 0);
            telemetryService.TrackDependency("Database", $"{operationName}.UserLookup", "GetUserByPersonIdAsync", startedAt, TimeSpan.FromMilliseconds(userLookupMs), userLookupMs > 0);
            telemetryService.TrackFaceRecognition(operationName, "system", totalMs > 0, null, TimeSpan.FromMilliseconds(totalMs));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to emit recognition telemetry.");
        }
    }
}
