using Africanideasfaceapi.Data;
using Africanideasfaceapi.Helpers;
using Africanideasfaceapi.Models;
using Microsoft.EntityFrameworkCore;

namespace Africanideasfaceapi.Services;

public class SettingsService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<SettingsService> _logger;
    private const string SETTINGS_ID = "1";

    public SettingsService(IDbContextFactory<AppDbContext> factory, ILogger<SettingsService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<SystemSettings> GetSettingsAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        return await GetSettingsAsync(context);
    }

    private async Task<SystemSettings> GetSettingsAsync(AppDbContext context)
    {
        try
        {
            var settings = await context.SystemSettings.FindAsync(SETTINGS_ID);
            if (settings == null)
            {
                settings = new SystemSettings { Id = SETTINGS_ID };
                // Add default SA holidays for current year
                var holidayService = new HolidayService(); 
                var defaults = holidayService.GetPublicHolidays(TimeHelper.GetNow().Year);
                settings.PublicHolidays = defaults.Select(h => new PublicHoliday { Date = h.Date, Name = h.Name }).ToList();
                
                context.SystemSettings.Add(settings);
                await context.SaveChangesAsync();
            }
            
            if (settings != null)
            {
                settings.PublicHolidays ??= new List<PublicHoliday>();
                settings.EnableOfflineAccess ??= true;
            }
            
            return settings ?? new SystemSettings { Id = SETTINGS_ID };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching system settings");
            return new SystemSettings(); // Fallback
        }
    }

    public async Task UpdateSettingsAsync(SystemSettings settings)
    {
        using var context = await _factory.CreateDbContextAsync();
        settings.Id = SETTINGS_ID; // Ensure ID is maintained
        context.SystemSettings.Update(settings);
        await context.SaveChangesAsync();
    }

    public async Task AddHolidayAsync(DateTime date, string name)
    {
        var settings = await GetSettingsAsync();
        if (!settings.PublicHolidays.Any(h => h.Date.Date == date.Date))
        {
            settings.PublicHolidays.Add(new PublicHoliday { Date = date, Name = name });
            await UpdateSettingsAsync(settings);
        }
    }

    public async Task RemoveHolidayAsync(DateTime date)
    {
        var settings = await GetSettingsAsync();
        var holiday = settings.PublicHolidays.FirstOrDefault(h => h.Date.Date == date.Date);
        if (holiday != null)
        {
            settings.PublicHolidays.Remove(holiday);
            await UpdateSettingsAsync(settings);
        }
    }
}
