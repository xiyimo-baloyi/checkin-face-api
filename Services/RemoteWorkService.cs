using Africanideasfaceapi.Data;
using Africanideasfaceapi.Models;
using Microsoft.EntityFrameworkCore;

namespace Africanideasfaceapi.Services;

public class RemoteWorkService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<RemoteWorkService> _logger;

    public RemoteWorkService(IDbContextFactory<AppDbContext> factory, ILogger<RemoteWorkService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<List<RemoteWorkRequest>> GetPendingRequestsAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        return (await context.RemoteWorkRequests
            .Where(r => r.Status == "Pending")
            .ToListAsync())
            .OrderBy(r => r.Date)
            .ToList();
    }

    public async Task<List<RemoteWorkRequest>> GetAllRequestsAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        return (await context.RemoteWorkRequests
            .ToListAsync())
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
    }

    public async Task<List<RemoteWorkRequest>> GetUserRequestsAsync(string userId)
    {
        using var context = await _factory.CreateDbContextAsync();
        return (await context.RemoteWorkRequests
            .Where(r => r.UserId == userId)
            .ToListAsync())
            .OrderByDescending(r => r.Date)
            .ToList();
    }

    public async Task<RemoteWorkRequest?> GetRequestByIdAsync(string id)
    {
        using var context = await _factory.CreateDbContextAsync();
        return await context.RemoteWorkRequests.FindAsync(id);
    }

    public async Task SubmitRequestAsync(RemoteWorkRequest request)
    {
        using var context = await _factory.CreateDbContextAsync();
        context.RemoteWorkRequests.Add(request);
        await context.SaveChangesAsync();
        _logger.LogInformation("Submitted remote work request for user {UserId} on {Date}", request.UserId, request.Date);
    }

    public async Task ApproveRequestAsync(string id, string? comment)
    {
        using var context = await _factory.CreateDbContextAsync();
        var request = await context.RemoteWorkRequests.FindAsync(id);
        if (request != null)
        {
            request.Status = "Approved";
            request.AdminComment = comment;
            await context.SaveChangesAsync();
            _logger.LogInformation("Approved remote work request {RequestId}", id);
        }
    }

    public async Task RejectRequestAsync(string id, string? comment)
    {
        using var context = await _factory.CreateDbContextAsync();
        var request = await context.RemoteWorkRequests.FindAsync(id);
        if (request != null)
        {
            request.Status = "Rejected";
            request.AdminComment = comment;
            await context.SaveChangesAsync();
            _logger.LogInformation("Rejected remote work request {RequestId}", id);
        }
    }
}
