using Africanideasfaceapi.Helpers;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Models;

public class Notification
{
    [Key]
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string RecipientId { get; set; } = string.Empty;

    public string? RecipientEmail { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Message { get; set; } = string.Empty;

    public string Type { get; set; } = "Alert"; // LateArrival, MissedClockOut, LowConfidence, SecurityAlert

    public bool IsRead { get; set; } = false;

    public DateTime SentAt { get; set; } = TimeHelper.GetNow();

    [JsonProperty("type")]
    public string DataType { get; set; } = "Notification";
}
