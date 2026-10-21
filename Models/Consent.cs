using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Models;

/// <summary>
/// Worker consent record - tracks consent for data collection and face recognition
/// </summary>
public class Consent
{
    [Key]
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Worker ID this consent belongs to
    /// </summary>
    [Required]
    public string WorkerId { get; set; } = string.Empty;
    
    /// <summary>
    /// Consent type: DataCollection, FaceRecognition, MedicalRecords, All
    /// </summary>
    [Required]
    public string ConsentType { get; set; } = "All";
    
    /// <summary>
    /// Consent status
    /// </summary>
    public bool IsConsented { get; set; }
    
    /// <summary>
    /// Date consent was given
    /// </summary>
    public DateTime ConsentDate { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Date consent was withdrawn (if applicable)
    /// </summary>
    public DateTime? WithdrawalDate { get; set; }
    
    /// <summary>
    /// IP address where consent was given
    /// </summary>
    public string? IpAddress { get; set; }
    
    /// <summary>
    /// User agent/browser information
    /// </summary>
    public string? UserAgent { get; set; }
    
    /// <summary>
    /// Additional notes about consent
    /// </summary>
    public string? Notes { get; set; }
}
