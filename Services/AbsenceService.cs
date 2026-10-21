using Africanideasfaceapi.Services.Interfaces;
using Africanideasfaceapi.Data;
using Africanideasfaceapi.Models;
using Microsoft.EntityFrameworkCore;

namespace Africanideasfaceapi.Services;

public class AbsenceService : IAbsenceService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly HolidayService _holidayService;
    private readonly LeaveService _leaveService;
    private readonly ILogger<AbsenceService> _logger;

    public AbsenceService(IDbContextFactory<AppDbContext> factory, HolidayService holidayService, LeaveService leaveService, ILogger<AbsenceService> logger)
    {
        _factory = factory;
        _holidayService = holidayService;
        _leaveService = leaveService;
        _logger = logger;
    }

    /// <summary>
    /// Identifies users who have not clocked in for a specific date and creates pending absence records.
    /// Skips weekends and South African public holidays.
    /// </summary>
    public async Task<int> RunAbsenceDetectionAsync(DateTime date)
    {
        using var context = await _factory.CreateDbContextAsync();
        var targetDate = date.Date;
        
        // Skip if not a workday
        if (!_holidayService.IsWorkDay(targetDate))
        {
            _logger.LogInformation("Absence detection for {Date} skipped: It is a weekend or public holiday.", targetDate.ToShortDateString());
            return 0;
        }
        
        // 1. Get all active employees (excluding Admins)
        var employees = await context.Users
            .Where(u => u.IsActive == true && u.Role == "User" && u.Status == "Approved")
            .ToListAsync();

        var nextDay = targetDate.AddDays(1);
        
        // 2. Get all attendance records for that day
        var attendances = await context.Attendance
            .Where(a => a.Date >= targetDate && a.Date < nextDay)
            .Select(a => a.EmployeeId)
            .ToListAsync();

        // 3. Get existing absence records for that day to avoid duplicates
        var existingAbsences = await context.Absences
            .Where(a => a.Date >= targetDate && a.Date < nextDay)
            .Select(a => a.EmployeeId)
            .ToListAsync();

        var absentEmployees = employees
            .Where(e => !attendances.Contains(e.Id) && !existingAbsences.Contains(e.Id))
            .ToList();

        int createdCount = 0;
        foreach (var employee in absentEmployees)
        {
            // NEW: Skip if employee is on approved leave
            if (await _leaveService.IsOnApprovedLeaveAsync(employee.Id, targetDate))
            {
                _logger.LogInformation("Employee {EmployeeId} is on approved leave for {Date}. Skipping absence record.", employee.Id, targetDate.ToShortDateString());
                continue;
            }

            var absence = new Absence
            {
                EmployeeId = employee.Id,
                Date = targetDate,
                Status = "Pending",
                AbsenceType = "Unexplained"
            };
            context.Absences.Add(absence);
            createdCount++;
        }

        if (createdCount > 0)
        {
            await context.SaveChangesAsync();
            _logger.LogInformation("Absence detection for {Date}: Created {Count} records.", targetDate.ToShortDateString(), createdCount);
        }

        return createdCount;
    }

    public async Task<bool> SubmitReasonAsync(string absenceId, string reason, string type)
    {
        using var context = await _factory.CreateDbContextAsync();
        var absence = await context.Absences.FindAsync(absenceId);
        if (absence == null) return false;

        absence.Reason = reason;
        absence.AbsenceType = type;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ApproveAbsenceAsync(string absenceId)
    {
        using var context = await _factory.CreateDbContextAsync();
        var absence = await context.Absences.FindAsync(absenceId);
        if (absence == null) return false;

        if (absence.Status == "Approved") return true; // Already approved

        absence.Status = "Approved";

        // Deduct from leave balance if applicable
        if (!string.IsNullOrEmpty(absence.AbsenceType))
        {
            var leaveType = MapAbsenceToLeaveType(absence.AbsenceType);
            await _leaveService.DeductFromBalanceAsync(context, absence.EmployeeId, leaveType, 1);
        }

        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RejectAbsenceAsync(string absenceId)
    {
        using var context = await _factory.CreateDbContextAsync();
        var absence = await context.Absences.FindAsync(absenceId);
        if (absence == null) return false;

        var previousStatus = absence.Status;
        absence.Status = "Rejected";

        // Revert leave balance if it was previously approved
        if (previousStatus == "Approved" && !string.IsNullOrEmpty(absence.AbsenceType))
        {
            var leaveType = MapAbsenceToLeaveType(absence.AbsenceType);
            await _leaveService.RevertToBalanceAsync(context, absence.EmployeeId, leaveType, 1);
        }

        await context.SaveChangesAsync();
        return true;
    }

    private string MapAbsenceToLeaveType(string? absenceType)
    {
        return absenceType switch
        {
            "Sick" => "Sick",
            "Family" => "Family",
            "Out" => "Out",
            "Training" => "Study",
            "Personal" => "Unpaid",
            "Other" => "Unpaid",
            "Leave" => "Vacation",
            _ => "Unpaid"
        };
    }

    public async Task<List<Absence>> GetEmployeeAbsencesAsync(string employeeId)
    {
        using var context = await _factory.CreateDbContextAsync();
        var records = await context.Absences
            .AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .ToListAsync();
            
        return records
            .OrderByDescending(a => a.Date)
            .ToList();
    }

    public async Task<List<Absence>> GetPendingAbsencesAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        var records = await context.Absences
            .AsNoTracking()
            .Where(a => a.Status == "Pending")
            .ToListAsync();
            
        return records
            .OrderByDescending(a => a.Date)
            .ToList();
    }

    public async Task<List<Absence>> GetAllAbsencesAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        var records = await context.Absences
            .AsNoTracking()
            .ToListAsync();
            
        return records
            .OrderByDescending(a => a.Date)
            .ToList();
    }

    public async Task<string> GenerateDailySummaryAsync(DateTime date)
    {
        using var context = await _factory.CreateDbContextAsync();
        var targetDate = date.Date;
        var nextDay = targetDate.AddDays(1);
        var totalEmployees = await context.Users.CountAsync(u => u.Role == "User" && u.IsActive == true);
        var presentCount = await context.Attendance.CountAsync(a => a.Date >= targetDate && a.Date < nextDay);
        var absenceCount = await context.Absences.CountAsync(a => a.Date >= targetDate && a.Date < nextDay);
        var outOfAreaCount = await context.Attendance.CountAsync(a => a.Date >= targetDate && a.Date < nextDay && a.IsOutOfArea);

        var summary = $@"
Daily Attendance Summary for {targetDate.ToShortDateString()}:
-----------------------------------------------
Total Employees: {totalEmployees}
Present: {presentCount}
Absent (Pending Review): {absenceCount}
Out-of-Area Clock-ins: {outOfAreaCount}
Attendance Rate: {(totalEmployees > 0 ? (presentCount * 100.0 / totalEmployees) : 0):F1}%
";
        return summary;
    }
}
