using Africanideasfaceapi.Models;

namespace Africanideasfaceapi.Services.Interfaces;

public interface IAbsenceService
{
    Task<int> RunAbsenceDetectionAsync(DateTime date);
    Task<bool> SubmitReasonAsync(string absenceId, string reason, string type);
    Task<bool> ApproveAbsenceAsync(string absenceId);
    Task<bool> RejectAbsenceAsync(string absenceId);
    Task<List<Absence>> GetEmployeeAbsencesAsync(string employeeId);
    Task<List<Absence>> GetPendingAbsencesAsync();
    Task<List<Absence>> GetAllAbsencesAsync();
    Task<string> GenerateDailySummaryAsync(DateTime date);
}
