using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Models;

/// <summary>
/// Worker model - represents an employee in the system
/// </summary>
public class Worker : IPerson
{
    [Key]
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [Required]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    public string IdNumber { get; set; } = string.Empty;
    
    [Required]
    public string Email { get; set; } = string.Empty;
    
    // Registration Method: "Face" or "Document"
    public string RegistrationMethod { get; set; } = "Face";
    
    // For Document Registration
    public string? DocumentType { get; set; } // "SAID" (South African ID) or "Passport"
    public string? DocumentImagePath { get; set; } // Path to stored document image
    
    // Azure Face API Person ID
    public string? PersonId { get; set; } // Azure Face API Person ID in PersonGroup
    
    /// <summary>
    /// ID card verification status
    /// </summary>
    public bool IdCardVerified { get; set; } = false;
    
    /// <summary>
    /// ID card verification confidence score (0-1)
    /// </summary>
    public double? IdCardVerificationConfidence { get; set; }
    
    /// <summary>
    /// Date of last ID card verification
    /// </summary>
    public DateTime? IdCardVerificationDate { get; set; }
    
    public string ImageBase64 { get; set; } = string.Empty;
    
    public string Role { get; set; } = "User"; // User or Admin
    
    public DateTime RegisteredDate { get; set; } = DateTime.UtcNow;
    
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Consent status for data collection
    /// </summary>
    public bool HasConsent { get; set; } = false;
    
    /// <summary>
    /// Consent date
    /// </summary>
    public DateTime? ConsentDate { get; set; }

    #region IPerson explicit implementations

    // These explicit implementations provide a common nullable-based
    // contract for services without changing the existing public API
    // or persistence shape for Worker.

    DateTime? IPerson.RegisteredDate
    {
        get => RegisteredDate;
        set => RegisteredDate = value ?? RegisteredDate;
    }

    bool? IPerson.IsActive
    {
        get => IsActive;
        set => IsActive = value ?? IsActive;
    }

    bool? IPerson.HasConsented
    {
        get => HasConsent;
        set => HasConsent = value ?? HasConsent;
    }

    DateTime? IPerson.ConsentDate
    {
        get => ConsentDate;
        set => ConsentDate = value;
    }

    #endregion
}
