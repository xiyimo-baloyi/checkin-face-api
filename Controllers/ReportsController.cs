using Africanideasfaceapi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Africanideasfaceapi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _reportService;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(ReportService reportService, ILogger<ReportsController> logger)
    {
        _reportService = reportService;
        _logger = logger;
    }

    [HttpGet("attendance/pdf")]
    public async Task<IActionResult> GetAttendancePdf([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        try
        {
            var pdfBytes = await _reportService.GenerateAttendancePdfAsync(startDate, endDate);
            var fileName = $"Attendance_Report_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating attendance PDF report");
            return StatusCode(500, "Error generating report");
        }
    }

    [HttpGet("attendance/excel")]
    public async Task<IActionResult> GetAttendanceExcel([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        try
        {
            var excelBytes = await _reportService.GenerateAttendanceExcelAsync(startDate, endDate);
            var fileName = $"Attendance_Report_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.xlsx";
            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating attendance Excel report");
            return StatusCode(500, "Error generating report");
        }
    }

    [HttpGet("audit/pdf")]
    public async Task<IActionResult> GetAuditLogPdf([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        try
        {
            var pdfBytes = await _reportService.GenerateAuditLogPdfAsync(startDate, endDate);
            var fileName = $"Audit_Log_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating audit log PDF report");
            return StatusCode(500, "Error generating report");
        }
    }

    [HttpGet("users/excel")]
    public async Task<IActionResult> GetUserDirectoryExcel()
    {
        try
        {
            var excelBytes = await _reportService.GenerateUserDirectoryExcelAsync();
            var fileName = $"User_Directory_{DateTime.Now:yyyyMMdd}.xlsx";
            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating user directory Excel report");
            return StatusCode(500, "Error generating report");
        }
    }

    [HttpGet("access/pdf")]
    public async Task<IActionResult> GetAccessLogPdf([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        try
        {
            var pdfBytes = await _reportService.GenerateAccessLogPdfAsync(startDate, endDate);
            var fileName = $"Access_Report_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating access PDF report");
            return StatusCode(500, "Error generating report");
        }
    }

    [HttpGet("access/excel")]
    public async Task<IActionResult> GetAccessLogExcel([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        try
        {
            var excelBytes = await _reportService.GenerateAccessLogExcelAsync(startDate, endDate);
            var fileName = $"Access_Report_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.xlsx";
            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating access Excel report");
            return StatusCode(500, "Error generating report");
        }
    }
}
