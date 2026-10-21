using Africanideasfaceapi.Models;

namespace Africanideasfaceapi.DTOs;

/// <summary>
/// Worker data transfer object for API responses
/// </summary>
public class WorkerDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string IdNumber { get; set; } = string.Empty;
    public string RegistrationMethod { get; set; } = string.Empty;
    public bool IdCardVerified { get; set; }
    public double? IdCardVerificationConfidence { get; set; }
    public DateTime RegisteredDate { get; set; }
    public bool HasConsent { get; set; }
    public bool IsActive { get; set; }

    public static WorkerDto FromWorker(Worker worker)
    {
        return new WorkerDto
        {
            Id = worker.Id,
            Name = worker.Name,
            Email = worker.Email,
            IdNumber = worker.IdNumber,
            RegistrationMethod = worker.RegistrationMethod,
            IdCardVerified = worker.IdCardVerified,
            IdCardVerificationConfidence = worker.IdCardVerificationConfidence,
            RegisteredDate = worker.RegisteredDate,
            HasConsent = worker.HasConsent,
            IsActive = worker.IsActive
        };
    }
}

/// <summary>
/// Create worker request DTO
/// </summary>
public class CreateWorkerDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string IdNumber { get; set; } = string.Empty;
    public string RegistrationMethod { get; set; } = "Face";
    public string? DocumentType { get; set; }
}
