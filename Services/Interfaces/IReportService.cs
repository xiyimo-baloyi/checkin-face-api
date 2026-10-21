namespace Africanideasfaceapi.Services.Interfaces;

public interface IReportService
{
    Task<byte[]> GenerateAttendancePdfAsync(DateTime startDate, DateTime endDate);
    Task<byte[]> GenerateAuditLogPdfAsync(DateTime startDate, DateTime endDate);
    Task<byte[]> GenerateAttendanceExcelAsync(DateTime startDate, DateTime endDate);
    Task<byte[]> GenerateUserDirectoryExcelAsync();
    Task<byte[]> GenerateAccessLogPdfAsync(DateTime startDate, DateTime endDate);
    Task<byte[]> GenerateAccessLogExcelAsync(DateTime startDate, DateTime endDate);
}
