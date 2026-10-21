using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Models;

/// <summary>
/// ID Card verification record - tracks verification attempts and results for workers
/// </summary>
public class IdCardVerification
{
    [Key]
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Worker ID this verification belongs to
    /// </summary>
    [Required]
    public string WorkerId { get; set; } = string.Empty;
    
    /// <summary>
    /// ID card image path
    /// </summary>
    public string? IdCardImagePath { get; set; }
    
    /// <summary>
    /// Face image path (from live capture)
    /// </summary>
    public string? FaceImagePath { get; set; }
    
    /// <summary>
    /// Verification result - true if faces match
    /// </summary>
    public bool IsVerified { get; set; }
    
    /// <summary>
    /// Confidence score from face comparison (0-1)
    /// </summary>
    public double Confidence { get; set; }
    
    /// <summary>
    /// Verification date
    /// </summary>
    public DateTime VerificationDate { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Error message if verification failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Document type verified (SAID, Passport, etc.)
    /// </summary>
    public string? DocumentType { get; set; }
    
    /// <summary>
    /// Verification method used
    /// </summary>
    public string VerificationMethod { get; set; } = "FaceComparison";
}
