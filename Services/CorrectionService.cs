using Africanideasfaceapi.Data;
using Africanideasfaceapi.Models;
using Microsoft.EntityFrameworkCore;

namespace Africanideasfaceapi.Services;

public class CorrectionService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<CorrectionService> _logger;
    private readonly AuditService _auditService;

    public CorrectionService(IDbContextFactory<AppDbContext> factory, ILogger<CorrectionService> logger, AuditService auditService)
    {
        _factory = factory;
        _logger = logger;
        _auditService = auditService;
    }

    public async Task<AttendanceCorrection> RequestCorrectionAsync(string employeeId, string attendanceId, DateTime? newIn, DateTime? newOut, string reason)
    {
        using var context = await _factory.CreateDbContextAsync();
        var request = new AttendanceCorrection
        {
            EmployeeId = employeeId,
            AttendanceId = attendanceId,
            RequestedClockInTime = newIn,
            RequestedClockOutTime = newOut,
            Reason = reason,
            Status = "Pending"
        };

        context.CorrectionRequests.Add(request);
        await context.SaveChangesAsync();
        
        _auditService.LogCorrectionRequest(employeeId, attendanceId, "Submitted");
        _logger.LogInformation("Correction request submitted by {EmployeeId} for attendance {AttendanceId}", employeeId, attendanceId);
        
        return request;
    }

    public async Task<bool> ReviewCorrectionAsync(string requestId, string status, string? comment, string adminId)
    {
        using var context = await _factory.CreateDbContextAsync();
        var request = await context.CorrectionRequests.FindAsync(requestId);
        if (request == null) return false;

        request.Status = status;
        request.AdminComment = comment;

        if (status == "Approved")
        {
            var attendance = await context.Attendance
                .FirstOrDefaultAsync(a => a.Id == request.AttendanceId);
            
            if (attendance != null)
            {
                if (request.RequestedClockInTime.HasValue) attendance.ClockInTime = request.RequestedClockInTime;
                if (request.RequestedClockOutTime.HasValue) attendance.ClockOutTime = request.RequestedClockOutTime;
                
                // Recalculate hours if both exist
                if (attendance.ClockInTime.HasValue && attendance.ClockOutTime.HasValue)
                {
                    attendance.TotalHours = (attendance.ClockOutTime.Value - attendance.ClockInTime.Value).TotalHours;
                }
            }
        }

        await context.SaveChangesAsync();
        _auditService.LogCorrectionRequest(request.EmployeeId, request.AttendanceId, $"Reviewed_{status}");
        
        return true;
    }

    public async Task<List<AttendanceCorrection>> GetEmployeeRequestsAsync(string employeeId)
    {
        using var context = await _factory.CreateDbContextAsync();
        return await context.CorrectionRequests
            .AsNoTracking()
            .Where(c => c.EmployeeId == employeeId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<AttendanceCorrection>> GetPendingRequestsAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        return await context.CorrectionRequests
            .AsNoTracking()
            .Where(c => c.Status == "Pending")
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }
}
