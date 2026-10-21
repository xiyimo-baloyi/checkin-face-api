using Africanideasfaceapi.Data;
using Africanideasfaceapi.Models;
using Africanideasfaceapi.Services;
using Africanideasfaceapi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Africanideasfaceapi.Models.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authorization;

namespace Africanideasfaceapi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class DiagnosticController : ControllerBase
{
    private readonly IFaceClientWrapper _faceClient;
    private readonly SettingsService _settingsService;
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<DiagnosticController> _logger;
    private readonly string _personGroupId;
    private readonly IConfiguration _configuration;

    public DiagnosticController(
        IFaceClientWrapper faceClient,
        SettingsService settingsService,
        IDbContextFactory<AppDbContext> factory,
        ILogger<DiagnosticController> logger,
        IOptions<AzureFaceApiSettings> settings,
        IConfiguration configuration)
    {
        _faceClient = faceClient;
        _settingsService = settingsService;
        _factory = factory;
        _logger = logger;
        _personGroupId = settings.Value.PersonGroupId;
        _configuration = configuration;
    }

    [HttpGet("system-state")]
    public async Task<IActionResult> GetSystemState()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            using var context = await _factory.CreateDbContextAsync();
            
            var userCount = await context.Users.CountAsync();
            var faceLogCount = await context.FaceLogs.CountAsync();
            var approvedUserCount = await context.Users.CountAsync(u => u.Status == "Approved");
            
            // Check Face API
            object faceApiStatus;
            try
            {
                var group = await _faceClient.GetPersonGroupAsync(_personGroupId);
                var trainingStatus = await _faceClient.GetPersonGroupTrainingStatusAsync(_personGroupId);
                faceApiStatus = new
                {
                    Exists = true,
                    Name = group.Name,
                    Status = trainingStatus.Status.ToString(),
                    Message = trainingStatus.Message
                };
            }
            catch (Exception ex)
            {
                faceApiStatus = new
                {
                    Exists = false,
                    Error = ex.Message
                };
            }

            return Ok(new
            {
                Timestamp = DateTime.UtcNow,
                Settings = new
                {
                    settings.GlobalDeviceLockEnforcement,
                    settings.GlobalGeofenceEnforcement,
                    settings.FaceRecognitionConfidenceThreshold,
                    settings.EnableOfflineAccess
                },
                Database = new
                {
                    UserCount = userCount,
                    ApprovedUserCount = approvedUserCount,
                    FaceLogCount = faceLogCount
                },
                FaceApi = faceApiStatus,
                Security = new
                {
                    ApiKeyConfigured = !string.IsNullOrEmpty(_configuration["ApiSettings:ApiKey"]),
                    JwtSecretConfigured = !string.IsNullOrEmpty(_configuration["JwtSettings:SecretKey"])
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    [HttpPost("trigger-training")]
    public async Task<IActionResult> TriggerTraining()
    {
        try
        {
            await _faceClient.TrainPersonGroupAsync(_personGroupId);
            return Ok(new { Message = "Training triggered successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    [HttpGet("recent-logs")]
    public async Task<IActionResult> GetRecentLogs([FromQuery] int count = 20)
    {
        using var context = await _factory.CreateDbContextAsync();
        var logs = await context.FaceLogs
            .OrderByDescending(l => l.Timestamp)
            .Take(count)
            .Select(l => new {
                l.Timestamp,
                l.Action,
                l.Success,
                l.ConfidenceScore,
                l.ErrorMessage,
                l.FailureCategory,
                l.RecognizedUserId,
                l.RecognizedUserEmail
            })
            .ToListAsync();
            
        return Ok(logs);
    }
}
