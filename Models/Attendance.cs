using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Models;

public class Attendance
{
    [Key]
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [Required]
    public string EmployeeId { get; set; } = string.Empty;
    
    public DateTime Date { get; set; }
    
    public DateTime? ClockInTime { get; set; }
    
    public DateTime? ClockOutTime { get; set; }
    
    public double? TotalHours { get; set; }
    
    public double? ShiftHours { get; set; }
    
    public double? OvertimeHours { get; set; }
    
    public string? Status { get; set; } // "Present", "Late", "Overtime", "HalfDay"
    
    public double? Latitude { get; set; }
    
    public double? Longitude { get; set; }
    
    public bool IsOutOfArea { get; set; }
    
    public string? DeviceId { get; set; }
}
