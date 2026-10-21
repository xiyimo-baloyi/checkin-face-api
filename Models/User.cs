using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Models;

public class User : IPerson
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
    
    [Required]
    public string Surname { get; set; } = string.Empty;
    
    public string? IdPassportNumber { get; set; }
    
    public DateTime? DateOfBirth { get; set; }
    
    public string? Nationality { get; set; }
    
    public string? EmployeeNumber { get; set; }

    public string? DeviceId { get; set; } // Linked device identifier

    public string? BranchId { get; set; } // Assigned branch
    
    public TimeSpan? WorkStartTime { get; set; } // e.g., 08:00:00
    
    public TimeSpan? WorkEndTime { get; set; } // e.g., 17:00:00

    public bool? HasConsented { get; set; } = false;
    public DateTime? ConsentDate { get; set; }

    public string Status { get; set; } = "Pending"; // Pending, Approved
    
    // Registration Method: "Face" or "Document"
    public string RegistrationMethod { get; set; } = "Face";
    
    // For Document Registration
    public string? DocumentType { get; set; } // "SAID" (South African ID) or "Passport"
    public string? DocumentImagePath { get; set; } // Path to stored document image
    
    // Azure Face API Person ID - nullable for document-registered users until first login
    public string? PersonId { get; set; } // Azure Face API Person ID in PersonGroup (permanent identifier)
    
    public string? ImageBase64 { get; set; } // Optional: Store base64 image for admin preview (can be removed for privacy compliance)
    // Note: Azure Face API stores encrypted face vectors. We only store personId for matching.
    
    public string Role { get; set; } = "User"; // User or Admin
    
    public string? PasswordHash { get; set; }
    
    public DateTime? RegisteredDate { get; set; } = DateTime.UtcNow;
    
    public bool? IsActive { get; set; } = true;
    
    // Clock Status Tracking
    public bool? IsClockedIn { get; set; } = false;
    public DateTime? LastClockInTime { get; set; }
    public DateTime? LastClockOutTime { get; set; }
    
    public string Country { get; set; } = "South Africa";

    public string? PhoneNumber { get; set; } // E.164 format for WhatsApp

    public bool? EnforceGeofence { get; set; } = true;
    public bool? EnforceDeviceLock { get; set; } = false;

    // Flexible Work Settings
    public string WorkType { get; set; } = "On-site"; // On-site, Hybrid, Remote
    public List<string> HybridWorkDays { get; set; } = new();
    public bool? AllowAnyDevice { get; set; } = false;
}

