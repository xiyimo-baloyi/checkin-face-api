namespace Africanideasfaceapi.Services.Interfaces;

public interface IFaceLogService
{
    Task LogFaceActivityAsync(
        string action, 
        string? userId, 
        bool success, 
        double confidence, 
        string? errorMessage, 
        string? category = null, 
        byte[]? imageBytes = null, 
        string? recognizedPersonId = null, 
        string? recognizedEmail = null);
        
    string MapErrorToCategory(string? error);
}
