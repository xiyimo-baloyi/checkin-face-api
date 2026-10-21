using Africanideasfaceapi.Data;
using Africanideasfaceapi.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace Africanideasfaceapi.Services;

/// <summary>
/// Service for exporting data in various formats (CSV, JSON, PDF)
/// </summary>
public class ExportService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<ExportService> _logger;

    public ExportService(IDbContextFactory<AppDbContext> factory, ILogger<ExportService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <summary>
    /// Export workers to CSV
    /// </summary>
    public async Task<byte[]> ExportWorkersToCsvAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        var workers = await context.Workers
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();

        var csv = new StringBuilder();
        csv.AppendLine("Id,Name,Email,IdNumber,RegistrationMethod,IdCardVerified,RegisteredDate,HasConsent");

        foreach (var worker in workers)
        {
            csv.AppendLine($"{worker.Id},{EscapeCsv(worker.Name)},{EscapeCsv(worker.Email)},{EscapeCsv(worker.IdNumber)},{worker.RegistrationMethod},{worker.IdCardVerified},{worker.RegisteredDate:yyyy-MM-dd HH:mm:ss},{worker.HasConsent}");
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    /// <summary>
    /// Export workers to JSON
    /// </summary>
    public async Task<byte[]> ExportWorkersToJsonAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        var workers = await context.Workers
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Email,
                p.IdNumber,
                p.RegistrationMethod,
                p.IdCardVerified,
                p.IdCardVerificationConfidence,
                p.RegisteredDate,
                p.HasConsent,
                p.ConsentDate
            })
            .ToListAsync();

        var json = JsonSerializer.Serialize(workers, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Export audit logs to CSV with enhanced security details
    /// </summary>
    public async Task<byte[]> ExportAuditLogsToCsvAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        using var context = await _factory.CreateDbContextAsync();
        var query = context.AuditLogs.AsNoTracking().AsQueryable();

        if (startDate.HasValue)
        {
            query = query.Where(a => a.Timestamp >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(a => a.Timestamp <= endDate.Value);
        }

        var logs = await query
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync();

        var csv = new StringBuilder();
        // Enhanced CSV header with security-relevant fields
        csv.AppendLine("Timestamp,EventID,ActionType,UserId,UserEmail,TargetUserId,Result,ErrorDetails,IPAddress,UserAgent,Severity");

        foreach (var log in logs)
        {
            var severity = DetermineSeverity(log);
            csv.AppendLine($"{log.Timestamp:yyyy-MM-dd HH:mm:ss.fff},{EscapeCsv(log.Id)},{EscapeCsv(log.ActionType)},{EscapeCsv(log.UserId ?? "")},{EscapeCsv(log.Email ?? "")},{EscapeCsv(log.TargetWorkerId ?? "")},{EscapeCsv(log.Result)},{EscapeCsv(log.ErrorMessage ?? "")},{EscapeCsv(log.IpAddress ?? "")},{EscapeCsv(log.UserAgent ?? "")},{severity}");
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    /// <summary>
    /// Export audit logs to JSON with enhanced metadata
    /// </summary>
    public async Task<byte[]> ExportAuditLogsToJsonAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        using var context = await _factory.CreateDbContextAsync();
        var query = context.AuditLogs.AsNoTracking().AsQueryable();

        if (startDate.HasValue)
        {
            query = query.Where(a => a.Timestamp >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(a => a.Timestamp <= endDate.Value);
        }

        var logs = await query
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync();

        // Enhanced audit export with metadata
        var exportData = new
        {
            ExportMetadata = new
            {
                GeneratedAt = DateTime.UtcNow,
                StartDate = startDate,
                EndDate = endDate,
                TotalRecords = logs.Count,
                ExportedBy = "System Administrator",
                ComplianceVersion = "POPIA-2021"
            },
            Summary = new
            {
                TotalEvents = logs.Count,
                SuccessfulActions = logs.Count(l => l.Result == "Success"),
                FailedActions = logs.Count(l => l.Result == "Failed"),
                UniqueUsers = logs.Where(l => !string.IsNullOrEmpty(l.UserId)).Select(l => l.UserId).Distinct().Count(),
                UniqueIPAddresses = logs.Where(l => !string.IsNullOrEmpty(l.IpAddress)).Select(l => l.IpAddress).Distinct().Count(),
                ActionTypes = logs.GroupBy(l => l.ActionType).Select(g => new { Action = g.Key, Count = g.Count() }).ToList(),
                SecurityAlerts = logs.Count(l => l.Result == "Failed" && (l.ActionType == "Login" || l.ActionType == "AdminAction"))
            },
            AuditLogs = logs.Select(a => new
            {
                a.Id,
                a.ActionType,
                a.UserId,
                a.Email,
                a.TargetWorkerId,
                a.Result,
                ErrorMessage = a.ErrorMessage ?? "",
                a.Timestamp,
                IpAddress = a.IpAddress ?? "",
                UserAgent = a.UserAgent ?? "",
                Severity = DetermineSeverity(a),
                IsSecurityEvent = IsSecurityEvent(a)
            }).ToList()
        };

        var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Determine severity level for audit log entry
    /// </summary>
    private string DetermineSeverity(AuditLog log)
    {
        if (log.Result == "Failed")
        {
            if (log.ActionType == "Login") return "HIGH";
            if (log.ActionType == "AdminAction") return "CRITICAL";
            return "MEDIUM";
        }
        
        if (log.ActionType == "AdminAction" || log.ActionType == "DeleteUser" || log.ActionType == "ApproveUser")
            return "MEDIUM";
        
        return "LOW";
    }

    /// <summary>
    /// Determine if log entry is a security event
    /// </summary>
    private bool IsSecurityEvent(AuditLog log)
    {
        return log.Result == "Failed" || 
               log.ActionType == "AdminAction" || 
               log.ActionType == "DeleteUser" ||
               log.ActionType == "ApproveUser" ||
               log.ActionType == "UpdateUser";
    }

    /// <summary>
    /// Export analytics report to JSON
    /// </summary>
    public async Task<byte[]> ExportAnalyticsToJsonAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        var totalWorkers = await context.Workers.CountAsync(p => p.IsActive);
        var faceRegistered = await context.Workers.CountAsync(p => p.IsActive && p.RegistrationMethod == "Face");
        var documentRegistered = await context.Workers.CountAsync(p => p.IsActive && p.RegistrationMethod == "Document");
        var idCardVerified = await context.Workers.CountAsync(p => p.IsActive && p.IdCardVerified);
        var withConsent = await context.Workers.CountAsync(p => p.IsActive && p.HasConsent);

        var registrationsByDate = await context.Workers
            .AsNoTracking()
            .Where(p => p.IsActive)
            .GroupBy(p => p.RegisteredDate.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .OrderBy(x => x.Date)
            .ToListAsync();

        var report = new
        {
            GeneratedAt = DateTime.UtcNow,
            Summary = new
            {
                TotalWorkers = totalWorkers,
                FaceRegistered = faceRegistered,
                DocumentRegistered = documentRegistered,
                IdCardVerified = idCardVerified,
                WithConsent = withConsent
            },
            RegistrationsByDate = registrationsByDate
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Export attendance logs to CSV (Payroll Ready)
    /// </summary>
    public async Task<byte[]> ExportAttendanceToCsvAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        using var context = await _factory.CreateDbContextAsync();
        var query = context.Attendance.AsNoTracking().AsQueryable();

        if (startDate.HasValue)
            query = query.Where(a => a.Date >= startDate.Value);
        
        if (endDate.HasValue)
            query = query.Where(a => a.Date <= endDate.Value);

        var attendanceData = await query
            .OrderByDescending(a => a.Date)
            .ToListAsync();

        var users = await context.Users.AsNoTracking().ToDictionaryAsync(u => u.Id);

        var csv = new StringBuilder();
        csv.AppendLine("Date,EmployeeName,EmployeeId,ClockIn,ClockOut,TotalHours,Status,IsOutOfArea,DeviceId");

        foreach (var record in attendanceData)
        {
            var userName = users.ContainsKey(record.EmployeeId) ? users[record.EmployeeId].Name : "Unknown";
            csv.AppendLine($"{record.Date:yyyy-MM-dd},{EscapeCsv(userName)},{record.EmployeeId},{record.ClockInTime:HH:mm:ss},{record.ClockOutTime:HH:mm:ss},{record.TotalHours:F2},{record.Status},{record.IsOutOfArea},{record.DeviceId}");
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    /// <summary>
    /// Export attendance logs to JSON
    /// </summary>
    public async Task<byte[]> ExportAttendanceToJsonAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        using var context = await _factory.CreateDbContextAsync();
        var query = context.Attendance.AsNoTracking().AsQueryable();

        if (startDate.HasValue)
            query = query.Where(a => a.Date >= startDate.Value);
        
        if (endDate.HasValue)
            query = query.Where(a => a.Date <= endDate.Value);

        var logs = await query
            .OrderByDescending(a => a.Date)
            .ToListAsync();

        var json = JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true });
        return Encoding.UTF8.GetBytes(json);
    }

    private string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}
