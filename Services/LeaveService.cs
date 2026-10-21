using Africanideasfaceapi.Helpers;
using Africanideasfaceapi.Models;
using Africanideasfaceapi.Data;
using Microsoft.EntityFrameworkCore;

namespace Africanideasfaceapi.Services;

public class LeaveService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly EmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly SettingsService _settingsService;
    private readonly HolidayService _holidayService; // Added HolidayService
    private readonly ILogger<LeaveService> _logger;
    private readonly WhatsAppService? _whatsAppService;

    public LeaveService(IDbContextFactory<AppDbContext> factory, EmailService emailService, IConfiguration configuration, SettingsService settingsService, HolidayService holidayService, ILogger<LeaveService> logger, WhatsAppService? whatsAppService = null)
    {
        _factory = factory;
        _emailService = emailService;
        _configuration = configuration;
        _settingsService = settingsService;
        _holidayService = holidayService; // Initialized HolidayService
        _logger = logger;
        _whatsAppService = whatsAppService;
    }

    public async Task<LeaveRequest> SubmitLeaveRequestAsync(LeaveRequest request, string employeeName)
    {
        using var context = await _factory.CreateDbContextAsync();
        request.CreatedAt = TimeHelper.GetNow();
        request.Status = "Pending";
        request.EmployeeName = employeeName;
        
        // Ensure duration is calculated correctly (excluding non-working days)
        request.DurationDays = CalculateWorkingDays(request.StartDate, request.EndDate);

        context.LeaveRequests.Add(request);
        await context.SaveChangesAsync();
        
        _logger.LogInformation("Leave request submitted by {EmployeeId} for {Start} to {End}", 
            request.EmployeeId, request.StartDate.ToShortDateString(), request.EndDate.ToShortDateString());

        // Send email notification to manager
        try
        {
            var managerEmail = _configuration["EmailSettings:ManagerEmail"] ?? _configuration["EmailSettings:SmtpUsername"];
            var baseUrl = _configuration["AppBaseUrl"] ?? "http://localhost:5031"; 
            await _emailService.SendLeaveRequestEmailAsync(managerEmail!, employeeName, request, baseUrl);
            request.NotificationSent = true;
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send leave request email notification");
        }

        return request;
    }

    public async Task<List<LeaveRequest>> GetEmployeeLeaveRequestsAsync(string employeeId)
    {
        using var context = await _factory.CreateDbContextAsync();
        var records = await context.LeaveRequests
            .Where(l => l.EmployeeId == employeeId)
            .ToListAsync();
            
        return records
            .OrderByDescending(l => l.CreatedAt)
            .ToList();
    }

    public async Task<List<LeaveRequest>> GetPendingLeaveRequestsAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        var records = await context.LeaveRequests
            .Where(l => l.Status == "Pending")
            .ToListAsync();
            
        return records
            .OrderByDescending(l => l.CreatedAt)
            .ToList();
    }

    public async Task<List<LeaveRequest>> GetAllLeaveRequestsAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        return await context.LeaveRequests
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> ReviewLeaveRequestAsync(string requestId, string status, string? comment, string? adminId)
    {
        using var context = await _factory.CreateDbContextAsync();
        var request = await context.LeaveRequests.FindAsync(requestId);
        if (request == null) return false;

        var previousStatus = request.Status;
        request.Status = status;
        request.AdminComment = comment;
        request.ReviewedBy = adminId;
        request.ReviewedAt = TimeHelper.GetNow();

        // If newly approved, deduct from balance
        if (status == "Approved" && previousStatus != "Approved")
        {
            await DeductFromBalanceAsync(context, request.EmployeeId, request.LeaveType, request.DurationDays);
        }
        // If it was already approved and now changed to something else, revert balance
        else if (previousStatus == "Approved" && status != "Approved")
        {
            await RevertToBalanceAsync(context, request.EmployeeId, request.LeaveType, request.DurationDays);
        }
        
        await context.SaveChangesAsync();
        
        // Notify employee
        try
        {
            var employee = await context.Users.FindAsync(request.EmployeeId);
            if (employee != null)
            {
                // Email
                if (!string.IsNullOrEmpty(employee.Email))
                {
                    await _emailService.SendLeaveStatusUpdateEmailAsync(employee.Email, employee.Name, request);
                    request.ApprovalNotificationSent = true;
                }

                // WhatsApp
                if (_whatsAppService != null && !string.IsNullOrEmpty(employee.PhoneNumber))
                {
                    var msg = $"Leave Update: Your {request.LeaveType} leave for {request.StartDate:dd MMM} to {request.EndDate:dd MMM} has been {status.ToUpper()}.";
                    if (!string.IsNullOrEmpty(comment)) msg += $" Comment: {comment}";
                    
                    _ = _whatsAppService.SendMessageAsync(employee.PhoneNumber, msg);
                }

                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send status update notification for request {RequestId}", requestId);
        }

        _logger.LogInformation("Leave request {RequestId} {Status} by admin {AdminId}.", requestId, status, adminId);
        return true;
    }

    public async Task<LeaveBalance> GetOrCreateLeaveBalanceAsync(string employeeId, int? year = null)
    {
        using var context = await _factory.CreateDbContextAsync();
        return await GetOrCreateLeaveBalanceAsync(context, employeeId, year);
    }
    
    // Alias to match what I used in UI
    public Task<LeaveBalance> GetOrCreateBalanceAsync(string employeeId, int? year = null) => GetOrCreateLeaveBalanceAsync(employeeId, year);

    private async Task<LeaveBalance> GetOrCreateLeaveBalanceAsync(AppDbContext context, string employeeId, int? yearInput = null)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            throw new ArgumentException("Employee id is required to load leave balance.", nameof(employeeId));
        }

        var year = yearInput ?? TimeHelper.GetNow().Year;

        // Fetch and filter in-memory to avoid Cosmos DB composite index / OrderBy translation issues.
        var balance = (await context.LeaveBalances
                .Where(b => b.EmployeeId == employeeId)
                .ToListAsync())
            .FirstOrDefault(b => b.Year == year);

        if (balance != null)
        {
            return balance;
        }

        var settings = await _settingsService.GetSettingsAsync();
        balance = new LeaveBalance
        {
            EmployeeId = employeeId,
            Year = year,
            AnnualLeaveTotal = settings.DefaultAnnualLeaveDays.GetValueOrDefault(15),
            SickLeaveTotal = settings.DefaultSickLeaveDays.GetValueOrDefault(30),
            FamilyLeaveTotal = settings.DefaultFamilyLeaveDays.GetValueOrDefault(3),
            ParentalLeaveTotal = settings.DefaultParentalLeaveDays.GetValueOrDefault(10),
            MaternityLeaveTotal = settings.DefaultMaternityLeaveDays.GetValueOrDefault(120),
            StudyLeaveTotal = settings.DefaultStudyLeaveDays.GetValueOrDefault(5),
            OutLeaveTotal = settings.DefaultOutLeaveDays.GetValueOrDefault(5)
        };

        try
        {
            context.LeaveBalances.Add(balance);
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create leave balance for employee {EmployeeId} (year {Year})", employeeId, year);
            throw;
        }

        return balance;
    }

    public async Task UpdateBalanceAsync(LeaveBalance balance)
    {
        using var context = await _factory.CreateDbContextAsync();
        context.LeaveBalances.Update(balance);
        await context.SaveChangesAsync();
    }

    public async Task DeductFromBalanceAsync(AppDbContext context, string employeeId, string type, int days)
    {
        var balance = await GetOrCreateLeaveBalanceAsync(context, employeeId);
        switch (type)
        {
            case "Vacation": balance.AnnualLeaveUsed += days; break;
            case "Sick": balance.SickLeaveUsed += days; break;
            case "Family": balance.FamilyLeaveUsed += days; break;
            case "Parental": balance.ParentalLeaveUsed += days; break;
            case "Maternity": balance.MaternityLeaveUsed += days; break;
            case "Study": balance.StudyLeaveUsed += days; break;
            case "Out": balance.OutLeaveUsed += days; break;
            case "Unpaid": balance.UnpaidLeaveUsed += days; break;
        }
        balance.LastUpdated = TimeHelper.GetNow();
    }

    public async Task RevertToBalanceAsync(AppDbContext context, string employeeId, string type, int days)
    {
        var balance = await GetOrCreateLeaveBalanceAsync(context, employeeId);
        switch (type)
        {
            case "Vacation": balance.AnnualLeaveUsed -= days; break;
            case "Sick": balance.SickLeaveUsed -= days; break;
            case "Family": balance.FamilyLeaveUsed -= days; break;
            case "Parental": balance.ParentalLeaveUsed -= days; break;
            case "Maternity": balance.MaternityLeaveUsed -= days; break;
            case "Study": balance.StudyLeaveUsed -= days; break;
            case "Out": balance.OutLeaveUsed -= days; break;
            case "Unpaid": balance.UnpaidLeaveUsed -= days; break;
        }
        balance.LastUpdated = TimeHelper.GetNow();
    }

    public async Task<bool> IsOnApprovedLeaveAsync(string employeeId, DateTime date)
    {
        using var context = await _factory.CreateDbContextAsync();
        var checkDate = date.Date;
        var startOfNextDay = checkDate.AddDays(1);
        
        // Fetch approved leave requests for the employee from the database
        // Date filtering is done in-memory to avoid LINQ translation issues with Cosmos DB
        var approvedLeaves = await context.LeaveRequests
            .Where(l => l.EmployeeId == employeeId && l.Status == "Approved")
            .ToListAsync();

        return approvedLeaves.Any(l => l.StartDate < startOfNextDay && l.EndDate >= checkDate);
    }

    public int CalculateWorkingDays(DateTime start, DateTime end)
    {
        int count = 0;
        for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
        {
            if (_holidayService.IsWorkDay(date))
            {
                count++;
            }
        }
        return count;
    }
}
