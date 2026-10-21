using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Models;

public class Branch
{
    [Key]
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string Name { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    // Geofence radius in meters (default 100m)
    public double GeofenceRadius { get; set; } = 100;

    public bool IsActive { get; set; } = true;

    [JsonProperty("type")]
    public string Type { get; set; } = "Branch";
}
