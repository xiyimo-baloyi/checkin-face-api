using Africanideasfaceapi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Africanideasfaceapi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class ExportController : ControllerBase
{
    private readonly ExportService _exportService;
    private readonly ILogger<ExportController> _logger;

    public ExportController(
        ExportService exportService,
        ILogger<ExportController> logger)
    {
        _exportService = exportService;
        _logger = logger;
    }

    /// <summary>
    /// Export workers to CSV or JSON
    /// </summary>
    [HttpGet("workers")]
    public async Task<IActionResult> ExportWorkers([FromQuery] string format = "csv")
    {
        try
        {
            byte[] data;
            string contentType;
            string fileName;

            if (format.ToLower() == "json")
            {
                data = await _exportService.ExportWorkersToJsonAsync();
                contentType = "application/json";
                fileName = $"workers_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
            }
            else
            {
                data = await _exportService.ExportWorkersToCsvAsync();
                contentType = "text/csv";
                fileName = $"workers_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            }

            return File(data, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting workers");
            return StatusCode(500, new { error = "An error occurred while exporting workers", message = ex.Message });
        }
    }

    /// <summary>
    /// Export audit logs to CSV or JSON
    /// </summary>
    [HttpGet("audit-logs")]
    public async Task<IActionResult> ExportAuditLogs(
        [FromQuery] string format = "csv",
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        try
        {
            byte[] data;
            string contentType;
            string fileName;

            if (format.ToLower() == "json")
            {
                data = await _exportService.ExportAuditLogsToJsonAsync(startDate, endDate);
                contentType = "application/json";
                fileName = $"audit-logs_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
            }
            else
            {
                data = await _exportService.ExportAuditLogsToCsvAsync(startDate, endDate);
                contentType = "text/csv";
                fileName = $"audit-logs_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            }

            return File(data, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting audit logs");
            return StatusCode(500, new { error = "An error occurred while exporting audit logs", message = ex.Message });
        }
    }

    /// <summary>
    /// Export analytics report to JSON
    /// </summary>
    [HttpGet("analytics-report")]
    public async Task<IActionResult> ExportAnalyticsReport([FromQuery] string format = "json")
    {
        try
        {
            var data = await _exportService.ExportAnalyticsToJsonAsync();
            var contentType = "application/json";
            var fileName = $"analytics-report_{DateTime.UtcNow:yyyyMMddHHmmss}.json";

            return File(data, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting analytics report");
            return StatusCode(500, new { error = "An error occurred while exporting analytics report", message = ex.Message });
        }
    }

    /// <summary>
    /// Export attendance logs to CSV or JSON (Payroll Ready)
    /// </summary>
    [HttpGet("attendance")]
    public async Task<IActionResult> ExportAttendance(
        [FromQuery] string format = "csv",
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        try
        {
            byte[] data;
            string contentType;
            string fileName;

            if (format.ToLower() == "json")
            {
                data = await _exportService.ExportAttendanceToJsonAsync(startDate, endDate);
                contentType = "application/json";
                fileName = $"attendance_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
            }
            else
            {
                data = await _exportService.ExportAttendanceToCsvAsync(startDate, endDate);
                contentType = "text/csv";
                fileName = $"attendance_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            }

            return File(data, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting attendance logs");
            return StatusCode(500, new { error = "An error occurred while exporting attendance logs", message = ex.Message });
        }
    }
}
