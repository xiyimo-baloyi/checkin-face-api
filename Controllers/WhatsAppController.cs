using System.Security.Cryptography;
using System.Text;
using Africanideasfaceapi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Africanideasfaceapi.Controllers;

[ApiController]
[Route("api/whatsapp")]
public class WhatsAppController : ControllerBase
{
    private readonly WhatsAppService _whatsAppService;
    private readonly ILogger<WhatsAppController> _logger;
    private readonly IConfiguration _configuration;

    public WhatsAppController(WhatsAppService whatsAppService, ILogger<WhatsAppController> logger, IConfiguration configuration)
    {
        _whatsAppService = whatsAppService;
        _logger = logger;
        _configuration = configuration;
    }

    [AllowAnonymous]
    [HttpGet("webhook")]
    public IActionResult VerifyWebhook([FromQuery(Name = "hub.mode")] string mode,
                                       [FromQuery(Name = "hub.verify_token")] string token,
                                       [FromQuery(Name = "hub.challenge")] string challenge)
    {
        var verifyToken = _configuration["WhatsAppSettings:VerifyToken"];
        if (string.IsNullOrWhiteSpace(verifyToken))
        {
            _logger.LogWarning("WhatsApp webhook verification attempted but VerifyToken is not configured.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        
        if (!string.IsNullOrEmpty(mode) && !string.IsNullOrEmpty(token))
        {
            if (mode == "subscribe" && token == verifyToken)
            {
                _logger.LogInformation("Webhook verified successfully.");
                return Ok(challenge);
            }

            _logger.LogWarning("Webhook verification failed. Token mismatch.");
            return Forbid();
        }
        return BadRequest();
    }

    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> ReceiveMessage()
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        var appSecret = _configuration["WhatsAppSettings:AppSecret"];
        if (!string.IsNullOrWhiteSpace(appSecret))
        {
            var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
            if (!IsValidSignature(body, appSecret, signature))
            {
                _logger.LogWarning("WhatsApp webhook rejected due to invalid signature.");
                return Unauthorized();
            }
        }

        _logger.LogInformation("Received WhatsApp webhook payload ({Length} bytes).", body.Length);
        return Ok();
    }

    private static bool IsValidSignature(string payload, string appSecret, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var provided = signatureHeader["sha256=".Length..];
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provided.Trim().ToLowerInvariant()));
    }
}
