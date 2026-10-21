using Africanideasfaceapi.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Africanideasfaceapi.Services.Interfaces;

public interface IAnalyticsService
{
    Task<DashboardStats> GetDashboardStatsAsync(int days = 30);
    Task<List<BranchDistribution>> GetBranchDistributionAsync();
    Task<List<AuditLog>> GetRecentActivityAsync(int count = 10);
    Task<List<RegistrationTrend>> GetRegistrationTrendsAsync(int days = 30);
    Task<IdCardVerificationStats> GetIdCardVerificationStatsAsync();
    Task<AttendanceStats> GetDailyAttendanceStatsAsync(DateTime date);
    Task<List<AuditLog>> GetDetailedLoginLogsAsync(int count = 50);
    Task<List<FaceLog>> GetHighConfidenceAttemptsAsync(double minConfidence = 0.4, int count = 50);
    Task<FaceAccuracyStats> GetFaceAccuracyStatsAsync(DateTime start, DateTime end);
    Task<BiometricHealthMetrics> GetBiometricHealthMetricsAsync(DateTime start, DateTime end);
    Task<AttendanceTrendAnalysis> GetAttendanceTrendsAsync(DateTime start, DateTime end);
    Task<PeakUsageAnalysis> GetPeakUsageAnalysisAsync(DateTime start, DateTime end);
    Task<DeviceLocationAnalytics> GetDeviceLocationAnalyticsAsync(DateTime start, DateTime end);
    Task<List<FailureCategoryData>> GetFailureAnalysisAsync(DateTime start, DateTime end);
    Task<List<SimilarityInsight>> GetSimilarityInsightsAsync(int count = 20);
    Task<List<SimilarityGroupByConfidence>> GetSimilarityInsightsGroupedByConfidenceAsync(DateTime? start = null, DateTime? end = null, int maxPerBand = 20);
    Task<ActionableInsights> GetActionableInsightsAsync();
    Task<List<UserReliabilityLog>> GetReliabilityInsightsAsync(int days = 30);
}
