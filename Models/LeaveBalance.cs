using Africanideasfaceapi.Helpers;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Models;

/// <summary>
/// Leave balance tracking for employees
/// </summary>
public class LeaveBalance
{
    [Key]
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [Required]
    public string EmployeeId { get; set; } = string.Empty;
    
    public int Year { get; set; } = TimeHelper.GetNow().Year;
    
    // Annual Leave
    public double AnnualLeaveTotal { get; set; } = 15; // Default 15 days per year
    public double AnnualLeaveUsed { get; set; } = 0;
    [NotMapped]
    public double AnnualLeaveAvailable => AnnualLeaveTotal - AnnualLeaveUsed;
    
    // Sick Leave
    public double SickLeaveTotal { get; set; } = 30; // Default 30 days per 3-year cycle
    public double SickLeaveUsed { get; set; } = 0;
    [NotMapped]
    public double SickLeaveAvailable => SickLeaveTotal - SickLeaveUsed;
    
    // Family Responsibility Leave (BCEA: 3 days per year)
    public double FamilyLeaveTotal { get; set; } = 3; 
    public double FamilyLeaveUsed { get; set; } = 0;
    [NotMapped]
    public double FamilyLeaveAvailable => FamilyLeaveTotal - FamilyLeaveUsed;
    
    // Parental Leave (BCEA: 10 consecutive days)
    public double ParentalLeaveTotal { get; set; } = 10;
    public double ParentalLeaveUsed { get; set; } = 0;
    [NotMapped]
    public double ParentalLeaveAvailable => ParentalLeaveTotal - ParentalLeaveUsed;

    // Maternity Leave (BCEA: 4 consecutive months)
    public double MaternityLeaveTotal { get; set; } = 120; // Default ~4 months
    public double MaternityLeaveUsed { get; set; } = 0;
    [NotMapped]
    public double MaternityLeaveAvailable => MaternityLeaveTotal - MaternityLeaveUsed;

    // Study Leave (Company Benefit)
    public double StudyLeaveTotal { get; set; } = 5; 
    public double StudyLeaveUsed { get; set; } = 0;
    [NotMapped]
    public double StudyLeaveAvailable => StudyLeaveTotal - StudyLeaveUsed;

    // Out Leave (Company Benefit / Off-site)
    public double OutLeaveTotal { get; set; } = 5; 
    public double OutLeaveUsed { get; set; } = 0;
    [NotMapped]
    public double OutLeaveAvailable => OutLeaveTotal - OutLeaveUsed;
    
    // Unpaid Leave (tracked but not limited)
    public double UnpaidLeaveUsed { get; set; } = 0;
    
    public DateTime LastUpdated { get; set; } = TimeHelper.GetNow();
}
