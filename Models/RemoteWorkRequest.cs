using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Models;

public class RemoteWorkRequest
{
    [Key]
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string UserId { get; set; } = string.Empty;

    public string? UserName { get; set; }

    [Required]
    public DateTime Date { get; set; }

    [Required]
    public string Reason { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected

    public string? AdminComment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
