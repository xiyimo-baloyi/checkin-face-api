using Africanideasfaceapi.Helpers;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Models;

/// <summary>
/// Audit log entry - tracks all important actions in the system
/// </summary>
public class AuditLog
{
    [Key]
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Action type: Login, Registration, IdCardVerification, Consent, etc.
    /// </summary>
    [Required]
    public string ActionType { get; set; } = string.Empty;
    
    /// <summary>
    /// Worker/User ID who performed the action
    /// </summary>
    public string? UserId { get; set; }
    
    /// <summary>
    /// Email of the user
    /// </summary>
    public string? Email { get; set; }
    
    /// <summary>
    /// Target worker ID (if action was on another worker)
    /// </summary>
    public string? TargetWorkerId { get; set; }
    
    /// <summary>
    /// Action result: Success, Failed, Error
    /// </summary>
    public string Result { get; set; } = "Success";
    
    /// <summary>
    /// Error message if action failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Additional data in JSON format
    /// </summary>
    public string? AdditionalData { get; set; }
    
    /// <summary>
    /// IP address
    /// </summary>
    public string? IpAddress { get; set; }
    
    /// <summary>
    /// User agent
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Latitude of the action
    /// </summary>
    public double? Latitude { get; set; }

    /// <summary>
    /// Longitude of the action
    /// </summary>
    public double? Longitude { get; set; }
    
    /// <summary>
    /// Timestamp of the action
    /// </summary>
    public DateTime Timestamp { get; set; } = TimeHelper.GetNow();
}
