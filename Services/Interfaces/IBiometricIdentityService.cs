using Africanideasfaceapi.Models;

namespace Africanideasfaceapi.Services.Interfaces;

public interface IBiometricIdentityService
{
    Task<(bool Success, string? PersonId, string? Error)> RegisterIdentityAsync(string name, string surname, Stream documentStream);
    Task<(bool Success, string? PersonId, string? Error, byte[]? RotatedBytes)> EnrollFaceFromDocumentAsync(Stream documentStream, string userName);
    Task<(bool Success, User? User, double Confidence, string? Error)> VerifyIdentityAsync(Stream captureStream);
    Task<(bool Success, User? User, double Confidence, string? Error)> VerifyIdentityFromIdAsync(Guid faceId);
    Task<bool> DeleteIdentityAsync(string personId);
    Task<List<(string PersonId, double Confidence)>> DetectDuplicatesAsync(Stream imageStream);
    Task<(bool IsValid, double Yaw, double Pitch, double Roll, Guid? FaceId, string? Error)> DetectHeadPoseAsync(Stream imageStream);
    Task<byte[]> OptimizeImageForStorageAsync(byte[] imageBytes, int maxWidth = 800, int maxHeight = 800, int quality = 75);
}
