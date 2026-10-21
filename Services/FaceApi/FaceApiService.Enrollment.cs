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
    public async Task<string> CreatePersonAsync(string userName)
    {
        var person = await _faceClient.CreatePersonAsync(_personGroupId, userName);
        return person.PersonId.ToString();
    }

    public async Task<(bool IsValid, string? Error)> AddFaceToPersonAsync(string personId, Stream imageStream)
    {
        try
        {
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

            var faceAttributes = new List<FaceAttributeType>
            {
                FaceAttributeType.HeadPose,
                FaceAttributeType.Occlusion
            };

                using (var detectStream = new MemoryStream(imageBytes))
                {
                    var faces = await _faceClient.DetectWithStreamAsync(
                        detectStream,
                        returnFaceAttributes: faceAttributes,
                        recognitionModel: RecognitionModel.Recognition04,
                        detectionModel: DetectionModel.Detection03,
                        returnRecognitionModel: true
                    );

                    if (faces == null || faces.Count == 0)
                        return (false, "No face detected in the image.");

                    if (faces.Count > 1)
                        return (false, "Multiple faces detected. Please ensure only one face is visible.");

                    var face = faces[0];
                    var attributes = face.FaceAttributes;
                    var errors = new List<string>();

                    if (attributes?.HeadPose != null)
                    {
                        if (Math.Abs(attributes.HeadPose.Pitch) > 25 || Math.Abs(attributes.HeadPose.Roll) > 25 || Math.Abs(attributes.HeadPose.Yaw) > 25)
                            errors.Add("Face angle is too steep. Please look more directly at the camera.");
                    }

                    if (attributes?.Occlusion != null)
                    {
                        if (attributes.Occlusion.ForeheadOccluded || attributes.Occlusion.EyeOccluded || attributes.Occlusion.MouthOccluded)
                            errors.Add("Face is partially covered. Please ensure your face is fully visible.");
                    }

                    if (errors.Any())
                        return (false, string.Join(" ", errors));

                    using (var addFaceStream = new MemoryStream(imageBytes))
                    {
                        await _faceClient.AddFaceFromStreamAsync(
                            _personGroupId,
                            Guid.Parse(personId),
                            addFaceStream
                        );
                    }

                    return (true, null);
                }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding face to person {PersonId}", personId);
            return (false, $"Error processing image: {ex.Message}");
        }
    }

    public async Task TrainPersonGroupAsync()
    {
        try
        {
            await _faceClient.TrainPersonGroupAsync(_personGroupId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Training failed for PersonGroup {PersonGroupId}", _personGroupId);
        }
    }

    public async Task<(bool IsValid, string? PersonId, string? Error)> DetectAndValidateFaceAsync(Stream imageStream, string userName)
    {
        try
        {
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

            // Use a minimal set of attributes to keep the call fast
            var faceAttributes = new List<FaceAttributeType>
            {
                FaceAttributeType.HeadPose,
                FaceAttributeType.Occlusion
            };

            // Optimization: Run Detection and Person Creation in PARALLEL
            // We start the costly detection and the simpler person creation simultaneously.
            // If detection fails, we can delete the person or just ignore the ID (cleaning up later or now).
            
            // Task 1: Detect Face
            var detectionTask = Task.Run(async () => 
            {
                using (var detectStream = new MemoryStream(imageBytes))
                {
                    return await _faceClient.DetectWithStreamAsync(
                        detectStream,
                        returnFaceAttributes: faceAttributes,
                        recognitionModel: RecognitionModel.Recognition04,
                        detectionModel: DetectionModel.Detection03,
                        returnRecognitionModel: true
                    );
                }
            });

            // Task 2: Create Person
            var createPersonTask = _faceClient.CreatePersonAsync(_personGroupId, userName);

            // Wait for both
            await Task.WhenAll(detectionTask, createPersonTask);

            var faces = await detectionTask;
            var person = await createPersonTask;

            // Validate Detection
            if (faces == null || faces.Count == 0)
            {
                // Rollback
                await _faceClient.DeletePersonAsync(_personGroupId, person.PersonId);
                return (false, null, "No face detected in the image. Please ensure your face is clearly visible.");
            }

            if (faces.Count > 1)
            {
                 // Rollback
                await _faceClient.DeletePersonAsync(_personGroupId, person.PersonId);
                return (false, null, "Multiple faces detected. Please upload an image with only one face.");
            }

            var face = faces[0];
            var attributes = face.FaceAttributes;
            var errors = new List<string>();

            // Check head pose
            if (attributes?.HeadPose != null)
            {
                var pitch = attributes.HeadPose.Pitch;
                var roll = attributes.HeadPose.Roll;
                var yaw = attributes.HeadPose.Yaw;

                if (Math.Abs(pitch) > 20 || Math.Abs(roll) > 20 || Math.Abs(yaw) > 20)
                {
                    errors.Add("Face angle is not ideal. Please face the camera directly.");
                }
            }

            // Check occlusion
            if (attributes?.Occlusion != null)
            {
                var occlusion = attributes.Occlusion;
                if (occlusion.ForeheadOccluded == true || 
                    occlusion.EyeOccluded == true || 
                    occlusion.MouthOccluded == true)
                {
                    errors.Add("Face is partially covered. Please ensure your face is fully visible.");
                }
            }

            if (errors.Any())
            {
                // Rollback
                await _faceClient.DeletePersonAsync(_personGroupId, person.PersonId);
                return (false, null, string.Join(" ", errors));
            }

            // Face is valid, add it to the person
            _logger.LogInformation("Creating person for user: {UserName}", userName);
            
            // Use a fresh stream for adding face to person
            using (var addFaceStream = new MemoryStream(imageBytes))
            {
                _logger.LogDebug("Adding face to person {PersonId}", person.PersonId);
                await _faceClient.AddFaceFromStreamAsync(
                    _personGroupId,
                    person.PersonId,
                    addFaceStream
                );
            }

            // Train the person group in the background for speed
            _logger.LogInformation("Triggering background training for PersonGroup {PersonGroupId}", _personGroupId);
            _ = Task.Run(async () =>
            {
                try
                {
                    await _faceClient.TrainPersonGroupAsync(_personGroupId);
                }
                catch (Exception trainEx)
                {
                    _logger.LogWarning(trainEx, "Background training failed for PersonGroup {PersonGroupId}", _personGroupId);
                }
            });

            // Face is valid, return the Person ID
            _logger.LogInformation("Face registration successful for user {UserName}, PersonId: {PersonId}", userName, person.PersonId);
            return (true, person.PersonId.ToString(), null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing face registration for user {UserName}", userName);
            return (false, null, $"Error processing image: {ex.Message}");
        }
    }
}
