using Microsoft.AspNetCore.Http;

namespace Africanideasfaceapi.DTOs;

/// <summary>
/// ID card verification request DTO
/// </summary>
public class IdCardVerificationRequestDto
{
    public string WorkerId { get; set; } = string.Empty;
    public IFormFile? IdCardImage { get; set; }
    public IFormFile? LiveFaceImage { get; set; }
}

/// <summary>
/// ID card verification response DTO
/// </summary>
public class IdCardVerificationResponseDto
{
    public bool IsVerified { get; set; }
    public double Confidence { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime VerificationDate { get; set; }
}
