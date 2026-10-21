namespace Africanideasfaceapi.DTOs;

/// <summary>
/// Analytics dashboard DTO
/// </summary>
public class AnalyticsDashboardDto
{
    public int TotalWorkers { get; set; }
    public int FaceRegistered { get; set; }
    public int DocumentRegistered { get; set; }
    public int IdCardVerified { get; set; }
    public int WithConsent { get; set; }
    public int RegistrationsToday { get; set; }
    public int RegistrationsThisWeek { get; set; }
    public int RegistrationsThisMonth { get; set; }
    public double LoginSuccessRate { get; set; }
    public double VerificationSuccessRate { get; set; }
    public List<RegistrationTrendDto> RegistrationTrends { get; set; } = new();
}

/// <summary>
/// Registration trend DTO
/// </summary>
public class RegistrationTrendDto
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
    public int FaceCount { get; set; }
    public int DocumentCount { get; set; }
}
