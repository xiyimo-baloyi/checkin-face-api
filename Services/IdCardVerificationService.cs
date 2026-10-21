using Africanideasfaceapi.Data;
using Africanideasfaceapi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Africanideasfaceapi.Services.Interfaces;
using Africanideasfaceapi.Models.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;

namespace Africanideasfaceapi.Services;

/// <summary>
/// Service for verifying ID cards by comparing face on ID card with registered patient face
/// </summary>
public class IdCardVerificationService
{
    private readonly IFaceClientWrapper _faceClient;
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<IdCardVerificationService> _logger;
    private readonly FileStorageService _fileStorageService;
    private readonly EmailService _emailService;
    private readonly string _personGroupId;

    public IdCardVerificationService(
        IFaceClientWrapper faceClient,
        IOptions<AzureFaceApiSettings> settings,
        IDbContextFactory<AppDbContext> factory,
        ILogger<IdCardVerificationService> logger,
        FileStorageService fileStorageService,
        EmailService emailService)
    {
        _faceClient = faceClient;
        _personGroupId = settings.Value.PersonGroupId;
        _factory = factory;
        _logger = logger;
        _fileStorageService = fileStorageService;
        _emailService = emailService;
    }


    /// <summary>
    /// Verify ID card by comparing face on ID card with worker's registered face
    /// </summary>
    public async Task<(bool IsVerified, double Confidence, string? Error)> VerifyIdCardAsync(
        Stream idCardImageStream,
        string workerId,
        Stream? liveFaceImageStream = null)
    {
        try
        {
            using var context = await _factory.CreateDbContextAsync();
            _logger.LogInformation("Starting ID card verification for worker: {WorkerId}", workerId);

            // Get worker from database
            Worker? worker = await context.Workers
                .OrderByDescending(p => p.RegisteredDate)
                .FirstOrDefaultAsync(p => p.Id == workerId);
            if (worker == null)
            {
                // Fallback to Users table for backward compatibility
                var user = await context.Users
                    .OrderByDescending(u => u.RegisteredDate)
                    .FirstOrDefaultAsync(u => u.Id == workerId);
                if (user == null)
                {
                    return (false, 0, "Worker not found");
                }
                // Convert User to Worker for processing
                worker = new Worker
                {
                    Id = user.Id,
                    Name = user.Name,
                    Email = user.Email,
                    IdNumber = user.IdNumber,
                    PersonId = user.PersonId,
                    DocumentType = user.DocumentType,
                    RegistrationMethod = user.RegistrationMethod,
                    IdCardVerified = false
                };
            }

            // Extract face from ID card
            idCardImageStream.Position = 0;
            var idCardFaces = await _faceClient.DetectWithStreamAsync(
                idCardImageStream,
                recognitionModel: RecognitionModel.Recognition04,
                detectionModel: DetectionModel.Detection03,
                returnRecognitionModel: true
            );

            if (idCardFaces == null || idCardFaces.Count == 0)
            {
                return (false, 0, "No face detected on ID card. Please ensure the ID card image is clear and the face is visible.");
            }

            if (idCardFaces.Count > 1)
            {
                return (false, 0, "Multiple faces detected on ID card. Please use an ID card with only one face.");
            }

            var idCardFaceId = idCardFaces[0].FaceId;
            if (idCardFaceId == null || !idCardFaceId.HasValue)
            {
                return (false, 0, "Failed to extract face from ID card.");
            }

            // If worker has PersonId, compare with registered face
            if (!string.IsNullOrEmpty(worker.PersonId))
            {
                // Get person's faces from Azure Face API
                var person = await _faceClient.GetFacePersonAsync(_personGroupId, Guid.Parse(worker.PersonId));
                
                if (person == null || person.PersistedFaceIds == null || !person.PersistedFaceIds.Any())
                {
                    return (false, 0, "Worker has no registered face. Please complete face registration first.");
                }

                // Compare ID card face with all registered faces
                double maxConfidence = 0;
                foreach (var persistedFaceId in person.PersistedFaceIds)
                {
                    var verifyResult = await _faceClient.VerifyFaceToFaceAsync(
                        idCardFaceId.GetValueOrDefault(),
                        persistedFaceId
                    );

                    if (verifyResult.Confidence > maxConfidence)
                    {
                        maxConfidence = verifyResult.Confidence;
                    }
                }

                // Threshold for verification (0.7 = 70%)
                var isVerified = maxConfidence >= 0.7;
                
                // Save verification record
                var verification = new IdCardVerification
                {
                    WorkerId = workerId,
                    IsVerified = isVerified,
                    Confidence = maxConfidence,
                    VerificationDate = DateTime.UtcNow,
                    DocumentType = worker.DocumentType,
                    VerificationMethod = "FaceComparison"
                };

                if (isVerified)
                {
                    // Update worker if it exists in Workers table
                    var existingWorker = await context.Workers
                        .OrderByDescending(p => p.RegisteredDate)
                        .FirstOrDefaultAsync(p => p.Id == workerId);
                    if (existingWorker != null)
                    {
                        existingWorker.IdCardVerified = true;
                        existingWorker.IdCardVerificationConfidence = maxConfidence;
                        existingWorker.IdCardVerificationDate = DateTime.UtcNow;
                    }
                }
                else
                {
                    verification.ErrorMessage = $"Face comparison confidence ({maxConfidence:P2}) below threshold (70%)";
                }

                context.IdCardVerifications.Add(verification);
                await context.SaveChangesAsync();

                _logger.LogInformation("ID card verification completed for worker {WorkerId}: Verified={Verified}, Confidence={Confidence}",
                    workerId, isVerified, maxConfidence);

                // Send verification update email
                try
                {
                    await _emailService.SendVerificationUpdateEmailAsync(worker.Email, worker.Name, isVerified, isVerified ? null : verification.ErrorMessage);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send verification update email to {Email}", worker.Email);
                }

                return (isVerified, maxConfidence, isVerified ? null : $"Face match confidence ({maxConfidence:P2}) is below the required threshold.");
            }
            else if (liveFaceImageStream != null)
            {
                // Worker doesn't have registered face yet - compare ID card with live capture
                liveFaceImageStream.Position = 0;
                var liveFaces = await _faceClient.DetectWithStreamAsync(
                    liveFaceImageStream,
                    recognitionModel: RecognitionModel.Recognition04,
                    detectionModel: DetectionModel.Detection03,
                    returnRecognitionModel: true
                );

                if (liveFaces == null || liveFaces.Count == 0)
                {
                    return (false, 0, "No face detected in live capture.");
                }

                if (liveFaces.Count > 1)
                {
                    return (false, 0, "Multiple faces detected in live capture.");
                }

                var liveFaceId = liveFaces[0].FaceId;
                if (liveFaceId == null || !liveFaceId.HasValue)
                {
                    return (false, 0, "Failed to extract face from live capture.");
                }

                // Compare ID card face with live capture face
                var verifyResult = await _faceClient.VerifyFaceToFaceAsync(
                    idCardFaceId.GetValueOrDefault(),
                    liveFaceId.GetValueOrDefault()
                );

                var isVerified = verifyResult.Confidence >= 0.7;

                // Save verification record
                var verification = new IdCardVerification
                {
                    WorkerId = workerId,
                    IsVerified = isVerified,
                    Confidence = verifyResult.Confidence,
                    VerificationDate = DateTime.UtcNow,
                    DocumentType = worker.DocumentType,
                    VerificationMethod = "LiveCaptureComparison"
                };

                if (isVerified)
                {
                    // Update worker if it exists in Workers table
                    var existingWorker = await context.Workers
                        .OrderByDescending(p => p.RegisteredDate)
                        .FirstOrDefaultAsync(p => p.Id == workerId);
                    if (existingWorker != null)
                    {
                        existingWorker.IdCardVerified = true;
                        existingWorker.IdCardVerificationConfidence = verifyResult.Confidence;
                        existingWorker.IdCardVerificationDate = DateTime.UtcNow;
                    }
                }
                else
                {
                    verification.ErrorMessage = $"Face comparison confidence ({verifyResult.Confidence:P2}) below threshold (70%)";
                }

                context.IdCardVerifications.Add(verification);
                await context.SaveChangesAsync();

                return (isVerified, verifyResult.Confidence, isVerified ? null : $"Face match confidence ({verifyResult.Confidence:P2}) is below the required threshold.");
            }
            else
            {
                return (false, 0, "Worker has no registered face and no live capture provided. Please provide a live face capture for comparison.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during ID card verification for worker {WorkerId}", workerId);
            return (false, 0, $"Error verifying ID card: {ex.Message}");
        }
    }

    /// <summary>
    /// Get verification history for a worker
    /// </summary>
    public async Task<List<IdCardVerification>> GetVerificationHistoryAsync(string workerId)
    {
        using var context = await _factory.CreateDbContextAsync();
        return await context.IdCardVerifications
            .Where(v => v.WorkerId == workerId)
            .OrderByDescending(v => v.VerificationDate)
            .ToListAsync();
    }
}
