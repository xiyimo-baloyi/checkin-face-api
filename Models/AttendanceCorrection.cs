using Africanideasfaceapi.Helpers;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Models;

public class AttendanceCorrection
{
    [Key]
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string AttendanceId { get; set; } = string.Empty;

    [Required]
    public string EmployeeId { get; set; } = string.Empty;

    public DateTime? RequestedClockInTime { get; set; }

    public DateTime? RequestedClockOutTime { get; set; }

    [Required]
    public string Reason { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected

    public string? AdminComment { get; set; }

    public DateTime CreatedAt { get; set; } = TimeHelper.GetNow();

    [JsonProperty("type")]
    public string DataType { get; set; } = "CorrectionRequest";
}
