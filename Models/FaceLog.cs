using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Models;

public class FaceLog
{
    [Key]
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [Required]
    public string EmployeeId { get; set; } = string.Empty;
    
    [Required]
    public string Action { get; set; } = string.Empty; // "ClockIn" or "ClockOut"
    
    public double ConfidenceScore { get; set; }
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    public bool Success { get; set; }
    
    public string? ErrorMessage { get; set; }

    public string? FailureCategory { get; set; } // "LowConfidence", "NoFace", "WrongUser", "MultipleFaces", "Lighting", "Pose"
    
    public string? FaceImagePath { get; set; } // Path to the captured face image
    
    public string? RecognizedUserId { get; set; } // ID of user recognized by Azure (even if failed login)
    
    public string? RecognizedUserEmail { get; set; }
    
    public double? ProcessingTimeMs { get; set; }
}
