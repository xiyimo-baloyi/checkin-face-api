using Africanideasfaceapi.Data;
using Africanideasfaceapi.Models;
using Africanideasfaceapi.Helpers;
using Microsoft.EntityFrameworkCore;
using Africanideasfaceapi.Services.Interfaces;
using System.Text;

namespace Africanideasfaceapi.Services;

/// <summary>
/// Handles automated absence notifications and reports
/// </summary>
public class AbsenceNotificationService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IAbsenceService _absenceService;
    private readonly AppNotificationService _notificationService;
    private readonly EmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AbsenceNotificationService> _logger;

    public AbsenceNotificationService(
        IDbContextFactory<AppDbContext> factory,
        IAbsenceService absenceService,
        AppNotificationService notificationService,
        EmailService emailService,
        IConfiguration configuration,
        ILogger<AbsenceNotificationService> logger)
    {
        _factory = factory;
        _absenceService = absenceService;
        _notificationService = notificationService;
        _emailService = emailService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Runs daily absence detection and sends notifications to absent employees and admins
    /// Should be scheduled to run after work hours (e.g., 6 PM)
    /// </summary>
    public async Task SendDailyAbsenceAlertsAsync()
    {
        try
        {
            var today = TimeHelper.GetToday();
            _logger.LogInformation("Starting daily absence alerts for {Date}", today.ToShortDateString());

            // 1. Run absence detection for today
            var absenceCount = await _absenceService.RunAbsenceDetectionAsync(today);
            
            if (absenceCount == 0)
            {
                _logger.LogInformation("No absences detected for {Date}", today.ToShortDateString());
                return;
            }

            using var context = await _factory.CreateDbContextAsync();

            // 2. Get all new absences created today
            var todayAbsences = await context.Absences
                .Where(a => a.Date == today && a.Status == "Pending")
                .ToListAsync();

            // 3. Get user details for absent employees
            var absentEmployeeIds = todayAbsences.Select(a => a.EmployeeId).ToList();
            var absentEmployees = await context.Users
                .Where(u => absentEmployeeIds.Contains(u.Id))
                .ToListAsync();

            // 4. Send notification to each absent employee
            foreach (var absence in todayAbsences)
            {
                var employee = absentEmployees.FirstOrDefault(e => e.Id == absence.EmployeeId);
                if (employee != null)
                {
                    await SendAbsenceAlertToEmployeeAsync(employee, absence);
                }
            }

            // 5. Send daily summary to all admins
            await SendDailySummaryToAdminsAsync(today, todayAbsences, absentEmployees);

            _logger.LogInformation("Daily absence alerts completed. {Count} employees notified", absenceCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending daily absence alerts");
        }
    }

    /// <summary>
    /// Generates and sends weekly absence report every Friday at 5 PM
    /// </summary>
    public async Task SendWeeklyAbsenceReportAsync()
    {
        try
        {
            var today = TimeHelper.GetToday();
            
            // Calculate week range (Monday to Friday)
            var daysFromMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var monday = today.AddDays(-daysFromMonday);
            var friday = monday.AddDays(4);

            _logger.LogInformation("Generating weekly absence report for {StartDate} to {EndDate}", 
                monday.ToShortDateString(), friday.ToShortDateString());

            using var context = await _factory.CreateDbContextAsync();

            // Get all absences for the week
            var weekAbsences = await context.Absences
                .Where(a => a.Date >= monday && a.Date <= friday)
                .ToListAsync();

            if (!weekAbsences.Any())
            {
                _logger.LogInformation("No absences for the week. Skipping report.");
                return;
            }

            // Group by employee and calculate totals
            var absencesByEmployee = weekAbsences
                .GroupBy(a => a.EmployeeId)
                .Select(g => new EmployeeAbsenceSummary
                {
                    EmployeeId = g.Key,
                    AbsenceCount = g.Count(),
                    Dates = g.Select(a => a.Date).OrderBy(d => d).ToList()
                })
                .OrderByDescending(x => x.AbsenceCount)
                .ToList();

            // Get employee details
            var employeeIds = absencesByEmployee.Select(a => a.EmployeeId).ToList();
            var employees = await context.Users
                .Where(u => employeeIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id);

            // Generate report HTML
            var reportHtml = GenerateWeeklyReportHtml(monday, friday, absencesByEmployee, employees);

            // Send to all admins
            var admins = await context.Users
                .Where(u => u.Role == "Admin" && u.IsActive == true && u.Email != null && u.Email != "")
                .ToListAsync();

            foreach (var admin in admins)
            {
                await _emailService.SendEmailAsync(
                    admin.Email!,
                    $"Weekly Absence Report - Week of {monday:MMM dd}",
                    reportHtml
                );
            }

            _logger.LogInformation("Weekly absence report sent to {Count} admins", admins.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending weekly absence report");
        }
    }

    private async Task SendAbsenceAlertToEmployeeAsync(User employee, Absence absence)
    {
        try
        {
            var title = "Absence Notice - Action Required";
            var message = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
                    <h2 style='color: #dc3545;'>Absence Notice</h2>
                    <p>Dear {employee.Name},</p>
                    <p>Our records show you did not clock in on <strong>{absence.Date:dddd, MMMM dd, yyyy}</strong>.</p>
                    <div style='background-color: #fff3cd; border-left: 4px solid #ffc107; padding: 15px; margin: 20px 0;'>
                        <p style='margin: 0; color: #856404;'><strong>⚠️ Your day will be marked as absent unless you submit a reason.</strong></p>
                    </div>
                    <p>Please log in to the system and provide an explanation for your absence as soon as possible.</p>
                    <div style='margin-top: 20px;'>
                        <a href='{(_configuration["AppBaseUrl"] ?? "http://localhost:5031")}/absences' 
                           style='background-color: #0078d4; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                            Submit Reason
                        </a>
                    </div>
                    <hr style='border: none; border-top: 1px solid #eee; margin: 30px 0;' />
                    <p style='font-size: 0.8em; color: #777;'>This is an automated message from the African Ideas Attendance System.</p>
                </div>";

            // Create in-app notification
            await _notificationService.CreateNotificationAsync(
                employee.Id,
                title,
                $"You did not clock in on {absence.Date:MMM dd}. Please submit a reason to avoid penalties.",
                "AbsenceAlert",
                employee.Email
            );

            _logger.LogInformation("Absence alert sent to {EmployeeName} ({Email})", employee.Name, employee.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send absence alert to employee {EmployeeId}", employee.Id);
        }
    }

    private async Task SendDailySummaryToAdminsAsync(DateTime date, List<Absence> absences, List<User> absentEmployees)
    {
        try
        {
            var employeeDict = absentEmployees.ToDictionary(e => e.Id);
            
            var summaryHtml = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
                    <h2 style='color: #0078d4;'>Daily Absence Summary</h2>
                    <p><strong>Date:</strong> {date:dddd, MMMM dd, yyyy}</p>
                    <p><strong>Total Absent:</strong> {absences.Count} employees</p>
                    <hr style='border: none; border-top: 2px solid #0078d4; margin: 20px 0;' />
                    <table style='width: 100%; border-collapse: collapse; margin-top: 20px;'>
                        <thead>
                            <tr style='background-color: #f8f9fa;'>
                                <th style='padding: 12px; text-align: left; border-bottom: 2px solid #dee2e6;'>Employee</th>
                                <th style='padding: 12px; text-align: left; border-bottom: 2px solid #dee2e6;'>Email</th>
                                <th style='padding: 12px; text-align: center; border-bottom: 2px solid #dee2e6;'>Status</th>
                            </tr>
                        </thead>
                        <tbody>
                            {string.Join("", absences.Select(a =>
                            {
                                var emp = employeeDict.ContainsKey(a.EmployeeId) ? employeeDict[a.EmployeeId] : null;
                                var name = emp != null ? $"{emp.Name} {emp.Surname}" : "Unknown";
                                var email = emp?.Email ?? "N/A";
                                return $@"
                                <tr>
                                    <td style='padding: 10px; border-bottom: 1px solid #dee2e6;'>{name}</td>
                                    <td style='padding: 10px; border-bottom: 1px solid #dee2e6;'>{email}</td>
                                    <td style='padding: 10px; border-bottom: 1px solid #dee2e6; text-align: center;'>
                                        <span style='background-color: #ffc107; color: #000; padding: 4px 12px; border-radius: 12px; font-size: 0.85em;'>
                                            {a.Status}
                                        </span>
                                    </td>
                                </tr>";
                            }))}
                        </tbody>
                    </table>
                    <hr style='border: none; border-top: 1px solid #eee; margin: 30px 0;' />
                    <p style='font-size: 0.8em; color: #777;'>This is an automated daily report from the African Ideas Attendance System.</p>
                </div>";

            using var context = await _factory.CreateDbContextAsync();
            var admins = await context.Users
                .Where(u => u.Role == "Admin" && u.IsActive == true && u.Email != null && u.Email != "")
                .ToListAsync();

            foreach (var admin in admins)
            {
                await _emailService.SendEmailAsync(
                    admin.Email!,
                    $"Daily Absence Summary - {date:MMM dd, yyyy}",
                    summaryHtml
                );
            }

            _logger.LogInformation("Daily summary sent to {Count} admins", admins.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send daily summary to admins");
        }
    }

    private string GenerateWeeklyReportHtml(DateTime monday, DateTime friday, 
        List<EmployeeAbsenceSummary> absencesByEmployee, Dictionary<string, User> employees)
    {
        var totalAbsences = absencesByEmployee.Sum(a => a.AbsenceCount);
        
        return $@"
            <div style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
                <h1 style='color: #0078d4; border-bottom: 3px solid #0078d4; padding-bottom: 10px;'>
                    Weekly Absence Report
                </h1>
                <p><strong>Week of:</strong> {monday:MMMM dd} - {friday:MMMM dd, yyyy}</p>
                <p><strong>Total Absences:</strong> {totalAbsences} days</p>
                <p><strong>Employees Affected:</strong> {absencesByEmployee.Count}</p>
                
                <hr style='border: none; border-top: 2px solid #0078d4; margin: 30px 0;' />
                
                <h2 style='color: #495057; font-size: 1.2em;'>Absence Details</h2>
                <table style='width: 100%; border-collapse: collapse; margin-top: 20px;'>
                    <thead>
                        <tr style='background-color: #0078d4; color: white;'>
                            <th style='padding: 12px; text-align: left;'>Employee</th>
                            <th style='padding: 12px; text-align: center;'>Days Absent</th>
                            <th style='padding: 12px; text-align: left;'>Dates</th>
                        </tr>
                    </thead>
                    <tbody>
                        {string.Join("", absencesByEmployee.Select((a, index) =>
                        {
                            var emp = employees.ContainsKey(a.EmployeeId) ? employees[a.EmployeeId] : null;
                            var name = emp != null ? $"{emp.Name} {emp.Surname}" : "Unknown";
                            var dates = string.Join(", ", a.Dates.Select(d => d.ToString("MMM dd")));
                            var bgColor = index % 2 == 0 ? "#f8f9fa" : "#ffffff";
                            
                            return $@"
                            <tr style='background-color: {bgColor};'>
                                <td style='padding: 10px; border-bottom: 1px solid #dee2e6;'>{name}</td>
                                <td style='padding: 10px; border-bottom: 1px solid #dee2e6; text-align: center; font-weight: bold;'>
                                    {a.AbsenceCount}
                                </td>
                                <td style='padding: 10px; border-bottom: 1px solid #dee2e6;'>{dates}</td>
                            </tr>";
                        }))}
                    </tbody>
                </table>
                
                <hr style='border: none; border-top: 1px solid #eee; margin: 30px 0;' />
                <p style='font-size: 0.9em; color: #666;'>
                    <strong>Note:</strong> This report includes all absences recorded during the week, 
                    regardless of approval status. Please review pending absence reasons in the admin dashboard.
                </p>
                <p style='font-size: 0.8em; color: #777; margin-top: 20px;'>
                    This is an automated weekly report from the African Ideas Attendance System.
                </p>
            </div>";
    }

    // Helper class for weekly report
    private class EmployeeAbsenceSummary
    {
        public string EmployeeId { get; set; } = string.Empty;
        public int AbsenceCount { get; set; }
        public List<DateTime> Dates { get; set; } = new();
    }
}
