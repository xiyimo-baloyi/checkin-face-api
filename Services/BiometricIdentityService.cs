using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using Africanideasfaceapi.Models;
using Africanideasfaceapi.Services.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace Africanideasfaceapi.Services;

public class BiometricIdentityService : IBiometricIdentityService
{
    private readonly IFaceClientWrapper _faceClient;
    private readonly IFaceLogService _logService;
    private readonly UserService _userService;
    private readonly SettingsService _settingsService;
    private readonly ILogger<BiometricIdentityService> _logger;
    private readonly string _personGroupId = "africanideas-users";

    public BiometricIdentityService(
        IFaceClientWrapper faceClient,
        IFaceLogService logService,
        UserService userService,
        SettingsService settingsService,
        ILogger<BiometricIdentityService> logger)
    {
        _faceClient = faceClient;
        _logService = logService;
        _userService = userService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<(bool Success, string? PersonId, string? Error)> RegisterIdentityAsync(string name, string surname, Stream imageStream)
    {
        try
        {
            var fullName = $"{name} {surname}";
            byte[] imageBytes = await StreamToByteArray(imageStream);

            // 1. Detect and Validate Face
            var faces = await _faceClient.DetectWithStreamAsync(
                new MemoryStream(imageBytes),
                returnFaceAttributes: new List<FaceAttributeType> { FaceAttributeType.HeadPose, FaceAttributeType.Occlusion },
                recognitionModel: RecognitionModel.Recognition04,
                detectionModel: DetectionModel.Detection03
            );

            if (faces == null || faces.Count == 0)
                return (false, null, "No face detected. Please ensure your face is clearly visible.");

            if (faces.Count > 1)
                return (false, null, "Multiple faces detected. Please ensure only one person is in the frame.");

            var face = faces[0];
            var (isValid, poseError) = ValidateFaceAttributes(face.FaceAttributes);
            if (!isValid)
                return (false, null, poseError);

            // 2. Check for Duplicates
            var duplicates = await DetectDuplicatesInternal(new MemoryStream(imageBytes));
            if (duplicates.Any(d => d.Confidence > 0.7))
                return (false, null, "This identity appears to be already registered.");

            // 3. Create Azure Person
            var person = await _faceClient.CreatePersonAsync(_personGroupId, fullName);
            
            // 4. Add Face
            await _faceClient.AddFaceFromStreamAsync(_personGroupId, person.PersonId, new MemoryStream(imageBytes));

            // 5. Trigger Training
            _ = _faceClient.TrainPersonGroupAsync(_personGroupId);

            _logger.LogInformation("Successfully registered biometric identity for {FullName}. PersonId: {PersonId}", fullName, person.PersonId);
            return (true, person.PersonId.ToString(), null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering identity for {Name} {Surname}", name, surname);
            return (false, null, $"Registry error: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? PersonId, string? Error, byte[]? RotatedBytes)> EnrollFaceFromDocumentAsync(Stream documentStream, string userName)
    {
        try
        {
            _logger.LogInformation("Enrolling face from document for user: {UserName}", userName);
            byte[] imageBytes = await StreamToByteArray(documentStream);

            // 1. Detect and Validate
            var faces = await _faceClient.DetectWithStreamAsync(
                new MemoryStream(imageBytes),
                returnFaceAttributes: new List<FaceAttributeType>
                {
                    FaceAttributeType.HeadPose,
                    FaceAttributeType.Occlusion,
                    FaceAttributeType.Blur,
                    FaceAttributeType.Exposure
                },
                recognitionModel: RecognitionModel.Recognition04,
                detectionModel: DetectionModel.Detection03
            );

            if (faces == null || faces.Count == 0)
                return (false, null, "No face detected in document.", null);

            var face = faces.Count > 1 
                ? faces.OrderByDescending(f => f.FaceRectangle.Width * f.FaceRectangle.Height).First() 
                : faces[0];

            // 2. Orientation Correction
            bool imageRotated = false;
            if (face.FaceAttributes?.HeadPose != null && Math.Abs(face.FaceAttributes.HeadPose.Roll) > 30)
            {
                var correctedBytes = await TryRotateImage(imageBytes, face.FaceAttributes.HeadPose.Roll);
                if (correctedBytes != null)
                {
                    imageBytes = correctedBytes;
                    imageRotated = true;
                    // Re-detect to get new coordinates for enrollment
                    var newFaces = await _faceClient.DetectWithStreamAsync(new MemoryStream(imageBytes), recognitionModel: RecognitionModel.Recognition04, detectionModel: DetectionModel.Detection03);
                    if (newFaces != null && newFaces.Any())
                        face = newFaces.Count > 1 ? newFaces.OrderByDescending(f => f.FaceRectangle.Width * f.FaceRectangle.Height).First() : newFaces[0];
                }
            }

            // 3. Document Quality Checks
            var attributes = face.FaceAttributes;
            var errors = new List<string>();
            if (attributes?.Blur?.Value > 0.7) errors.Add("Image is too blurry.");
            if (attributes?.Exposure != null && (attributes.Exposure.Value < 0.3 || attributes.Exposure.Value > 0.9)) errors.Add("Poor lighting.");
            if (attributes?.Occlusion != null && (attributes.Occlusion.ForeheadOccluded || attributes.Occlusion.EyeOccluded || attributes.Occlusion.MouthOccluded)) errors.Add("Face is partially covered.");

            if (errors.Any())
                return (false, null, string.Join(" ", errors), null);

            // 4. Create Person and Add Face
            var person = await _faceClient.CreatePersonAsync(_personGroupId, userName);
            await _faceClient.AddFaceFromStreamAsync(_personGroupId, person.PersonId, new MemoryStream(imageBytes));

            // 5. Training
            _ = _faceClient.TrainPersonGroupAsync(_personGroupId);

            _logger.LogInformation("Document enrollment successful for {UserName}. PersonId: {PersonId}", userName, person.PersonId);
            return (true, person.PersonId.ToString(), null, imageRotated ? imageBytes : null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enrollment failed for {UserName}", userName);
            return (false, null, ex.Message, null);
        }
    }

    private async Task<byte[]?> TryRotateImage(byte[] imageBytes, double roll)
    {
        try
        {
            using var image = SixLabors.ImageSharp.Image.Load(imageBytes);
            if (roll > 30 && roll <= 135) image.Mutate(x => x.Rotate(90));
            else if (roll < -30 && roll >= -135) image.Mutate(x => x.Rotate(-90));
            else if (Math.Abs(roll) > 135) image.Mutate(x => x.Rotate(180));
            else return null;

            using var ms = new MemoryStream();
            await image.SaveAsync(ms, new JpegEncoder());
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Orientation correction failed");
            return null;
        }
    }

    public async Task<(bool Success, User? User, double Confidence, string? Error)> VerifyIdentityAsync(Stream captureStream)
    {
        byte[] imageBytes = await StreamToByteArray(captureStream);
        try
        {
            // 1. Detect
            var faces = await _faceClient.DetectWithStreamAsync(
                new MemoryStream(imageBytes),
                returnFaceId: true,
                recognitionModel: RecognitionModel.Recognition04,
                detectionModel: DetectionModel.Detection03
            );

            if (faces == null || faces.Count == 0)
            {
                await _logService.LogFaceActivityAsync("Verification", null, false, 0, "No face detected", "NoFace", imageBytes);
                return (false, null, 0, "No face detected.");
            }

            var faceId = faces[0].FaceId;
            if (faceId == null) return (false, null, 0, "Detection failed.");

            // 2. Identify
            var identifyResults = await _faceClient.IdentifyAsync(new List<Guid> { faceId.Value }, _personGroupId);
            if (identifyResults == null || !identifyResults.Any())
            {
                _logger.LogWarning("Identify API returned null or empty results for FaceId: {FaceId}", faceId.Value);
                await _logService.LogFaceActivityAsync("Verification", null, false, 0, "System unavailable", "SystemError", imageBytes);
                return (false, null, 0, "Biometric verification system is currently unavailable.");
            }

            if (!identifyResults[0].Candidates.Any())
            {
                _logger.LogInformation("No candidates found for FaceId: {FaceId} in PersonGroup: {PersonGroupId}", faceId.Value, _personGroupId);
                await _logService.LogFaceActivityAsync("Verification", null, false, 0, "Unknown identity", "WrongUser", imageBytes);
                return (false, null, 0, "Identity not recognized. Please ensure you are registered.");
            }

            var candidate = identifyResults[0].Candidates[0];
            var confidence = candidate.Confidence;
            var settings = await _settingsService.GetSettingsAsync();
            var threshold = Math.Clamp(settings.FaceRecognitionConfidenceThreshold ?? 0.4, 0.4, 0.95);

            if (confidence < threshold)
            {
                await _logService.LogFaceActivityAsync("Verification", null, false, confidence, "Low confidence match", "LowConfidence", imageBytes);
                return (false, null, confidence, "Face not recognized with sufficient confidence.");
            }

            // 3. Match Local User
            var personId = candidate.PersonId.ToString();
            var user = await _userService.GetUserByPersonIdAsync(personId);

            if (user == null)
            {
                await _logService.LogFaceActivityAsync("Verification", null, false, confidence, "No local account found", "WrongUser", imageBytes, personId);
                return (false, null, confidence, "Face recognized but no account found.");
            }

            await _logService.LogFaceActivityAsync("Verification", user.Id, true, confidence, null, null, imageBytes, personId, user.Email);
            return (true, user, confidence, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Identity verification failure");
            var category = _logService.MapErrorToCategory(ex.Message);
            await _logService.LogFaceActivityAsync("Verification", null, false, 0, ex.Message, category, imageBytes);
            return (false, null, 0, $"Verification error: {ex.Message}");
        }
    }

    public async Task<(bool Success, User? User, double Confidence, string? Error)> VerifyIdentityFromIdAsync(Guid faceId)
    {
        try
        {
            // 1. Identify
            var identifyResults = await _faceClient.IdentifyAsync(new List<Guid> { faceId }, _personGroupId);
            if (identifyResults == null || !identifyResults.Any())
            {
                _logger.LogWarning("Identify API (from ID) returned null or empty results for FaceId: {FaceId}", faceId);
                return (false, null, 0, "Biometric verification system is currently unavailable.");
            }

            if (!identifyResults[0].Candidates.Any())
            {
                _logger.LogInformation("No candidates found for FaceId: {FaceId} in PersonGroup: {PersonGroupId}", faceId, _personGroupId);
                return (false, null, 0, "Identity not recognized.");
            }

            var candidate = identifyResults[0].Candidates[0];
            var confidence = candidate.Confidence;
            var settings = await _settingsService.GetSettingsAsync();
            var threshold = Math.Clamp(settings.FaceRecognitionConfidenceThreshold ?? 0.4, 0.4, 0.95);

            if (confidence < threshold)
            {
                return (false, null, confidence, "Face not recognized with sufficient confidence.");
            }

            // 2. Match Local User
            var personId = candidate.PersonId.ToString();
            var user = await _userService.GetUserByPersonIdAsync(personId);

            if (user == null)
            {
                await _logService.LogFaceActivityAsync("Verification", null, false, confidence, "No local account found", "WrongUser", null, personId);
                return (false, null, confidence, "Face recognized but no account found.");
            }

            await _logService.LogFaceActivityAsync("Verification", user.Id, true, confidence, null, null, null, personId, user.Email);
            return (true, user, confidence, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Identity verification from ID failure");
            return (false, null, 0, $"Verification error: {ex.Message}");
        }
    }

    public async Task<bool> DeleteIdentityAsync(string personId)
    {
        try
        {
            await _faceClient.DeletePersonAsync(_personGroupId, Guid.Parse(personId));
            _ = _faceClient.TrainPersonGroupAsync(_personGroupId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete identity {PersonId}", personId);
            return false;
        }
    }

    public async Task<List<(string PersonId, double Confidence)>> DetectDuplicatesAsync(Stream imageStream)
    {
        return await DetectDuplicatesInternal(imageStream);
    }

    private async Task<List<(string PersonId, double Confidence)>> DetectDuplicatesInternal(Stream imageStream)
    {
        try
        {
            var faces = await _faceClient.DetectWithStreamAsync(
                imageStream, 
                returnFaceId: true, 
                recognitionModel: RecognitionModel.Recognition04, 
                detectionModel: DetectionModel.Detection03);

            var faceId = faces[0].FaceId ?? Guid.Empty;
            if (faceId == Guid.Empty) return new List<(string, double)>();

            var identifyResults = await _faceClient.IdentifyAsync(new List<Guid> { faceId }, _personGroupId);
            if (identifyResults == null || !identifyResults.Any())
                return new List<(string, double)>();

            var results = new List<(string, double)>();
            foreach (var candidate in identifyResults[0].Candidates)
            {
                if (candidate.Confidence < 0.5) continue;
                
                var personId = candidate.PersonId.ToString();
                var user = await _userService.GetUserByPersonIdAsync(personId);
                
                if (user != null)
                {
                    results.Add((personId, candidate.Confidence));
                }
                else
                {
                    // Ghost cleanup
                    _logger.LogWarning("Cleaning ghost record: {PersonId}", personId);
                    _ = DeleteIdentityAsync(personId);
                }
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Duplicate detection failed");
            return new List<(string, double)>();
        }
    }

    public async Task<(bool IsValid, double Yaw, double Pitch, double Roll, Guid? FaceId, string? Error)> DetectHeadPoseAsync(Stream imageStream)
    {
        try
        {
            var faces = await _faceClient.DetectWithStreamAsync(
                imageStream,
                returnFaceAttributes: new List<FaceAttributeType> { FaceAttributeType.HeadPose },
                recognitionModel: RecognitionModel.Recognition04,
                detectionModel: DetectionModel.Detection03
            );

            if (faces == null || faces.Count == 0) return (false, 0, 0, 0, null, "No face detected.");
            if (faces.Count > 1) return (false, 0, 0, 0, null, "Multiple faces detected.");

            var pose = faces[0].FaceAttributes?.HeadPose;
            if (pose == null) return (false, 0, 0, 0, null, "Face pose attributes missing.");

            return (true, pose.Yaw, pose.Pitch, pose.Roll, faces[0].FaceId, null);
        }
        catch (Exception ex)
        {
            return (false, 0, 0, 0, null, ex.Message);
        }
    }

    private async Task<byte[]> StreamToByteArray(Stream stream)
    {
        if (stream is MemoryStream ms) return ms.ToArray();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
    }

    private (bool IsValid, string? Error) ValidateFaceAttributes(FaceAttributes? attributes)
    {
        if (attributes == null) return (true, null);
        
        if (attributes.HeadPose != null)
        {
            if (Math.Abs(attributes.HeadPose.Pitch) > 25 || Math.Abs(attributes.HeadPose.Roll) > 25 || Math.Abs(attributes.HeadPose.Yaw) > 25)
                return (false, "Face angle is too steep. Please look directly at the camera.");
        }

        if (attributes.Occlusion != null)
        {
            if (attributes.Occlusion.ForeheadOccluded || attributes.Occlusion.EyeOccluded || attributes.Occlusion.MouthOccluded)
                return (false, "Face is partially covered. Please ensure your face is fully visible.");
        }

        return (true, null);
    }

    public async Task<byte[]> OptimizeImageForStorageAsync(byte[] imageBytes, int maxWidth = 800, int maxHeight = 800, int quality = 75)
    {
        try
        {
            using var image = SixLabors.ImageSharp.Image.Load(imageBytes);
            
            // Only resize if the image is larger than the target dimensions
            if (image.Width > maxWidth || image.Height > maxHeight)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(maxWidth, maxHeight),
                    Mode = ResizeMode.Max
                }));
            }

            using var ms = new MemoryStream();
            await image.SaveAsync(ms, new JpegEncoder { Quality = quality });
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to optimize image for storage");
            return imageBytes; // Return original if optimization fails
        }
    }
}
