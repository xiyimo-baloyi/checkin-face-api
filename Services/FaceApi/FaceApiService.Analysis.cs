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
    public async Task<bool> DeletePersonAsync(string personId)
    {
        try
        {
            await _faceClient.DeletePersonAsync(_personGroupId, Guid.Parse(personId));
            await _faceClient.TrainPersonGroupAsync(_personGroupId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(bool IsValid, double Yaw, double Pitch, double Roll, Guid? FaceId, string? Error)> DetectHeadPoseAsync(Stream imageStream)
    {
        try
        {
            // Copy stream
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

            using (var detectStream = new MemoryStream(imageBytes))
            {
                var faces = await _faceClient.DetectWithStreamAsync(
                    detectStream,
                    returnFaceAttributes: new List<FaceAttributeType> { FaceAttributeType.HeadPose },
                    recognitionModel: RecognitionModel.Recognition04,
                    detectionModel: DetectionModel.Detection03,
                    returnRecognitionModel: true
                );

                if (faces == null || faces.Count == 0)
                {
                    return (false, 0, 0, 0, null, "No face detected.");
                }

                if (faces.Count > 1)
                {
                    return (false, 0, 0, 0, null, "Multiple faces detected.");
                }

                var face = faces[0];
                var pose = face.FaceAttributes.HeadPose;
                return (true, pose.Yaw, pose.Pitch, pose.Roll, face.FaceId, null);
            }
        }
        catch (Exception ex)
        {
            return (false, 0, 0, 0, null, $"Error detecting head pose: {ex.Message}");
        }
    }

    /// <summary>
    /// Compare two faces directly
    /// </summary>
    public async Task<(bool IsMatch, double Confidence, string? Error)> CompareFacesAsync(Stream face1Stream, Stream face2Stream)
    {
        try
        {
            _logger.LogDebug("Starting face comparison");

            // Copy streams to byte arrays to avoid streams being closed/disposed by Azure SDK
            byte[] face1Bytes;
            face1Stream.Position = 0;
            using (var ms1 = new MemoryStream())
            {
                await face1Stream.CopyToAsync(ms1);
                face1Bytes = ms1.ToArray();
            }

            byte[] face2Bytes;
            face2Stream.Position = 0;
            using (var ms2 = new MemoryStream())
            {
                await face2Stream.CopyToAsync(ms2);
                face2Bytes = ms2.ToArray();
            }

            // Detect faces in both images
            using (var detectStream1 = new MemoryStream(face1Bytes))
            {
                var faces1 = await _faceClient.DetectWithStreamAsync(
                    detectStream1,
                    returnFaceId: true,
                    recognitionModel: RecognitionModel.Recognition04,
                    detectionModel: DetectionModel.Detection03,
                    returnRecognitionModel: true
                );

                if (faces1 == null || faces1.Count == 0)
                {
                    return (false, 0, "No face detected in first image.");
                }

                if (faces1.Count > 1)
                {
                    return (false, 0, "Multiple faces detected in first image.");
                }

                using (var detectStream2 = new MemoryStream(face2Bytes))
                {
                    var faces2 = await _faceClient.DetectWithStreamAsync(
                        detectStream2,
                        returnFaceId: true,
                        recognitionModel: RecognitionModel.Recognition04,
                        detectionModel: DetectionModel.Detection03,
                        returnRecognitionModel: true
                    );

                    if (faces2 == null || faces2.Count == 0)
                    {
                        return (false, 0, "No face detected in second image.");
                    }

                    if (faces2.Count > 1)
                    {
                        return (false, 0, "Multiple faces detected in second image.");
                    }

                    var face1Id = faces1[0].FaceId;
                    var face2Id = faces2[0].FaceId;

                    if (face1Id == null || !face1Id.HasValue || face2Id == null || !face2Id.HasValue)
                    {
                        return (false, 0, "Failed to extract face IDs.");
                    }

                    // Compare faces
                    var verifyResult = await _faceClient.VerifyFaceToFaceAsync(
                        face1Id.GetValueOrDefault(),
                        face2Id.GetValueOrDefault()
                    );

                    var isMatch = verifyResult.Confidence >= 0.7;
                    _logger.LogInformation("Face comparison completed: Match={Match}, Confidence={Confidence}",
                        isMatch, verifyResult.Confidence);

                    return (isMatch, verifyResult.Confidence, isMatch ? null : $"Face match confidence ({verifyResult.Confidence:P2}) is below the required threshold.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during face comparison");
            return (false, 0, $"Error comparing faces: {ex.Message}");
        }
    }

    /// <summary>
    /// Detect potential duplicate registrations by comparing faces
    /// </summary>
    public async Task<List<(string PersonId, double Confidence)>> DetectDuplicatesAsync(Stream newFaceStream, UserService userService)
    {
        try
        {
            _logger.LogDebug("Starting duplicate detection");

            // Copy stream to byte array to avoid stream being closed/disposed by Azure SDK
            byte[] imageBytes;
            newFaceStream.Position = 0;
            using (var copyStream = new MemoryStream())
            {
                await newFaceStream.CopyToAsync(copyStream);
                imageBytes = copyStream.ToArray();
            }

            // Detect face in new image
            using (var detectStream = new MemoryStream(imageBytes))
            {
                var faces = await _faceClient.DetectWithStreamAsync(
                    detectStream,
                    returnFaceId: true,
                    recognitionModel: RecognitionModel.Recognition04,
                    detectionModel: DetectionModel.Detection03,
                    returnRecognitionModel: true
                );

                if (faces == null || faces.Count == 0)
                {
                    return new List<(string, double)>();
                }

                var newFaceId = faces[0].FaceId;
                if (newFaceId == null || !newFaceId.HasValue)
                {
                    return new List<(string, double)>();
                }

                // Use identification to find similar faces
                var identifyResults = await _faceClient.IdentifyAsync(
                    new List<Guid> { newFaceId.GetValueOrDefault() },
                    _personGroupId
                );

                if (identifyResults == null || !identifyResults.Any() || identifyResults[0].Candidates == null)
                {
                    return new List<(string, double)>();
                }

                var confirmedDuplicates = new List<(string, double)>();
                var threshold = await GetConfidenceThresholdAsync();
                foreach (var candidate in identifyResults[0].Candidates)
                {
                    if (candidate.Confidence < threshold) continue;

                    var personId = candidate.PersonId.ToString();
                    
                    // Verify if this person actually exists in our database
                    var user = await userService.GetUserByPersonIdAsync(personId);
                    
                    if (user != null)
                    {
                        // Genuine duplicate found
                        confirmedDuplicates.Add((personId, candidate.Confidence));
                    }
                    else
                    {
                        // Ghost record found in Azure (Person exists there but not in our DB)
                        // This happens if DB was wiped but Azure wasn't.
                        // We should cleanup this ghost record and NOT counting it as a duplicate.
                        _logger.LogWarning("Found ghost record in Azure PersonGroup: {PersonId}. Deleting...", personId);
                        
                        // Fire and forget deletion to not block flow
                        _ = DeletePersonAsync(personId);
                    }
                }

                _logger.LogInformation("Duplicate detection completed: Found {Count} confirmed duplicates (after filtering ghosts)", confirmedDuplicates.Count);

                return confirmedDuplicates;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during duplicate detection");
            return new List<(string, double)>();
        }
    }

    /// <summary>
    /// Detects and validates a face in a document image without creating a Person record.
    /// Used for checking ID/Passport photo quality during registration.
    /// </summary>
}
