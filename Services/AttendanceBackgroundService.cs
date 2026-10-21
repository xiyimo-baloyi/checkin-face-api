using Africanideasfaceapi.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Africanideasfaceapi.Services;

public class AttendanceBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AttendanceBackgroundService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

    public AttendanceBackgroundService(IServiceProvider serviceProvider, ILogger<AttendanceBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Attendance Background Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Checking for stale attendance sessions at: {Time}", DateTimeOffset.Now);

                using (var scope = _serviceProvider.CreateScope())
                {
                    var attendanceService = scope.ServiceProvider.GetRequiredService<AttendanceService>();
                    int processedCount = await attendanceService.AutoClockOutCheckAsync();

                    if (processedCount > 0)
                    {
                        _logger.LogInformation("Successfully auto-clocked out {Count} sessions.", processedCount);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while running the attendance auto-clock-out check.");
            }

            // Wait for the next interval
            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Attendance Background Service is stopping.");
    }
}
