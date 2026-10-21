using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Models;

public class SystemSettings
{
    [Key]
    [JsonProperty("id")]
    public string Id { get; set; } = "1"; // Singleton like approach for global settings

    [JsonProperty("standardStartTime")]
    public TimeOnly? StandardStartTime { get; set; } = new TimeOnly(9, 0);

    [JsonProperty("standardEndTime")]
    public TimeOnly? StandardEndTime { get; set; } = new TimeOnly(17, 0);

    [JsonProperty("lateThresholdMinutes")]
    public int? LateThresholdMinutes { get; set; } = 15;

    [JsonProperty("publicHolidays")]
    public List<PublicHoliday> PublicHolidays { get; set; } = new();

    public string DefaultTimeZone { get; set; } = "South Africa Standard Time";
    
    // Leave Policy Defaults (BCEA etc)
    public double? DefaultAnnualLeaveDays { get; set; } = 15;
    public double? DefaultSickLeaveDays { get; set; } = 30;
    public double? DefaultFamilyLeaveDays { get; set; } = 3;
    public double? DefaultParentalLeaveDays { get; set; } = 10;
    public double? DefaultMaternityLeaveDays { get; set; } = 120; // ~4 months
    public double? DefaultStudyLeaveDays { get; set; } = 5;
    public double? DefaultOutLeaveDays { get; set; } = 5;

    public bool? GlobalGeofenceEnforcement { get; set; } = true;
    public bool? GlobalDeviceLockEnforcement { get; set; } = false;

    /// <summary>Face recognition minimum confidence (0.0–1.0). Default 0.6 (60%). Lower = more lenient.</summary>
    public double? FaceRecognitionConfidenceThreshold { get; set; } = 0.4;

    public double? StandardShiftHours { get; set; } = 8.0;
    public double? AutoClockOutThreshold { get; set; } = 10.0;

    // System Capabilities
    [JsonProperty("enableOfflineAccess")]
    public bool? EnableOfflineAccess { get; set; } = true;
}

public class PublicHoliday
{
    public DateTime Date { get; set; }
    public string Name { get; set; } = string.Empty;
}
