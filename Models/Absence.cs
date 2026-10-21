using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Models;

public class Absence
{
    [Key]
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string EmployeeId { get; set; } = string.Empty;

    public DateTime Date { get; set; }

    public string? Reason { get; set; }

    public string? AbsenceType { get; set; } // Sick, Personal, Unauthorized, Leave

    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonProperty("type")]
    public string Type { get; set; } = "Absence";
}
