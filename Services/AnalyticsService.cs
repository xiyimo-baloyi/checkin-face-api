using Africanideasfaceapi.Helpers;
using Africanideasfaceapi.Models;
using Africanideasfaceapi.Data;
using Microsoft.EntityFrameworkCore;
using Africanideasfaceapi.Services.Interfaces;

namespace Africanideasfaceapi.Services;

/// <summary>
/// Service for analytics and statistics
/// </summary>
public class AnalyticsService : IAnalyticsService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<AnalyticsService> _logger;
    private readonly IAbsenceService _absenceService;

    public AnalyticsService(IDbContextFactory<AppDbContext> factory, ILogger<AnalyticsService> logger, IAbsenceService absenceService)
    {
        _factory = factory;
        _logger = logger;
        _absenceService = absenceService;
    }

    /// <summary>
    /// Get dashboard statistics for a specific timeframe
    /// </summary>
    public async Task<DashboardStats> GetDashboardStatsAsync(int days = 30)
    {
        using var context = await _factory.CreateDbContextAsync();
        var totalUsers = await context.Users.CountAsync(u => u.IsActive == true);
        var faceRegistered = await context.Users.CountAsync(u => u.IsActive == true && u.PersonId != null);
        var documentRegistered = await context.Users.CountAsync(u => u.IsActive == true && u.RegistrationMethod == "Document");
        var idCardVerified = await context.Users.CountAsync(u => u.IsActive == true && u.IdPassportNumber != null);
        var withConsent = await context.Users.CountAsync(u => u.IsActive == true && u.HasConsented == true);

        var today = TimeHelper.GetToday();
        var startDate = today.AddDays(-days);
        var thisWeek = today.AddDays(-(int)today.DayOfWeek);
        var thisMonth = new DateTime(today.Year, today.Month, 1);
        
        var registrationsInRange = await context.Users.CountAsync(u => u.IsActive == true && u.RegisteredDate >= startDate);
        var registrationsToday = await context.Users.CountAsync(u => u.IsActive == true && u.RegisteredDate >= today);
        var registrationsThisWeek = await context.Users.CountAsync(u => u.IsActive == true && u.RegisteredDate >= thisWeek);
        var registrationsThisMonth = await context.Users.CountAsync(u => u.IsActive == true && u.RegisteredDate >= thisMonth);

        var successfulLogins = await context.AuditLogs.CountAsync(a => a.ActionType == "Login" && a.Result == "Success" && a.Timestamp >= startDate);
        var failedLogins = await context.AuditLogs.CountAsync(a => a.ActionType == "Login" && a.Result == "Failed" && a.Timestamp >= startDate);
        var loginSuccessRate = successfulLogins + failedLogins > 0 
            ? (double)successfulLogins / (successfulLogins + failedLogins) * 100 
            : 0;

        var idCardVerifications = await context.IdCardVerifications.CountAsync(v => v.VerificationDate >= startDate);
        var successfulVerificatons = await context.IdCardVerifications.CountAsync(v => v.IsVerified && v.VerificationDate >= startDate);
        var verificationSuccessRate = idCardVerifications > 0 
            ? (double)successfulVerificatons / idCardVerifications * 100 
            : 0;

        var pendingApprovals = await context.Users.CountAsync(u => u.IsActive == true && u.Status == "Pending");

        // Real active users today (unique IDs in AuditLogs + Attendance)
        var activeUserIdsAudit = await context.AuditLogs
            .Where(a => a.Timestamp >= today)
            .Select(a => a.UserId)
            .Distinct()
            .ToListAsync();
            
        var activeUserIdsAttendance = await context.Attendance
            .AsNoTracking()
            .Where(a => a.Date >= today)
            .Select(a => a.EmployeeId)
            .Distinct()
            .ToListAsync();
            
        var activeUsersTodayCount = activeUserIdsAudit
            .Union(activeUserIdsAttendance)
            .Where(id => id != "Unknown" && !string.IsNullOrEmpty(id))
            .Distinct()
            .Count();

        var attendanceInRange = await context.Attendance
            .AsNoTracking()
            .Where(a => a.Date >= startDate)
            .ToListAsync();

        var totalOvertimeHours = attendanceInRange.Sum(a => a.OvertimeHours ?? 0);
        var overtimeCount = attendanceInRange.Count(a => (a.OvertimeHours ?? 0) > 0);

        return new DashboardStats
        {
            TotalWorkers = totalUsers,
            FaceRegistered = faceRegistered,
            DocumentRegistered = documentRegistered,
            IdCardVerified = idCardVerified,
            WithConsent = withConsent,
            RegistrationsToday = registrationsToday,
            RegistrationsInRange = registrationsInRange,
            RegistrationsThisWeek = registrationsThisWeek,
            RegistrationsThisMonth = registrationsThisMonth,
            LoginSuccessRate = loginSuccessRate,
            VerificationSuccessRate = verificationSuccessRate,
            ActiveUsersToday = activeUsersTodayCount, 
            PendingApprovals = pendingApprovals,
            TotalOvertimeHours = totalOvertimeHours,
            OvertimeCount = overtimeCount
        };
    }

    /// <summary>
    /// Gets distribution of workers across different branches
    /// </summary>
    public async Task<List<BranchDistribution>> GetBranchDistributionAsync()
    {
        try
        {
            using var context = await _factory.CreateDbContextAsync();
            var branches = await context.Branches.Where(b => b.IsActive).ToListAsync();
            var users = await context.Users.Where(u => u.IsActive == true && u.BranchId != null).ToListAsync();
            
            var distribution = branches.Select(b => new BranchDistribution
            {
                BranchName = b.Name,
                WorkerCount = users.Count(u => u.BranchId == b.Id)
            }).OrderByDescending(d => d.WorkerCount).ToList();
            
            return distribution;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting branch distribution");
            return new List<BranchDistribution>();
        }
    }

    /// <summary>
    /// Gets the most recent system activity logs
    /// </summary>
    public async Task<List<AuditLog>> GetRecentActivityAsync(int count = 10)
    {
        try
        {
            using var context = await _factory.CreateDbContextAsync();
            return await context.AuditLogs
                .OrderByDescending(a => a.Timestamp)
                .Take(count)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent activity logs");
            return new List<AuditLog>();
        }
    }

    /// <summary>
    /// Get registration trends over time
    /// </summary>
    public async Task<List<RegistrationTrend>> GetRegistrationTrendsAsync(int days = 30)
    {
        using var context = await _factory.CreateDbContextAsync();
        var startDate = TimeHelper.GetToday().AddDays(-days);
        
        // Fetch data first (Cosmos DB provider has limited GroupBy support)
        var users = await context.Users
            .Where(u => u.IsActive == true && u.RegisteredDate >= startDate)
            .ToListAsync();

        var trends = users
            .GroupBy(u => u.RegisteredDate.GetValueOrDefault().Date)
            .Select(g => new RegistrationTrend
            {
                Date = g.Key,
                Count = g.Count(),
                FaceCount = g.Count(u => u.RegistrationMethod == "Face"),
                DocumentCount = g.Count(u => u.RegistrationMethod == "Document")
            })
            .OrderBy(t => t.Date)
            .ToList();

        return trends;
    }

    /// <summary>
    /// Get ID card verification statistics
    /// </summary>
    public async Task<IdCardVerificationStats> GetIdCardVerificationStatsAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        var total = await context.IdCardVerifications.CountAsync();
        var successful = await context.IdCardVerifications.CountAsync(v => v.IsVerified);
        var failed = total - successful;
        
        var avgConfidence = await context.IdCardVerifications
            .Where(v => v.IsVerified)
            .AverageAsync(v => (double?)v.Confidence) ?? 0;

        var recentVerifications = await context.IdCardVerifications
            .OrderByDescending(v => v.VerificationDate)
            .Take(10)
            .ToListAsync();

        return new IdCardVerificationStats
        {
            Total = total,
            Successful = successful,
            Failed = failed,
            SuccessRate = total > 0 ? (double)successful / total * 100 : 0,
            AverageConfidence = avgConfidence,
            RecentVerifications = recentVerifications
        };
    }
    /// <summary>
    /// Get attendance statistics for a specific date
    /// </summary>
    public async Task<AttendanceStats> GetDailyAttendanceStatsAsync(DateTime date)
    {
        // TRIGGER: Run absence detection for the requested date (Monday-Friday) 
        // using fire-and-forget but ensuring we wait if it's today and we want fresh data
        if (date.Date <= TimeHelper.GetToday())
        {
            try 
            {
                await _absenceService.RunAbsenceDetectionAsync(date);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Auto-absence detection failed during analytics retrieval: {Message}", ex.Message);
            }
        }

        using var context = await _factory.CreateDbContextAsync();
        var targetDate = date.Date;
        var totalUsers = await context.Users.CountAsync(u => u.Role == "User" && u.IsActive == true && u.Status == "Approved");
        
        var attendanceRecords = await context.Attendance
            .AsNoTracking()
            .Where(a => a.Date == targetDate).ToListAsync();
        var presentCount = attendanceRecords.Count;
        var outOfAreaCount = attendanceRecords.Count(a => a.IsOutOfArea);
        var totalOvertime = attendanceRecords.Sum(a => a.OvertimeHours ?? 0);
        var overtimeCount = attendanceRecords.Count(a => (a.OvertimeHours ?? 0) > 0);

        var absenceCount = await context.Absences.CountAsync(a => a.Date == targetDate);

        return new AttendanceStats
        {
            Date = targetDate,
            TotalEmployees = totalUsers,
            PresentCount = presentCount,
            OutOfAreaCount = outOfAreaCount,
            AbsenceCount = absenceCount,
            AttendanceRate = totalUsers > 0 ? (double)presentCount / totalUsers * 100 : 0,
            TotalOvertimeHours = totalOvertime,
            OvertimeCount = overtimeCount
        };
    }

    /// <summary>
    /// Gets detailed login logs for a specific timeframe
    /// </summary>
    public async Task<List<AuditLog>> GetDetailedLoginLogsAsync(int count = 50)
    {
        using var context = await _factory.CreateDbContextAsync();
        return await context.AuditLogs
            .Where(a => a.ActionType == "Login")
            .OrderByDescending(a => a.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    /// <summary>
    /// Gets face logs with images for auditing, filtered by confidence
    /// </summary>
    public async Task<List<FaceLog>> GetHighConfidenceAttemptsAsync(double minConfidence = 0.4, int count = 50)
    {
        using var context = await _factory.CreateDbContextAsync();
        return await context.FaceLogs
            .Where(l => l.ConfidenceScore >= minConfidence)
            .OrderByDescending(l => l.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    /// <summary>
    /// Get face recognition accuracy statistics
    /// </summary>
    public async Task<FaceAccuracyStats> GetFaceAccuracyStatsAsync(DateTime start, DateTime end)
    {
        using var context = await _factory.CreateDbContextAsync();
        var logs = await context.FaceLogs
            .Where(l => l.Timestamp >= start && l.Timestamp <= end)
            .ToListAsync();

        if (!logs.Any()) return new FaceAccuracyStats();

        var successfulLogs = logs.Where(l => l.Success).ToList();
        
        return new FaceAccuracyStats
        {
            TotalAttempts = logs.Count,
            SuccessCount = successfulLogs.Count,
            AverageConfidence = successfulLogs.Any() ? successfulLogs.Average(l => l.ConfidenceScore) : 0,
            FailureReasons = logs.Where(l => !l.Success)
                .GroupBy(l => l.ErrorMessage ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    /// <summary>
    /// Get biometric health metrics - system performance and quality indicators
    /// </summary>
    public async Task<BiometricHealthMetrics> GetBiometricHealthMetricsAsync(DateTime start, DateTime end)
    {
        using var context = await _factory.CreateDbContextAsync();
        var faceLogs = await context.FaceLogs
            .Where(l => l.Timestamp >= start && l.Timestamp <= end)
            .ToListAsync();

        var auditLogs = await context.AuditLogs
            .Where(a => a.Timestamp >= start && a.Timestamp <= end && a.ActionType == "Login")
            .ToListAsync();

        var totalFaceAttempts = faceLogs.Count;
        var successfulFaceAttempts = faceLogs.Count(l => l.Success);
        var avgResponseTime = faceLogs.Any() ? faceLogs.Average(l => l.ProcessingTimeMs ?? 0) : 0;
        
        var loginAttempts = auditLogs.Count;
        var successfulLogins = auditLogs.Count(a => a.Result == "Success");
        var failedLogins = loginAttempts - successfulLogins;

        // Calculate confidence distribution
        var confidenceDistribution = faceLogs
            .Where(l => l.Success)
            .GroupBy(l => Math.Floor(l.ConfidenceScore * 10) / 10) // Group by 0.1 intervals
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());

        return new BiometricHealthMetrics
        {
            TotalFaceRecognitionAttempts = totalFaceAttempts,
            SuccessfulRecognitions = successfulFaceAttempts,
            FailedRecognitions = totalFaceAttempts - successfulFaceAttempts,
            AverageConfidenceScore = faceLogs.Where(l => l.Success).Any() 
                ? faceLogs.Where(l => l.Success).Average(l => l.ConfidenceScore) 
                : 0,
            AverageResponseTimeMs = avgResponseTime,
            LoginSuccessRate = loginAttempts > 0 ? (double)successfulLogins / loginAttempts * 100 : 0,
            TotalLoginAttempts = loginAttempts,
            FailedLoginAttempts = failedLogins,
            ConfidenceDistribution = confidenceDistribution,
            SystemHealthScore = CalculateSystemHealthScore(successfulFaceAttempts, totalFaceAttempts, avgResponseTime)
        };
    }

    /// <summary>
    /// Get attendance trends and patterns over time
    /// </summary>
    public async Task<AttendanceTrendAnalysis> GetAttendanceTrendsAsync(DateTime start, DateTime end)
    {
        using var context = await _factory.CreateDbContextAsync();
        var attendanceRecords = await context.Attendance
            .AsNoTracking()
            .Where(a => a.Date >= start && a.Date <= end)
            .ToListAsync();

        var dailyAttendance = attendanceRecords
            .GroupBy(a => a.Date)
            .Select(g => new DailyAttendanceData
            {
                Date = g.Key,
                TotalPresent = g.Count(),
                OnTime = g.Count(a => a.Status != "Late"),
                Late = g.Count(a => a.Status == "Late"),
                OutOfArea = g.Count(a => a.IsOutOfArea),
                AverageHours = g.Any() ? g.Average(a => a.TotalHours ?? 0) : 0,
                TotalOvertimeHours = g.Sum(a => a.OvertimeHours ?? 0)
            })
            .OrderBy(d => d.Date)
            .ToList();

        var totalEmployees = await context.Users.CountAsync(u => u.Role == "User" && u.IsActive == true && u.Status == "Approved");
        
        return new AttendanceTrendAnalysis
        {
            DailyData = dailyAttendance,
            AverageAttendanceRate = dailyAttendance.Any() && totalEmployees > 0
                ? dailyAttendance.Average(d => (double)d.TotalPresent / totalEmployees * 100)
                : 0,
            AverageLateRate = dailyAttendance.Any()
                ? dailyAttendance.Average(d => d.TotalPresent > 0 ? (double)d.Late / d.TotalPresent * 100 : 0)
                : 0,
            TotalWorkingDays = dailyAttendance.Count,
            TotalEmployees = totalEmployees
        };
    }

    /// <summary>
    /// Get peak usage times and patterns
    /// </summary>
    public async Task<PeakUsageAnalysis> GetPeakUsageAnalysisAsync(DateTime start, DateTime end)
    {
        using var context = await _factory.CreateDbContextAsync();
        var attendanceRecords = await context.Attendance
            .AsNoTracking()
            .Where(a => a.Date >= start && a.Date <= end && a.ClockInTime != null)
            .ToListAsync();

        var hourlyClockIns = attendanceRecords
            .Where(a => a.ClockInTime.HasValue)
            .GroupBy(a => a.ClockInTime!.Value.ToLocalTime().Hour)
            .Select(g => new HourlyUsageData
            {
                Hour = g.Key,
                Count = g.Count()
            })
            .OrderBy(h => h.Hour)
            .ToList();

        var dayOfWeekPattern = attendanceRecords
            .GroupBy(a => a.Date.DayOfWeek)
            .Select(g => new DayOfWeekData
            {
                DayOfWeek = g.Key,
                AverageAttendance = g.Count() / (double)((end - start).Days / 7 + 1)
            })
            .OrderBy(d => d.DayOfWeek)
            .ToList();

        return new PeakUsageAnalysis
        {
            HourlyClockInDistribution = hourlyClockIns,
            DayOfWeekPattern = dayOfWeekPattern,
            PeakHour = hourlyClockIns.OrderByDescending(h => h.Count).FirstOrDefault()?.Hour ?? 0,
            BusiestDay = dayOfWeekPattern.OrderByDescending(d => d.AverageAttendance).FirstOrDefault()?.DayOfWeek ?? DayOfWeek.Monday
        };
    }

    /// <summary>
    /// Get device and location analytics
    /// </summary>
    public async Task<DeviceLocationAnalytics> GetDeviceLocationAnalyticsAsync(DateTime start, DateTime end)
    {
        using var context = await _factory.CreateDbContextAsync();
        var attendanceRecords = await context.Attendance
            .AsNoTracking()
            .Where(a => a.Date >= start && a.Date <= end)
            .ToListAsync();

        var deviceUsage = attendanceRecords
            .Where(a => !string.IsNullOrEmpty(a.DeviceId))
            .GroupBy(a => a.DeviceId)
            .Select(g => new DeviceUsageData
            {
                DeviceId = g.Key ?? "Unknown",
                UsageCount = g.Count(),
                UniqueUsers = g.Select(a => a.EmployeeId).Distinct().Count()
            })
            .OrderByDescending(d => d.UsageCount)
            .Take(10)
            .ToList();

        var outOfAreaCount = attendanceRecords.Count(a => a.IsOutOfArea);
        var totalRecords = attendanceRecords.Count;

        return new DeviceLocationAnalytics
        {
            TopDevices = deviceUsage,
            TotalUniqueDevices = attendanceRecords.Where(a => !string.IsNullOrEmpty(a.DeviceId))
                .Select(a => a.DeviceId).Distinct().Count(),
            OutOfAreaPercentage = totalRecords > 0 ? (double)outOfAreaCount / totalRecords * 100 : 0,
            TotalOutOfAreaEvents = outOfAreaCount
        };
    }

    /// <summary>
    /// Calculate overall system health score (0-100)
    /// </summary>
    private double CalculateSystemHealthScore(int successfulAttempts, int totalAttempts, double avgResponseTime)
    {
        if (totalAttempts == 0) return 100;

        var successRate = (double)successfulAttempts / totalAttempts;
        var performanceScore = avgResponseTime < 1000 ? 1.0 : Math.Max(0, 1.0 - (avgResponseTime - 1000) / 4000);
        
        return (successRate * 0.7 + performanceScore * 0.3) * 100;
    }

    /// <summary>
    /// Get distribution of biometric failures by category
    /// </summary>
    public async Task<List<FailureCategoryData>> GetFailureAnalysisAsync(DateTime start, DateTime end)
    {
        using var context = await _factory.CreateDbContextAsync();
        var logs = await context.FaceLogs
            .Where(l => l.Timestamp >= start && l.Timestamp <= end && !l.Success)
            .ToListAsync();

        return logs
            .GroupBy(l => l.FailureCategory ?? "Other")
            .Select(g => new FailureCategoryData
            {
                Category = g.Key,
                Count = g.Count(),
                Percentage = logs.Count > 0 ? (double)g.Count() / logs.Count * 100 : 0
            })
            .OrderByDescending(f => f.Count)
            .ToList();
    }

    /// <summary>
    /// Get "interesting" biometric attempts where faces match but users are different,
    /// or high-confidence failures.
    /// </summary>
    public async Task<List<SimilarityInsight>> GetSimilarityInsightsAsync(int count = 20)
    {
        using var context = await _factory.CreateDbContextAsync();
        var logs = await context.FaceLogs
            .Where(l => l.ConfidenceScore > 0.4)
            .OrderByDescending(l => l.Timestamp)
            .Take(count)
            .ToListAsync();

        var users = await context.Users.ToDictionaryAsync(u => u.Id);

        return logs.Select(l => new SimilarityInsight
        {
            Timestamp = l.Timestamp,
            AttemptedUserId = l.EmployeeId,
            AttemptedUserName = users.ContainsKey(l.EmployeeId) ? users[l.EmployeeId].Name : "Unknown",
            RecognizedUserId = l.RecognizedUserId,
            RecognizedUserEmail = l.RecognizedUserEmail,
            Confidence = l.ConfidenceScore,
            ImagePath = l.FaceImagePath,
            IsSuccess = l.Success,
            FailureCategory = l.FailureCategory
        }).ToList();
    }

    /// <summary>
    /// Get face login/verification attempts grouped by confidence band (High 80-100%, Medium 60-80%, Low 40-60%).
    /// </summary>
    public async Task<List<SimilarityGroupByConfidence>> GetSimilarityInsightsGroupedByConfidenceAsync(DateTime? start = null, DateTime? end = null, int maxPerBand = 20)
    {
        using var context = await _factory.CreateDbContextAsync();
        var query = context.FaceLogs.Where(l => l.ConfidenceScore >= 0.4);
        if (start.HasValue)
            query = query.Where(l => l.Timestamp >= start.Value);
        if (end.HasValue)
            query = query.Where(l => l.Timestamp <= end.Value);

        var logs = (await query.ToListAsync())
            .OrderByDescending(l => l.Timestamp)
            .ToList();
        var users = await context.Users.ToDictionaryAsync(u => u.Id);

        SimilarityInsight MapToInsight(FaceLog l) => new SimilarityInsight
        {
            Timestamp = l.Timestamp,
            AttemptedUserId = l.EmployeeId,
            AttemptedUserName = users.ContainsKey(l.EmployeeId) ? users[l.EmployeeId].Name : "Unknown",
            RecognizedUserId = l.RecognizedUserId,
            RecognizedUserEmail = l.RecognizedUserEmail,
            Confidence = l.ConfidenceScore,
            ImagePath = l.FaceImagePath,
            IsSuccess = l.Success,
            FailureCategory = l.FailureCategory
        };

        var high = logs.Where(l => l.ConfidenceScore >= 0.8).Take(maxPerBand).Select(MapToInsight).ToList();
        var medium = logs.Where(l => l.ConfidenceScore >= 0.6 && l.ConfidenceScore < 0.8).Take(maxPerBand).Select(MapToInsight).ToList();
        var low = logs.Where(l => l.ConfidenceScore >= 0.4 && l.ConfidenceScore < 0.6).Take(maxPerBand).Select(MapToInsight).ToList();

        return new List<SimilarityGroupByConfidence>
        {
            new() { Band = "High", Label = "80-100%", MinScore = 0.8, MaxScore = 1.0, Count = logs.Count(l => l.ConfidenceScore >= 0.8), Insights = high },
            new() { Band = "Medium", Label = "60-80%", MinScore = 0.6, MaxScore = 0.8, Count = logs.Count(l => l.ConfidenceScore >= 0.6 && l.ConfidenceScore < 0.8), Insights = medium },
            new() { Band = "Low", Label = "40-60%", MinScore = 0.4, MaxScore = 0.6, Count = logs.Count(l => l.ConfidenceScore >= 0.4 && l.ConfidenceScore < 0.6), Insights = low }
        };
    }

    public async Task<ActionableInsights> GetActionableInsightsAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        
        var pendingAbsences = await context.Absences.CountAsync(a => a.Status == "Pending");
        var pendingUserApprovals = await context.Users.CountAsync(u => u.Status == "Pending");
        
        var today = TimeHelper.GetToday();
        var tomorrow = today.AddDays(1);
        
        var attendanceToday = await context.Attendance
            .AsNoTracking()
            .Where(a => a.Date >= today && a.Date < tomorrow)
            .ToListAsync();
        
        var geofenceViolations = attendanceToday.Count(a => a.IsOutOfArea);
            
        var failureLogsToday = await context.AuditLogs
            .Where(a => a.Timestamp >= today && a.Result == "Failed" && a.ActionType == "Login")
            .ToListAsync();

        var criticalBiometricFailures = failureLogsToday
            .GroupBy(a => a.UserId)
            .Count(g => g.Count() >= 3);

        return new ActionableInsights
        {
            PendingAbsenceReviews = pendingAbsences,
            PendingUserApprovals = pendingUserApprovals,
            GeofenceViolationsToday = geofenceViolations,
            CriticalBiometricFailures = criticalBiometricFailures
        };
    }

    public async Task<List<UserReliabilityLog>> GetReliabilityInsightsAsync(int days = 30)
    {
        using var context = await _factory.CreateDbContextAsync();
        var startDate = TimeHelper.GetToday().AddDays(-days);

        // Frequent Latecomers
        var lateAttendance = await context.Attendance
            .AsNoTracking()
            .Where(a => a.Date >= startDate && a.Status == "Late")
            .ToListAsync();

        var latecomers = lateAttendance
            .GroupBy(a => a.EmployeeId)
            .Select(g => new
            {
                UserId = g.Key,
                LateCount = g.Count()
            })
            .OrderByDescending(x => x.LateCount)
            .Take(5)
            .ToList();

        // Frequent Absences (Unexplained)
        var unexplainedAbsences = await context.Absences
            .Where(a => a.Date >= startDate && a.Status == "Pending" && a.AbsenceType == "Unexplained")
            .ToListAsync();

        var absentees = unexplainedAbsences
            .GroupBy(a => a.EmployeeId)
            .Select(g => new
            {
                UserId = g.Key,
                AbsenceCount = g.Count()
            })
            .OrderByDescending(x => x.AbsenceCount)
            .Take(5)
            .ToList();

        // Combine and fetch user names efficiently
        var lateUserIds = latecomers.Select(x => x.UserId).ToList();
        var absenteeUserIds = absentees.Select(x => x.UserId).ToList();
        var allUserIds = lateUserIds.Concat(absenteeUserIds).Distinct().ToList();
        
        var usersDict = await context.Users
            .Where(u => allUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);
            
        var results = new List<UserReliabilityLog>();
        
        foreach (var item in latecomers) {
            var user = usersDict.GetValueOrDefault(item.UserId);
            results.Add(new UserReliabilityLog { 
                UserName = user != null ? $"{user.Name} {user.Surname}" : "Unknown",
                Metric = "Frequent Lateness",
                Value = item.LateCount.ToString() + " times",
                Severity = item.LateCount > 5 ? "High" : "Medium"
            });
        }
 
        foreach (var item in absentees) {
            var user = usersDict.GetValueOrDefault(item.UserId);
            results.Add(new UserReliabilityLog { 
                UserName = user != null ? $"{user.Name} {user.Surname}" : "Unknown",
                Metric = "Unexplained Absence",
                Value = item.AbsenceCount.ToString() + " days",
                Severity = item.AbsenceCount > 3 ? "High" : "Medium"
            });
        }

        return results;
    }
}

public class DashboardStats
{
    public int TotalWorkers { get; set; }
    public int FaceRegistered { get; set; }
    public int DocumentRegistered { get; set; }
    public int IdCardVerified { get; set; }
    public int WithConsent { get; set; }
    public int RegistrationsToday { get; set; }
    public int RegistrationsInRange { get; set; }
    public int RegistrationsThisWeek { get; set; }
    public int RegistrationsThisMonth { get; set; }
    public double LoginSuccessRate { get; set; }
    public double VerificationSuccessRate { get; set; }
    public int ActiveUsersToday { get; set; }
    public int PendingApprovals { get; set; }
    public double TotalOvertimeHours { get; set; }
    public int OvertimeCount { get; set; }
}

public class BranchDistribution
{
    public string BranchName { get; set; } = string.Empty;
    public int WorkerCount { get; set; }
}

public class RegistrationTrend
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
    public int FaceCount { get; set; }
    public int DocumentCount { get; set; }
}

public class IdCardVerificationStats
{
    public int Total { get; set; }
    public int Successful { get; set; }
    public int Failed { get; set; }
    public double SuccessRate { get; set; }
    public double AverageConfidence { get; set; }
    public List<IdCardVerification> RecentVerifications { get; set; } = new();
}

public class AttendanceStats
{
    public DateTime Date { get; set; }
    public int TotalEmployees { get; set; }
    public int PresentCount { get; set; }
    public int OutOfAreaCount { get; set; }
    public int AbsenceCount { get; set; }
    public double AttendanceRate { get; set; }
    public double TotalOvertimeHours { get; set; }
    public int OvertimeCount { get; set; }
}

public class FaceAccuracyStats
{
    public int TotalAttempts { get; set; }
    public int SuccessCount { get; set; }
    public double AverageConfidence { get; set; }
    public Dictionary<string, int> FailureReasons { get; set; } = new();
    public double SuccessRate => TotalAttempts > 0 ? (double)SuccessCount / TotalAttempts * 100 : 0;
}

public class BiometricHealthMetrics
{
    public int TotalFaceRecognitionAttempts { get; set; }
    public int SuccessfulRecognitions { get; set; }
    public int FailedRecognitions { get; set; }
    public double AverageConfidenceScore { get; set; }
    public double AverageResponseTimeMs { get; set; }
    public double LoginSuccessRate { get; set; }
    public int TotalLoginAttempts { get; set; }
    public int FailedLoginAttempts { get; set; }
    public Dictionary<double, int> ConfidenceDistribution { get; set; } = new();
    public double SystemHealthScore { get; set; }
    public double RecognitionSuccessRate => TotalFaceRecognitionAttempts > 0 
        ? (double)SuccessfulRecognitions / TotalFaceRecognitionAttempts * 100 
        : 0;
}

public class AttendanceTrendAnalysis
{
    public List<DailyAttendanceData> DailyData { get; set; } = new();
    public double AverageAttendanceRate { get; set; }
    public double AverageLateRate { get; set; }
    public int TotalWorkingDays { get; set; }
    public int TotalEmployees { get; set; }
}

public class DailyAttendanceData
{
    public DateTime Date { get; set; }
    public int TotalPresent { get; set; }
    public int OnTime { get; set; }
    public int Late { get; set; }
    public int OutOfArea { get; set; }
    public double AverageHours { get; set; }
    public double TotalOvertimeHours { get; set; }
    public double AttendanceRate { get; set; }
}

public class PeakUsageAnalysis
{
    public List<HourlyUsageData> HourlyClockInDistribution { get; set; } = new();
    public List<DayOfWeekData> DayOfWeekPattern { get; set; } = new();
    public int PeakHour { get; set; }
    public DayOfWeek BusiestDay { get; set; }
}

public class HourlyUsageData
{
    public int Hour { get; set; }
    public int Count { get; set; }
}

public class DayOfWeekData
{
    public DayOfWeek DayOfWeek { get; set; }
    public double AverageAttendance { get; set; }
}

public class DeviceLocationAnalytics
{
    public List<DeviceUsageData> TopDevices { get; set; } = new();
    public int TotalUniqueDevices { get; set; }
    public double OutOfAreaPercentage { get; set; }
    public int TotalOutOfAreaEvents { get; set; }
}

public class DeviceUsageData
{
    public string DeviceId { get; set; } = string.Empty;
    public int UsageCount { get; set; }
    public int UniqueUsers { get; set; }
}

public class FailureCategoryData
{
    public string Category { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
}

public class SimilarityInsight
{
    public DateTime Timestamp { get; set; }
    public string AttemptedUserId { get; set; } = string.Empty;
    public string AttemptedUserName { get; set; } = string.Empty;
    public string? RecognizedUserId { get; set; }
    public string? RecognizedUserEmail { get; set; }
    public double Confidence { get; set; }
    public string? ImagePath { get; set; }
    public bool IsSuccess { get; set; }
    public string? FailureCategory { get; set; }
}

public class SimilarityGroupByConfidence
{
    public string Band { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public double MinScore { get; set; }
    public double MaxScore { get; set; }
    public int Count { get; set; }
    public List<SimilarityInsight> Insights { get; set; } = new();
}

public class ActionableInsights
{
    public int PendingAbsenceReviews { get; set; }
    public int PendingUserApprovals { get; set; }
    public int GeofenceViolationsToday { get; set; }
    public int CriticalBiometricFailures { get; set; }
    public bool HasIssues => PendingAbsenceReviews > 0 || PendingUserApprovals > 0 || GeofenceViolationsToday > 0 || CriticalBiometricFailures > 0;
}

public class UserReliabilityLog
{
    public string UserName { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty; // High, Medium, Low
}
