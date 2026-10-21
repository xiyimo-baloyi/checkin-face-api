using Africanideasfaceapi.Services.Interfaces;
using Africanideasfaceapi.Helpers;
using Africanideasfaceapi.Data;
using Africanideasfaceapi.Models;
using Africanideasfaceapi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Africanideasfaceapi.Services;

public class AttendanceService : IAttendanceService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<AttendanceService> _logger;
    private readonly WhatsAppService? _whatsAppService;
    private readonly IHubContext<AnalyticsHub>? _analyticsHub;

    public AttendanceService(IDbContextFactory<AppDbContext> factory, ILogger<AttendanceService> logger, WhatsAppService? whatsAppService = null, IHubContext<AnalyticsHub>? analyticsHub = null)
    {
        _factory = factory;
        _logger = logger;
        _whatsAppService = whatsAppService;
        _analyticsHub = analyticsHub;
    }

    private async Task SaveClockInAsync(Attendance attendance, User user, bool deviceIdChanged)
    {
        await using (var attendanceContext = await _factory.CreateDbContextAsync())
        {
            attendanceContext.Attendance.Add(attendance);
            await attendanceContext.SaveChangesAsync();
        }

        await using var userContext = await _factory.CreateDbContextAsync();
        var dbUser = await userContext.Users.FindAsync(user.Id);
        if (dbUser == null)
            throw new InvalidOperationException("User not found.");

        dbUser.IsClockedIn = true;
        dbUser.LastClockInTime = TimeHelper.GetNow();
        if (deviceIdChanged)
            dbUser.DeviceId = user.DeviceId;

        MarkUserClockStatusModified(userContext, dbUser, deviceIdChanged);
        await userContext.SaveChangesAsync();
    }

    private static void MarkUserClockStatusModified(AppDbContext context, User user, bool includeDeviceId)
    {
        var entry = context.Entry(user);
        entry.Property(u => u.ImageBase64).IsModified = false;
        entry.Property(u => u.IsClockedIn).IsModified = true;
        entry.Property(u => u.LastClockInTime).IsModified = true;
        if (includeDeviceId)
        {
            entry.Property(u => u.DeviceId).IsModified = true;
        }
    }

    public async Task<Attendance> ClockInAsync(string employeeId, double? lat, double? lon, bool isOutOfArea, string? deviceId)
    {
        using var context = await _factory.CreateDbContextAsync();
        var today = TimeHelper.GetToday();
        var tomorrow = today.AddDays(1);
        
        // Fetch global settings and user
        var settings = await context.SystemSettings
            .FirstOrDefaultAsync(s => s.Id == "1") ?? new SystemSettings();
        var user = await context.Users.FindAsync(employeeId);
        
        if (user == null) throw new InvalidOperationException("User not found.");

        // 1. Device Lock Enforcement
        var deviceIdChanged = false;
        bool enforceDevice = (settings.GlobalDeviceLockEnforcement.GetValueOrDefault(false) || user.EnforceDeviceLock.GetValueOrDefault(false)) && !user.AllowAnyDevice.GetValueOrDefault(false);
        if (enforceDevice)
        {
            if (string.IsNullOrEmpty(user.DeviceId))
            {
                // First time linking device
                user.DeviceId = deviceId;
                deviceIdChanged = true;
                _logger.LogInformation("Linked device {DeviceId} to user {EmployeeId}", deviceId, employeeId);
            }
            else if (user.DeviceId != deviceId)
            {
                _logger.LogWarning("Device mismatch for user {EmployeeId}. Expected {Expected}, got {Actual}", 
                    employeeId, user.DeviceId, deviceId);
                throw new InvalidOperationException("Unauthorized Device: This account is locked to another device. Please contact administration.");
            }
        }
        else if (string.IsNullOrEmpty(user.DeviceId) && !string.IsNullOrEmpty(deviceId))
        {
            // Auto-link device if none exists even if not strictly enforced (for tracking)
            user.DeviceId = deviceId;
            deviceIdChanged = true;
        }

        // 2. Geofence & Work Type Enforcement
        bool skipGeofence = false;
        var todayDay = TimeHelper.GetNow().DayOfWeek;

        if (user.WorkType == "Remote")
        {
            skipGeofence = true;
        }
        else if (user.WorkType == "Hybrid")
        {
            // If today is NOT a scheduled office day, skip geofence (work from home)
            if (!user.HybridWorkDays.Contains(todayDay.ToString()))
            {
                skipGeofence = true;
            }
        }
        else // On-site
        {
            // Check for approved remote work request for TODAY
            var remoteRequest = await context.RemoteWorkRequests
                .OrderByDescending(r => r.Date)
                .FirstOrDefaultAsync(r => r.UserId == employeeId && r.Date >= today && r.Date < tomorrow && r.Status == "Approved");
        
            if (remoteRequest != null)
            {
                skipGeofence = true;
                _logger.LogInformation("Geofence skipped for On-site user {EmployeeId} due to approved remote request.", employeeId);
            }
        }

        bool enforceGeo = (settings.GlobalGeofenceEnforcement.GetValueOrDefault(true) || (user.EnforceGeofence.GetValueOrDefault(true))) && !skipGeofence;

        // Server-side enforcement: reject clock-in without GPS coordinates so that every
        // attendance record is fully traceable from the admin. We still allow clock-in
        // without coordinates ONLY for explicit remote/hybrid-from-home cases (skipGeofence).
        if (!skipGeofence && (!lat.HasValue || !lon.HasValue))
        {
            _logger.LogWarning("Clock-in rejected for user {EmployeeId}: GPS coordinates were not provided.", employeeId);
            throw new InvalidOperationException("Location Required: GPS coordinates are required to clock in. Please enable location access in your browser and try again.");
        }

        if (enforceGeo && isOutOfArea)
        {
            _logger.LogWarning("Geofence violation for user {EmployeeId} at {Lat}, {Lon}", employeeId, lat, lon);
            throw new InvalidOperationException("Location Restriction: You must be within a designated branch area to clock in.");
        }

        // Check for any unclosed attendance record (active shift)
        var activeAttendance = await context.Attendance
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.ClockOutTime == null);

        if (activeAttendance != null)
        {
            _logger.LogInformation("User {EmployeeId} has an active session (clocked in at {Time}).", employeeId, activeAttendance.ClockInTime);
            return activeAttendance;
        }

        var attendance = new Attendance
        {
            EmployeeId = employeeId,
            Date = today,
            ClockInTime = TimeHelper.GetNow(),
            Latitude = lat,
            Longitude = lon,
            IsOutOfArea = isOutOfArea,
            DeviceId = deviceId,
            Status = isOutOfArea ? "OutOfArea" : "Present"
        };

        // Determine Work Start Time (User Override -> Global Settings -> Default 09:00)
        TimeSpan? scheduledStart = user.WorkStartTime;
        if (!scheduledStart.HasValue && settings.StandardStartTime.HasValue)
        {
            var s = settings.StandardStartTime.Value;
            scheduledStart = new TimeSpan(s.Hour, s.Minute, s.Second);
        }

        if (scheduledStart.HasValue && attendance.ClockInTime.HasValue)
        {
            try
            {
                var clockInLocal = attendance.ClockInTime.Value.TimeOfDay;
                var thresholdMins = settings.LateThresholdMinutes.GetValueOrDefault(15);
                var lateThreshold = scheduledStart.Value.Add(TimeSpan.FromMinutes(thresholdMins)); 
                
                if (clockInLocal > lateThreshold)
                {
                    attendance.Status = isOutOfArea ? "OutOfArea (Late)" : "Late";
                    _logger.LogInformation("User {EmployeeId} clocked in LATE at {Time}. Scheduled: {Scheduled}", 
                        employeeId, clockInLocal, scheduledStart.Value);

                    // Send WhatsApp Alert
                    if (_whatsAppService != null && !string.IsNullOrEmpty(user.PhoneNumber))
                    {
                        var minutesLate = (clockInLocal - scheduledStart.Value).TotalMinutes;
                        _ = _whatsAppService.SendLateAlertAsync(user.PhoneNumber, user.Name, TimeSpan.FromMinutes(minutesLate));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error calculating late status for user {EmployeeId}. Defaulting to Present/OutOfArea.", employeeId);
            }
        }

        try
        {
            await SaveClockInAsync(attendance, user, deviceIdChanged);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Clock-in save failed for user {EmployeeId}. AttendanceId={AttendanceId}. Inner: {Inner}",
                employeeId, attendance.Id, ex.InnerException?.Message);
            throw;
        }

        _logger.LogInformation("User {EmployeeId} clocked in at {Time}. Out of area: {IsOutOfArea}", 
            employeeId, attendance.ClockInTime, isOutOfArea);

        // Broadcast real-time analytics update
        if (_analyticsHub != null)
        {
            await _analyticsHub.Clients.All.SendAsync("AnalyticsUpdated", "ClockIn", $"User {employeeId} clocked in");
        }

        return attendance;
    }

    public async Task<Attendance?> ClockOutAsync(string employeeId)
    {
        using var context = await _factory.CreateDbContextAsync();
        
        // Find the most recent unclosed attendance record
        // Fetch and order in-memory to avoid Cosmos DB composite index requirement
        var attendance = (await context.Attendance
            .Where(a => a.EmployeeId == employeeId && a.ClockOutTime == null)
            .ToListAsync())
            .OrderByDescending(a => a.ClockInTime)
            .FirstOrDefault();

        if (attendance == null)
        {
            // Self-healing: Check if user is stuck in "Clocked In" state without an open session
            var userToCheck = await context.Users.FindAsync(employeeId);
            if (userToCheck != null && userToCheck.IsClockedIn == true)
            {
                _logger.LogWarning("User {EmployeeId} was marked as Clocked In but had no open attendance session. Auto-correcting state.", employeeId);
                userToCheck.IsClockedIn = false;
                userToCheck.LastClockOutTime = TimeHelper.GetNow();
                var entry = context.Entry(userToCheck);
                entry.Property(u => u.ImageBase64).IsModified = false;
                entry.Property(u => u.IsClockedIn).IsModified = true;
                entry.Property(u => u.LastClockOutTime).IsModified = true;
                await context.SaveChangesAsync();
                return null; // Still return null to indicate no specific session was closed, but state is fixed
            }

            return null;
        }

        var user = await context.Users.FindAsync(employeeId);
        var settings = await context.SystemSettings
            .FirstOrDefaultAsync(s => s.Id == "1") ?? new SystemSettings();

        attendance.ClockOutTime = TimeHelper.GetNow();
        if (attendance.ClockOutTime.HasValue && attendance.ClockInTime.HasValue)
        {
            var sessionHours = (attendance.ClockOutTime.Value - attendance.ClockInTime.Value).TotalHours;
            attendance.TotalHours = sessionHours;
            
            // Aggregated daily progress for multi-shift support
            var sessionsToday = await context.Attendance
                .AsNoTracking()
                .Where(a => a.EmployeeId == employeeId && a.Date >= attendance.Date.Date && a.Date < attendance.Date.AddDays(1).Date && a.Id != attendance.Id)
                .ToListAsync();
            
            double previousHoursToday = sessionsToday.Sum(s => s.TotalHours ?? 0);
            double totalDailyHours = previousHoursToday + sessionHours;

            var standardShift = settings.StandardShiftHours ?? 8.0;
            if (standardShift <= 0) standardShift = 8.0;

            if (totalDailyHours > standardShift)
            {
                // If previous sessions already covered the shift, all current session hours are overtime
                if (previousHoursToday >= standardShift)
                {
                    attendance.ShiftHours = 0;
                    attendance.OvertimeHours = sessionHours;
                }
                else
                {
                    // Regular hours for the remainder of the shift, rest is overtime
                    attendance.ShiftHours = standardShift - previousHoursToday;
                    attendance.OvertimeHours = totalDailyHours - standardShift;
                }
                attendance.Status = (attendance.Status == null || attendance.Status == "Present") ? "Overtime" : $"{attendance.Status} (Overtime)";
            }
            else
            {
                attendance.ShiftHours = sessionHours;
                attendance.OvertimeHours = 0;
            }
        }

        // Determine Scheduled End Time (User Override -> Global Settings -> Default 17:00)
        if (user != null && attendance.ClockOutTime.HasValue)
        {
            TimeSpan? scheduledEnd = user.WorkEndTime;
            if (!scheduledEnd.HasValue && settings.StandardEndTime.HasValue)
            {
                var s = settings.StandardEndTime.Value;
                scheduledEnd = new TimeSpan(s.Hour, s.Minute, s.Second);
            }

            if (scheduledEnd.HasValue)
            {
                var clockOutLocal = attendance.ClockOutTime.Value.TimeOfDay;
                if (clockOutLocal < scheduledEnd.Value)
                {
                    // Update Status to indicate early departure
                    var currentStatus = attendance.Status ?? "Present";
                    if (!currentStatus.Contains("Early Departure"))
                    {
                        attendance.Status = currentStatus == "Present" ? "Early Departure" : $"{currentStatus} (Early Departure)";
                    }
                    _logger.LogInformation("User {EmployeeId} clocked out EARLY at {Time}. Scheduled: {Scheduled}", 
                        employeeId, clockOutLocal, scheduledEnd.Value);
                }
            }

            user.IsClockedIn = false;
            user.LastClockOutTime = TimeHelper.GetNow();
            var entry = context.Entry(user);
            entry.Property(u => u.ImageBase64).IsModified = false;
            entry.Property(u => u.IsClockedIn).IsModified = true;
            entry.Property(u => u.LastClockOutTime).IsModified = true;
        }

        await context.SaveChangesAsync();
        _logger.LogInformation("User {EmployeeId} clocked out at {Time}. Total hours: {Hours:F2}", 
            employeeId, attendance.ClockOutTime, attendance.TotalHours);

        // Broadcast real-time analytics update
        if (_analyticsHub != null)
        {
            await _analyticsHub.Clients.All.SendAsync("AnalyticsUpdated", "ClockOut", $"User {employeeId} clocked out");
        }

        return attendance;
    }

    public async Task<Attendance?> GetAttendanceByIdAsync(string id)
    {
        using var context = await _factory.CreateDbContextAsync();
        return await context.Attendance.FindAsync(id);
    }

    public async Task<List<Attendance>> GetEmployeeAttendanceAsync(string employeeId)
    {
        using var context = await _factory.CreateDbContextAsync();
        var records = await context.Attendance
            .AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .ToListAsync();
            
        return records
            .OrderByDescending(a => a.Date)
            .ThenByDescending(a => a.ClockInTime)
            .ToList();
    }

    public async Task<List<Attendance>> GetRecentAttendanceAsync(int count = 50)
    {
        using var context = await _factory.CreateDbContextAsync();
        var records = await context.Attendance
            .AsNoTracking()
            .ToListAsync();
            
        return records
            .OrderByDescending(a => a.ClockInTime)
            .Take(count)
            .ToList();
    }

    public async Task<Attendance?> GetTodayAttendanceAsync(string employeeId)
    {
        using var context = await _factory.CreateDbContextAsync();
        var today = TimeHelper.GetToday();
        var tomorrow = today.AddDays(1);
        var records = await context.Attendance
            .AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.Date >= today && a.Date < tomorrow)
            .ToListAsync();
            
        return records
            .OrderByDescending(a => a.ClockInTime)
            .FirstOrDefault();
    }

    public async Task<int> AutoClockOutCheckAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        var settings = await context.SystemSettings
            .FirstOrDefaultAsync(s => s.Id == "1") ?? new SystemSettings();

        var threshold = settings.AutoClockOutThreshold ?? 10.0;
        if (threshold <= 0) threshold = 10.0;

        var cutoffTime = TimeHelper.GetNow().AddHours(-threshold);

        var openSessions = await context.Attendance
            .Where(a => a.ClockOutTime == null && a.ClockInTime < cutoffTime)
            .ToListAsync();

        int count = 0;
        foreach (var session in openSessions)
        {
            session.ClockOutTime = session.ClockInTime?.AddHours(threshold) ?? TimeHelper.GetNow();
            session.TotalHours = threshold;
            var shiftHours = (settings.StandardShiftHours.GetValueOrDefault(0)) > 0 ? settings.StandardShiftHours!.Value : 8.0;
            session.ShiftHours = shiftHours;
            session.OvertimeHours = threshold - shiftHours;
            session.Status = "Auto Clock-Out";

            var user = await context.Users.FindAsync(session.EmployeeId);
            if (user != null)
            {
                user.IsClockedIn = false;
                user.LastClockOutTime = session.ClockOutTime;
                var entry = context.Entry(user);
                entry.Property(u => u.ImageBase64).IsModified = false;
                entry.Property(u => u.IsClockedIn).IsModified = true;
                entry.Property(u => u.LastClockOutTime).IsModified = true;
            }
            count++;
        }

        if (count > 0)
        {
            await context.SaveChangesAsync();
            _logger.LogInformation("Auto-closed {Count} stale attendance sessions.", count);
        }

        return count;
    }
}
