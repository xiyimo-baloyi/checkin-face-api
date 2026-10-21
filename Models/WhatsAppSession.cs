using System;
using Africanideasfaceapi.Helpers;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Models;

public class WhatsAppSession
{
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string PhoneNumber { get; set; } = string.Empty;

    public string CurrentState { get; set; } = "Welcome";

    public Dictionary<string, string> SessionData { get; set; } = new Dictionary<string, string>();

    public DateTime LastInteraction { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;
}
