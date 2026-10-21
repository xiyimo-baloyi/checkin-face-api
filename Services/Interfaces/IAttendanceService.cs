using Africanideasfaceapi.Models;

namespace Africanideasfaceapi.Services.Interfaces;

public interface IAttendanceService
{
    Task<Attendance> ClockInAsync(string employeeId, double? lat, double? lon, bool isOutOfArea, string? deviceId);
    Task<Attendance?> ClockOutAsync(string employeeId);
    Task<Attendance?> GetAttendanceByIdAsync(string id);
    Task<List<Attendance>> GetEmployeeAttendanceAsync(string employeeId);
    Task<List<Attendance>> GetRecentAttendanceAsync(int count = 50);
    Task<Attendance?> GetTodayAttendanceAsync(string employeeId);
    Task<int> AutoClockOutCheckAsync();
}
