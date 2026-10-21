using Africanideasfaceapi.DTOs;
using Africanideasfaceapi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Africanideasfaceapi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AnalyticsController : ControllerBase
{
    private readonly AnalyticsService _analyticsService;
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(
        AnalyticsService analyticsService,
        ILogger<AnalyticsController> logger)
    {
        _analyticsService = analyticsService;
        _logger = logger;
    }

    /// <summary>
    /// Get dashboard statistics
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<AnalyticsDashboardDto>> GetDashboard()
    {
        try
        {
            var stats = await _analyticsService.GetDashboardStatsAsync();
            var trends = await _analyticsService.GetRegistrationTrendsAsync(30);

            var dashboard = new AnalyticsDashboardDto
            {
                TotalWorkers = stats.TotalWorkers,
                FaceRegistered = stats.FaceRegistered,
                DocumentRegistered = stats.DocumentRegistered,
                IdCardVerified = stats.IdCardVerified,
                WithConsent = stats.WithConsent,
                RegistrationsToday = stats.RegistrationsToday,
                RegistrationsThisWeek = stats.RegistrationsThisWeek,
                RegistrationsThisMonth = stats.RegistrationsThisMonth,
                LoginSuccessRate = stats.LoginSuccessRate,
                VerificationSuccessRate = stats.VerificationSuccessRate,
                RegistrationTrends = trends.Select(t => new RegistrationTrendDto
                {
                    Date = t.Date,
                    Count = t.Count,
                    FaceCount = t.FaceCount,
                    DocumentCount = t.DocumentCount
                }).ToList()
            };

            return Ok(dashboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard statistics");
            return StatusCode(500, new { error = "An error occurred while retrieving dashboard statistics", message = ex.Message });
        }
    }

    /// <summary>
    /// Get registration trends
    /// </summary>
    [HttpGet("trends")]
    public async Task<ActionResult<List<RegistrationTrendDto>>> GetTrends([FromQuery] int days = 30)
    {
        try
        {
            var trends = await _analyticsService.GetRegistrationTrendsAsync(days);
            var dtos = trends.Select(t => new RegistrationTrendDto
            {
                Date = t.Date,
                Count = t.Count,
                FaceCount = t.FaceCount,
                DocumentCount = t.DocumentCount
            }).ToList();

            return Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting registration trends");
            return StatusCode(500, new { error = "An error occurred while retrieving trends", message = ex.Message });
        }
    }

    /// <summary>
    /// Get ID card verification statistics
    /// </summary>
    [HttpGet("id-card-verification")]
    public async Task<ActionResult> GetIdCardVerificationStats()
    {
        try
        {
            var stats = await _analyticsService.GetIdCardVerificationStatsAsync();
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting ID card verification statistics");
            return StatusCode(500, new { error = "An error occurred while retrieving verification statistics", message = ex.Message });
        }
    }
}
