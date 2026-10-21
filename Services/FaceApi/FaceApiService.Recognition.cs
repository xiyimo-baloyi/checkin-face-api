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
    public async Task<(bool IsMatch, User? User, double Confidence, string? Error)> RecognizeFaceAsync(Stream imageStream, UserService userService)
    {
        var operationStart = DateTimeOffset.UtcNow;
        var operationWatch = Stopwatch.StartNew();
        long detectMs = 0;
        long identifyMs = 0;
        long userLookupMs = 0;
        try
        {
            _logger.LogDebug("Starting face recognition");
            
            // Copy stream to byte array to avoid stream being closed/disposed by Azure SDK
            byte[] imageBytes;
            if (imageStream is MemoryStream memStream && memStream.CanRead && memStream.CanSeek)
            {
                imageBytes = memStream.ToArray();
            }
            else
            {
                imageStream.Position = 0;
                using (var copyStream = new MemoryStream())
                {
                    await imageStream.CopyToAsync(copyStream);
                    imageBytes = copyStream.ToArray();
                }
            }

                // Detect face in the provided image
                using (var detectStream = new MemoryStream(imageBytes))
                {
                    var detectWatch = Stopwatch.StartNew();
                    var faces = await _faceClient.DetectWithStreamAsync(
                        detectStream,
                        returnFaceId: true,
                        recognitionModel: RecognitionModel.Recognition04,
                        detectionModel: DetectionModel.Detection03,
                        returnRecognitionModel: true
                    );
                    detectMs = detectWatch.ElapsedMilliseconds;

                    if (faces == null || faces.Count == 0)
                    {
                        _logger.LogWarning("No face detected during recognition attempt");
                        return (false, null, 0, "No face detected in the image.");
                    }

                    if (faces.Count > 1)
                    {
                        _logger.LogWarning("Multiple faces detected during recognition attempt");
                        return (false, null, 0, "Multiple faces detected. Please use an image with only one face.");
                    }

                    var faceId = faces[0].FaceId;
                    if (faceId == null || !faceId.HasValue)
                    {
                        return (false, null, 0, "Face detection failed - no face ID returned.");
                    }

                    var detectedFaceId = faceId.GetValueOrDefault();

                    // Use Face Identification to find the person
                    var identifyWatch = Stopwatch.StartNew();
                    var identifyResults = await _faceClient.IdentifyAsync(
                        new List<Guid> { detectedFaceId },
                        _personGroupId
                    );
                    identifyMs = identifyWatch.ElapsedMilliseconds;

                if (identifyResults == null || !identifyResults.Any())
                {
                    _logger.LogWarning("Identify API returned null or empty results for FaceId: {FaceId}", detectedFaceId);
                    return (false, null, 0, "Biometric verification system is currently unavailable. Please try again later.");
                }

                if (identifyResults[0].Candidates == null || !identifyResults[0].Candidates.Any())
                {
                    _logger.LogInformation("No candidates found for FaceId: {FaceId} in PersonGroup: {PersonGroupId}", detectedFaceId, _personGroupId);
                    return (false, null, 0, "Face not recognized. Please ensure you are registered and looking directly at the camera.");
                }

                var candidate = identifyResults[0].Candidates[0];
                var confidence = candidate.Confidence;
                var threshold = await GetConfidenceThresholdAsync();
                if (confidence < threshold)
                {
                    _logger.LogWarning("Face recognition confidence too low: {Confidence} (threshold: {Threshold})", confidence, threshold);
                    return (false, null, confidence, "Face not recognized with sufficient confidence. Please try again.");
                }

                var personId = candidate.PersonId.ToString();
                _logger.LogInformation("Face recognized with confidence {Confidence}, PersonId: {PersonId}", candidate.Confidence, personId);

                // Find the user by PersonId using direct database query (more efficient)
                var userLookupWatch = Stopwatch.StartNew();
                var user = await userService.GetUserByPersonIdAsync(personId);
                userLookupMs = userLookupWatch.ElapsedMilliseconds;

                if (user == null)
                {
                    _logger.LogWarning("User not found for PersonId: {PersonId}. This may indicate the user was deleted or PersonId is not set in the database.", personId);
                    
                    // Log the failure with "WrongUser" or "UnknownUser" category
                    _ = LogFaceActivityAsync("Recognition", null, false, confidence, "Face recognized but no local account found.", "WrongUser", imageBytes, personId);

                    return (false, null, confidence, "Face recognized but no account found. If you recently registered, please try again or contact support.");
                }

                _logger.LogInformation("Face recognition successful for user: {Email}", user.Email);
                
                // Log the success
                _ = LogFaceActivityAsync("Recognition", user.Id, true, confidence, null, null, imageBytes, personId, user.Email);

                return (true, user, confidence, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during face recognition");
            
            // Map error message to a category
            var category = FaceRecognitionErrorMapper.MapErrorToCategory(ex.Message);
            _ = LogFaceActivityAsync("Recognition", null, false, 0, ex.Message, category);

            return (false, null, 0, $"Error recognizing face: {ex.Message}");
        }
        finally
        {
            operationWatch.Stop();
            EmitRecognitionTelemetry("Recognition", operationStart, operationWatch.ElapsedMilliseconds, detectMs, identifyMs, userLookupMs);
        }
    }

    public async Task<(bool IsMatch, User? User, double Confidence, string? Error)> RecognizeFromMultipleFramesAsync(
        IEnumerable<Stream> frames,
        UserService userService)
    {
        var operationStart = DateTimeOffset.UtcNow;
        var operationWatch = Stopwatch.StartNew();
        long detectMs = 0;
        long identifyMs = 0;
        long userLookupMs = 0;
        try
        {
            _logger.LogDebug("Starting multi-frame face recognition");

            var faceIds = new List<Guid>();
            byte[]? firstImageBytes = null;

            var detectionTasks = frames.Select(async frame =>
            {
                byte[] imageBytes;
                if (frame is MemoryStream ms && ms.CanRead && ms.CanSeek)
                {
                    imageBytes = ms.ToArray();
                }
                else
                {
                    frame.Position = 0;
                    using var copy = new MemoryStream();
                    await frame.CopyToAsync(copy);
                    imageBytes = copy.ToArray();
                }

                // Capture first image bytes for logging
                if (firstImageBytes == null)
                {
                    lock (frames) // Simple thread-safe set
                    {
                        firstImageBytes ??= imageBytes;
                    }
                }

                using var detectStream = new MemoryStream(imageBytes);
                var faces = await _faceClient.DetectWithStreamAsync(
                    detectStream,
                    returnFaceId: true,
                    recognitionModel: RecognitionModel.Recognition04,
                    detectionModel: DetectionModel.Detection03,
                    returnRecognitionModel: true);

                if (faces != null && faces.Count == 1 && faces[0].FaceId.HasValue)
                {
                    return faces[0].FaceId!.Value;
                }
                return (Guid?)null;
            }).ToList();

            var detectWatch = Stopwatch.StartNew();
            var results = await Task.WhenAll(detectionTasks);
            detectMs = detectWatch.ElapsedMilliseconds;
            faceIds.AddRange(results.Where(id => id.HasValue).Select(id => id!.Value));

            if (faceIds.Count == 0)
            {
                _logger.LogWarning("No usable faces detected across all frames.");
                return (false, null, 0, "No usable faces detected across captured frames. Please move closer, check lighting, and try again.");
            }

            var identifyWatch = Stopwatch.StartNew();
            var identifyResults = await _faceClient.IdentifyAsync(faceIds, _personGroupId);
            identifyMs = identifyWatch.ElapsedMilliseconds;

            if (identifyResults == null || !identifyResults.Any())
            {
                _logger.LogWarning("Identify API (multi-frame) returned null or empty results.");
                return (false, null, 0, "Biometric verification system is currently unavailable. Please try again later.");
            }

            var allCandidates = identifyResults
                .Where(r => r.Candidates != null)
                .SelectMany(r => r.Candidates!)
                .ToList();

            if (allCandidates == null || allCandidates.Count == 0)
            {
                _logger.LogInformation("No candidates found across {FrameCount} frames in PersonGroup: {PersonGroupId}", faceIds.Count, _personGroupId);
                return (false, null, 0, "Face not recognized. Please ensure you are registered and move closer to the camera.");
            }

            var bestCandidate = allCandidates
                .OrderByDescending(c => c.Confidence)
                .First();

            var confidence = bestCandidate.Confidence;
            var threshold = await GetConfidenceThresholdAsync();

            if (confidence < threshold)
            {
                _logger.LogWarning("Multi-frame recognition confidence too low: {Confidence} (threshold: {Threshold})", confidence, threshold);
                return (false, null, confidence, "Face not recognized with sufficient confidence. Please try again.");
            }

            var personId = bestCandidate.PersonId.ToString();
            _logger.LogInformation("Multi-frame face recognized with confidence {Confidence}, PersonId: {PersonId}", confidence, personId);

            var userLookupWatch = Stopwatch.StartNew();
            var user = await userService.GetUserByPersonIdAsync(personId);
            userLookupMs = userLookupWatch.ElapsedMilliseconds;
            if (user == null)
            {
                _logger.LogWarning("User not found for PersonId (multi-frame): {PersonId}", personId);
                _ = LogFaceActivityAsync(
                    "RecognitionMulti",
                    null,
                    false,
                    confidence,
                    "Face recognized but no local account found.",
                    "WrongUser",
                    firstImageBytes,
                    personId);

                return (false, null, confidence, "Face recognized but no account found. If you recently registered, please try again or contact support.");
            }

            _ = LogFaceActivityAsync(
                "RecognitionMulti",
                user.Id,
                true,
                confidence,
                null,
                null,
                firstImageBytes,
                personId,
                user.Email);

            return (true, user, confidence, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during multi-frame face recognition");
            var category = FaceRecognitionErrorMapper.MapErrorToCategory(ex.Message);
            _ = LogFaceActivityAsync("RecognitionMulti", null, false, 0, ex.Message, category);
            return (false, null, 0, $"Error recognizing face: {ex.Message}");
        }
        finally
        {
            operationWatch.Stop();
            EmitRecognitionTelemetry("RecognitionMulti", operationStart, operationWatch.ElapsedMilliseconds, detectMs, identifyMs, userLookupMs);
        }
    }

    /// <summary>
    /// Enriches an existing person with additional faces from recent login frames,
    /// maintaining at most FaceApiConstants.MaxPersistedFacesPerPerson faces per account.
    /// </summary>
    public async Task EnrichPersonWithFramesAsync(
        string personId,
        IEnumerable<byte[]> frameBytes,
        double loginConfidence)
    {
        if (string.IsNullOrWhiteSpace(personId))
        {
            _logger.LogDebug("Enrichment skipped: empty personId.");
            return;
        }

        if (frameBytes == null)
        {
            return;
        }

        try
        {
            var threshold = await GetConfidenceThresholdAsync();
            var enrichmentThreshold = Math.Clamp(threshold + FaceApiConstants.EnrichmentConfidenceMargin, 0.0, 1.0);

            if (loginConfidence < enrichmentThreshold)
            {
                _logger.LogDebug(
                    "Enrichment skipped: confidence {Confidence} below enrichment threshold {Threshold}.",
                    loginConfidence,
                    enrichmentThreshold);
                return;
            }

            if (!Guid.TryParse(personId, out var personGuid))
            {
                _logger.LogWarning("Enrichment skipped: invalid personId format: {PersonId}", personId);
                return;
            }

            var framesToUse = frameBytes
                .Where(b => b != null && b.Length > 0)
                .Take(FaceApiConstants.MaxEnrichmentFramesPerLogin)
                .ToList();

            if (framesToUse.Count == 0)
            {
                _logger.LogDebug("Enrichment skipped: no usable frame bytes provided.");
                return;
            }

            foreach (var bytes in framesToUse)
            {
                try
                {
                    using var ms = new MemoryStream(bytes);
                    await _faceClient.AddFaceFromStreamAsync(_personGroupId, personGuid, ms);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add enrichment face for person {PersonId}", personId);
                }
            }

            try
            {
                var person = await _faceClient.GetFacePersonAsync(_personGroupId, personGuid);
                var persistedFaces = person.PersistedFaceIds;

                if (persistedFaces != null && persistedFaces.Count > FaceApiConstants.MaxPersistedFacesPerPerson)
                {
                    var toDeleteCount = persistedFaces.Count - FaceApiConstants.MaxPersistedFacesPerPerson;
                    var toDelete = persistedFaces.Take(toDeleteCount).ToList();

                    foreach (var faceGuid in toDelete)
                    {
                        try
                        {
                            await _faceClient.DeleteFaceAsync(_personGroupId, personGuid, faceGuid);
                            _logger.LogInformation("Deleted old persisted face {FaceId} for person {PersonId}", faceGuid, personId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete old persisted face {FaceId} for person {PersonId}", faceGuid, personId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to trim persisted faces for person {PersonId}", personId);
            }

            // Train person group in the background after enrichment
            _ = Task.Run(async () =>
            {
                try
                {
                    await _faceClient.TrainPersonGroupAsync(_personGroupId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background training failed after enrichment for person {PersonId}", personId);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EnrichPersonWithFramesAsync failed for person {PersonId}", personId);
        }
    }
}
