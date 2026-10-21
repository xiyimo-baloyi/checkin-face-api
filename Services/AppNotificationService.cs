using Africanideasfaceapi.Helpers;
using Africanideasfaceapi.Models;
using Africanideasfaceapi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Africanideasfaceapi.Hubs;

namespace Africanideasfaceapi.Services;

public class AppNotificationService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<AppNotificationService> _logger;
    private readonly EmailService _emailService;
    private readonly IHubContext<AnalyticsHub> _hubContext;

    public AppNotificationService(
        IDbContextFactory<AppDbContext> factory, 
        ILogger<AppNotificationService> logger,
        EmailService emailService,
        IHubContext<AnalyticsHub> hubContext)
    {
        _factory = factory;
        _logger = logger;
        _emailService = emailService;
        _hubContext = hubContext;
    }

    public async Task CreateNotificationAsync(string recipientId, string title, string message, string type, string? email = null)
    {
        using var context = await _factory.CreateDbContextAsync();
        await CreateNotificationAsync(context, recipientId, title, message, type, email);
    }

    private async Task CreateNotificationAsync(AppDbContext context, string recipientId, string title, string message, string type, string? email = null)
    {
        var notification = new Notification
        {
            RecipientId = recipientId,
            RecipientEmail = email,
            Title = title,
            Message = message,
            Type = type,
            SentAt = TimeHelper.GetNow()
        };

        context.Notifications.Add(notification);
        await context.SaveChangesAsync();

        // Broadcast to user in real-time
        try
        {
            await _hubContext.Clients.All.SendAsync("NotificationReceived", recipientId, notification);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast real-time notification to {RecipientId}", recipientId);
        }

        // Trigger real-time email if email is provided
        if (!string.IsNullOrEmpty(email))
        {
            try
            {
                await _emailService.SendEmailAsync(email, title, message);
                _logger.LogInformation("Notification sent to {Email}: {Title}", email, title);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send notification email to {Email}", email);
            }
        }
    }

    public async Task<List<Notification>> GetUserNotificationsAsync(string userId)
    {
        using var context = await _factory.CreateDbContextAsync();
        return await context.Notifications
            .Where(n => n.RecipientId == userId)
            .OrderByDescending(n => n.SentAt)
            .Take(50)
            .ToListAsync();
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        using var context = await _factory.CreateDbContextAsync();
        return await context.Notifications
            .Where(n => n.RecipientId == userId && !n.IsRead)
            .CountAsync();
    }

    public async Task MarkAsReadAsync(string notificationId)
    {
        using var context = await _factory.CreateDbContextAsync();
        var notification = await context.Notifications.FindAsync(notificationId);
        if (notification != null)
        {
            notification.IsRead = true;
            await context.SaveChangesAsync();
        }
    }

    public async Task DeleteNotificationAsync(string notificationId)
    {
        using var context = await _factory.CreateDbContextAsync();
        var notification = await context.Notifications.FindAsync(notificationId);
        if (notification != null)
        {
            context.Notifications.Remove(notification);
            await context.SaveChangesAsync();
        }
    }

    public async Task AlertLateArrivalAsync(User user, DateTime clockInTime)
    {
        if (user != null && user.WorkStartTime.HasValue)
        {
            try
            {
                var scheduledStartTime = user.WorkStartTime.Value;
                var actualTime = clockInTime.TimeOfDay;

                if (actualTime > scheduledStartTime.Add(TimeSpan.FromMinutes(15))) // 15-minute grace period
                {
                    var title = "Late Arrival Alert";
                    var message = $"User {user.Name} clocked in late at {clockInTime.ToLocalTime():HH:mm}. Scheduled: {scheduledStartTime:hh\\:mm}.";
                    
                    // Alert managers / Admins
                    using var context = await _factory.CreateDbContextAsync();
                    var admins = await context.Users.Where(u => u.Role == "Admin" && u.IsActive == true).ToListAsync();
                    foreach (var admin in admins)
                    {
                        await CreateNotificationAsync(context, admin.Id, title, message, "LateArrival", admin.Email);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing late arrival alert for user {UserId}", user.Id);
            }
        }
    }

    public async Task AlertLowConfidenceAsync(User user, double confidenceScore)
    {
        var title = "Low Confidence Recognition Alert";
        var message = $"User {user.Name} recognized with low confidence score: {confidenceScore:F2}. Verify identity manually.";
        
        // Alert Admins
        using var context = await _factory.CreateDbContextAsync();
        var admins = await context.Users.Where(u => u.Role == "Admin").ToListAsync();
        foreach (var admin in admins)
        {
            await CreateNotificationAsync(context, admin.Id, title, message, "LowConfidence", admin.Email);
        }
    }
}
