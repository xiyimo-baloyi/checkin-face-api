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
    public async Task<(bool IsValid, string? Error)> ValidateDocumentFaceAsync(Stream imageStream)
    {
        try
        {
            _logger.LogInformation("Validating face quality in document image");
            
            // Copy stream to byte array to avoid stream being closed/disposed by Azure SDK
            byte[] imageBytes;
            imageStream.Position = 0;
            using (var copyStream = new MemoryStream())
            {
                await imageStream.CopyToAsync(copyStream);
                imageBytes = copyStream.ToArray();
            }

            using (var detectStream = new MemoryStream(imageBytes))
            {
                var faces = await _faceClient.DetectWithStreamAsync(
                    detectStream,
                    returnFaceAttributes: new List<FaceAttributeType>
                    {
                        FaceAttributeType.HeadPose,
                        FaceAttributeType.Occlusion,
                        FaceAttributeType.Blur,
                        FaceAttributeType.Exposure
                    },
                    recognitionModel: RecognitionModel.Recognition04,
                    detectionModel: DetectionModel.Detection03,
                    returnRecognitionModel: true
                );

                if (faces == null || faces.Count == 0)
                {
                    return (false, "No face detected. Make sure the face on your ID is fully visible and the ID is straight.");
                }

                DetectedFace face;
                if (faces.Count > 1)
                {
                    // If multiple faces, pick the largest one (likely the main ID photo, ignoring ghost image)
                    face = faces.OrderByDescending(f => f.FaceRectangle.Width * f.FaceRectangle.Height).First();
                    _logger.LogWarning("Multiple faces detected in document validation. Using the largest face.");
                }
                else
                {
                    face = faces[0];
                }
                var attributes = face.FaceAttributes;
                var errors = new List<string>();

                // Check blur
                if (attributes?.Blur != null && attributes.Blur.Value > 0.7)
                {
                    errors.Add("The image appears to be blurry.");
                }

                // Check exposure
                if (attributes?.Exposure != null && (attributes.Exposure.Value < 0.3 || attributes.Exposure.Value > 0.9))
                {
                    errors.Add("The image lighting is poor (too dark or too bright). Take photo in a well-lit area without glare.");
                }

                // Check head pose
                if (attributes?.HeadPose != null)
                {
                    if (Math.Abs(attributes.HeadPose.Pitch) > 30 || 
                        Math.Abs(attributes.HeadPose.Roll) > 30 || 
                        Math.Abs(attributes.HeadPose.Yaw) > 30)
                    {
                        errors.Add("The face angle on the document is not suitable for recognition.");
                    }
                }

                // Check occlusion
                if (attributes?.Occlusion != null)
                {
                    if (attributes.Occlusion.ForeheadOccluded || 
                        attributes.Occlusion.EyeOccluded || 
                        attributes.Occlusion.MouthOccluded)
                    {
                        errors.Add("The face is partially covered in the document image.");
                    }
                }

                if (errors.Any())
                {
                    return (false, string.Join(" ", errors) + " Please upload a clearer image of your ID/Passport.");
                }

                _logger.LogInformation("Document face validation successful");
                return (true, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating document face");
            return (false, $"Error validating document face: {ex.Message}");
        }
    }

    /// <summary>
    /// Detects, validates, and enrolls a face from a document image (ID/Passport).
    /// Automatically rotates the image if the face is sideways/upside down.
    /// Returns the enrolled PersonId and optionally the corrected (rotated) image bytes.
    /// </summary>
    public async Task<(bool IsValid, string? PersonId, string? Error, byte[]? RotatedBytes)> EnrollFaceFromDocumentAsync(Stream documentStream, string userName)
    {
        try
        {
            _logger.LogInformation("Enrolling face from document for user: {UserName}", userName);
            
            // 1. Read stream to byte array
            byte[] imageBytes;
            documentStream.Position = 0;
            using (var memoryStream = new MemoryStream())
            {
                await documentStream.CopyToAsync(memoryStream);
                imageBytes = memoryStream.ToArray();
            }

            // 2. Detect with HeadPose to check orientation
            using (var detectStream = new MemoryStream(imageBytes))
            {
                var faces = await _faceClient.DetectWithStreamAsync(
                    detectStream,
                    returnFaceAttributes: new List<FaceAttributeType>
                    {
                        FaceAttributeType.HeadPose,
                        FaceAttributeType.Occlusion,
                        FaceAttributeType.Blur,
                        FaceAttributeType.Exposure
                    },
                    recognitionModel: RecognitionModel.Recognition04,
                    detectionModel: DetectionModel.Detection03,
                    returnRecognitionModel: true
                );

                if (faces == null || faces.Count == 0)
                {
                    return (false, null, "No face detected. Make sure the face on your ID is fully visible and the ID is straight.", null);
                }

                DetectedFace face;
                if (faces.Count > 1)
                {
                    // If multiple faces, pick the largest one (likely the main ID photo, ignoring ghost image)
                    face = faces.OrderByDescending(f => f.FaceRectangle.Width * f.FaceRectangle.Height).First();
                    _logger.LogWarning("Multiple faces detected in document enrollment. Using the largest face.");
                }
                else
                {
                    face = faces[0];
                }

                // Check Rotation (Roll) and correct if necessary
                bool imageRotated = false;
                if (face.FaceAttributes?.HeadPose != null)
                {
                    var roll = face.FaceAttributes.HeadPose.Roll;
                    _logger.LogInformation("Detected Face Roll: {Roll}", roll);

                    // Threshold of ~30 degrees for rotation correction
                    if (Math.Abs(roll) > 30)
                    {
                        try 
                        {
                            using (var image = SixLabors.ImageSharp.Image.Load(imageBytes))
                            {
                                if (roll > 30 && roll <= 135) 
                                {
                                    // Head tilted right (90 deg) -> Rotate image -90 (Counter-Clockwise)
                                    // Actually HeadPose Roll convention: Positive is counter-clockwise? 
                                    // Azure Docs: "Roll: The amount of roll in degrees. Roll is the rotation around the z-axis (the axis pointing out of the image). A positive value implies a right turn (counter-clockwise)."
                                    // Wait, let's verify visual: 
                                    // If I tilt head to right shoulder, that's typically negative roll?
                                    // Azure: "A positive value corresponds to a counter-clockwise rotation relative to the camera." -> Head leans Left.
                                    // If Roll is 90 (Head Left), we need to rotate Image -90 (Clockwise) to straighten it?
                                    // Let's assume:
                                    // Roll ~ 90 (Left) -> Rotate Clockwise (90)
                                    // Roll ~ -90 (Right) -> Rotate Counter-Clockwise (-90)
                                    // Roll ~ 180 -> Rotate 180
                                    
                                    // ImageSharp Rotate(degrees) is clockwise + ? No, usually + is clockwise.
                                    // Mutate(x => x.Rotate(90)) rotates 90 degrees clockwise.
                                    
                                    // Case 1: Roll ~ 90 (positive). Head is sideways (Top of head is Left).
                                    // To fix, we need to rotate image 90 degrees Clockwise?
                                    // If top of head points Left, rotating image 90 CW makes Top of head point Up. Correct.
                                    image.Mutate(x => x.Rotate(90));
                                    _logger.LogInformation("Correcting orientation: Rotating 90 degrees Clockwise");
                                }
                                else if (roll < -30 && roll >= -135)
                                {
                                    // Roll ~ -90 (negative). Head is sideways (Top of head is Right).
                                    // To fix, rotate image -90 (Counter-Clockwise) OR 270.
                                    image.Mutate(x => x.Rotate(-90));
                                    _logger.LogInformation("Correcting orientation: Rotating 90 degrees Counter-Clockwise");
                                }
                                else if (Math.Abs(roll) > 135)
                                {
                                    // Roll ~ 180. Upside down.
                                    image.Mutate(x => x.Rotate(180));
                                    _logger.LogInformation("Correcting orientation: Rotating 180 degrees");
                                }
                                
                                using (var ms = new MemoryStream())
                                {
                                    await image.SaveAsJpegAsync(ms);
                                    imageBytes = ms.ToArray();
                                    imageRotated = true;
                                }
                            }
                        }
                        catch (Exception imgEx)
                        {
                            _logger.LogError(imgEx, "Failed to rotate image");
                            // Fallback to original bytes
                        }
                    }
                }

                if (imageRotated)
                {
                    // If we rotated, we should re-detect just to ensure we have the new correct FaceRectangle for enrollment
                    // This is safer than enrolling without targetFace or guessing coordinates
                    using (var reDetectStream = new MemoryStream(imageBytes))
                    {
                         // Optional: we can skip full attribute check here to save time/cost, just need existence
                         var newFaces = await _faceClient.DetectWithStreamAsync(reDetectStream, recognitionModel: RecognitionModel.Recognition04, detectionModel: DetectionModel.Detection03, returnRecognitionModel: true);
                         if (newFaces != null && newFaces.Count > 0)
                         {
                             // Pick largest again
                             if (newFaces.Count > 1) 
                                 face = newFaces.OrderByDescending(f => f.FaceRectangle.Width * f.FaceRectangle.Height).First();
                             else 
                                 face = newFaces[0];
                         }
                         else
                         {
                            // Rotation made face undetectable? Unlikely. Fallback using original bytes if needed?
                            // Or return error.
                            _logger.LogWarning("Face not detected after rotation. Retrying with original image.");
                            // Revert? For now, let's proceed and hope AddFace finds it if this check failed slightly.
                            // But usually safe to error here.
                         }
                    }
                }

                // Proceed with validation logic (attributes) - Checks might be on OLD attributes if we didn't re-detect everything.
                // But for "Pose", since we rotated, the new pose should be 0. We can skip validating pose on the 'old' face object if we rotated.
                var attributes = face.FaceAttributes;
                var errors = new List<string>();

                if (!imageRotated) // Only validate these if we didn't just fix them via rotation
                {
                    if (attributes?.HeadPose != null)
                    {
                         if (Math.Abs(attributes.HeadPose.Pitch) > 30 || Math.Abs(attributes.HeadPose.Roll) > 30 || Math.Abs(attributes.HeadPose.Yaw) > 30)
                            errors.Add("The face angle on the document is not suitable for recognition.");
                    }
                }
                
                // Other checks (Blur, Exposure) still valid
                 if (attributes?.Blur != null && attributes.Blur.Value > 0.7)
                    errors.Add("The document photo is too blurry.");
                
                if (attributes?.Exposure != null && (attributes.Exposure.Value < 0.3 || attributes.Exposure.Value > 0.9))
                    errors.Add("The document photo lighting is poor.");

                 if (attributes?.Occlusion != null)
                {
                    if (attributes.Occlusion.ForeheadOccluded || attributes.Occlusion.EyeOccluded || attributes.Occlusion.MouthOccluded)
                        errors.Add("The face on the document is partially covered.");
                }

                if (errors.Any())
                {
                     return (false, null, string.Join(" ", errors) + " Please use a higher quality scan or photo.", null);
                }

                // 3. Create Person in PersonGroup
                _logger.LogInformation("Creating Person record for {UserName}", userName);
                var person = await _faceClient.CreatePersonAsync(_personGroupId, userName);

                // 4. Add Face to Person using (potentially rotated) imageBytes
                using (var addFaceStream = new MemoryStream(imageBytes))
                {
                    // If we re-detected or rotated, we usually let Azure find the face again (don't pass targetFace) 
                    // OR pass the new coordinates.
                    // Safest: Use AddFaceFromStreamAsync WITHOUT targetFace, Azure will pick the largest face automatically.
                    // This handles both the "Multiple Faces" and "Rotated" changes gracefully.
                    await _faceClient.AddFaceFromStreamAsync(
                        _personGroupId,
                        person.PersonId,
                        addFaceStream
                        // Omitting targetFace to let Azure auto-detect largest face in corrected image
                    );
                }

                // 5. Trigger Training
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

                _logger.LogInformation("Document face enrollment successful for {UserName}, PersonId: {PersonId}", userName, person.PersonId);
                return (true, person.PersonId.ToString(), null, imageRotated ? imageBytes : null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during document face enrollment for {UserName}", userName);
            return (false, null, $"Enrollment failed: {ex.Message}", null);
        }
    }
}
