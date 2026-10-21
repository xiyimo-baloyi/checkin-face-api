using System.ComponentModel.DataAnnotations;

namespace Africanideasfaceapi.DTOs;

/// <summary>
/// Request payload for multi-frame face login.
/// Each entry in <see cref="FramesBase64"/> should be either a raw base64 string
/// or a data URL (e.g. "data:image/jpeg;base64,...").
/// </summary>
public class MultiFrameLoginRequest
{
    [Required]
    public List<string> FramesBase64 { get; set; } = new();

    /// <summary>
    /// One-time token from POST /api/auth/liveness/session after client completes liveness checks.
    /// </summary>
    public string? LivenessToken { get; set; }
}

