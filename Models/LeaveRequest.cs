using Africanideasfaceapi.Helpers;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Models;

public class LeaveRequest
{
    [Key]
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string EmployeeId { get; set; } = string.Empty;
    
    public string? EmployeeName { get; set; }

    [Required]
    public DateTime StartDate { get; set; }

    [Required]
    public DateTime EndDate { get; set; }

    [Required]
    public string LeaveType { get; set; } = "Vacation"; // Vacation, Sick, Personal, Family, Study, Unpaid

    public string Reason { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected, Cancelled

    public string? AdminComment { get; set; }
    
    public string? ReviewedBy { get; set; } // Admin/Manager who reviewed
    
    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; } = TimeHelper.GetNow();
    
    // Supporting documents (optional)
    public string? AttachmentPath { get; set; }
    
    // Total duration in days (excluding non-working days)
    public int DurationDays { get; set; }
    
    // Email notification tracking
    public bool NotificationSent { get; set; } = false;
    public bool ApprovalNotificationSent { get; set; } = false;
}
