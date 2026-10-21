using Africanideasfaceapi.DTOs;
using Africanideasfaceapi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Africanideasfaceapi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class IdCardController : ControllerBase
{
    private readonly IdCardVerificationService _verificationService;
    private readonly ILogger<IdCardController> _logger;

    public IdCardController(
        IdCardVerificationService verificationService,
        ILogger<IdCardController> logger)
    {
        _verificationService = verificationService;
        _logger = logger;
    }

    /// <summary>
    /// Verify ID card by comparing face on ID card with worker's registered face
    /// </summary>
    [HttpPost("verify")]
    public async Task<ActionResult<IdCardVerificationResponseDto>> VerifyIdCard(
        [FromForm] IdCardVerificationRequestDto request)
    {
        if (request.IdCardImage == null || request.IdCardImage.Length == 0)
        {
            return BadRequest("ID card image is required");
        }

        if (string.IsNullOrEmpty(request.WorkerId))
        {
            return BadRequest("Worker ID is required");
        }

        try
        {
            using var idCardStream = request.IdCardImage.OpenReadStream();
            Stream? liveFaceStream = null;
            
            if (request.LiveFaceImage != null && request.LiveFaceImage.Length > 0)
            {
                liveFaceStream = request.LiveFaceImage.OpenReadStream();
            }

            var (isVerified, confidence, error) = await _verificationService.VerifyIdCardAsync(
                idCardStream,
                request.WorkerId,
                liveFaceStream
            );

            var response = new IdCardVerificationResponseDto
            {
                IsVerified = isVerified,
                Confidence = confidence,
                ErrorMessage = error,
                VerificationDate = DateTime.UtcNow
            };

            if (isVerified)
            {
                return Ok(response);
            }
            else
            {
                return BadRequest(response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying ID card for worker {WorkerId}", request.WorkerId);
            return StatusCode(500, new { error = "An error occurred while verifying the ID card", message = ex.Message });
        }
    }

    /// <summary>
    /// Get verification history for a worker
    /// </summary>
    [HttpGet("history/{workerId}")]
    public async Task<ActionResult> GetVerificationHistory(string workerId)
    {
        try
        {
            var history = await _verificationService.GetVerificationHistoryAsync(workerId);
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting verification history for worker {WorkerId}", workerId);
            return StatusCode(500, new { error = "An error occurred while retrieving verification history", message = ex.Message });
        }
    }
}
