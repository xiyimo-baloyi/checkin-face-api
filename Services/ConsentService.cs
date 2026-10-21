using Africanideasfaceapi.Helpers;
using Africanideasfaceapi.Models;
using Africanideasfaceapi.Data;
using Microsoft.EntityFrameworkCore;

namespace Africanideasfaceapi.Services;

/// <summary>
/// Service for managing worker consent
/// </summary>
public class ConsentService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<ConsentService> _logger;
    private readonly AuditService? _auditService;

    public ConsentService(
        IDbContextFactory<AppDbContext> factory,
        ILogger<ConsentService> logger,
        AuditService? auditService = null)
    {
        _factory = factory;
        _logger = logger;
        _auditService = auditService;
    }

    /// <summary>
    /// Record consent for a worker
    /// </summary>
    public async Task<Consent> RecordConsentAsync(
        string workerId,
        string consentType,
        bool isConsented,
        string? ipAddress = null,
        string? userAgent = null,
        string? notes = null)
    {
        using var context = await _factory.CreateDbContextAsync();
        var consent = new Consent
        {
            WorkerId = workerId,
            ConsentType = consentType,
            IsConsented = isConsented,
            ConsentDate = TimeHelper.GetNow(),
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Notes = notes
        };

        context.Consents.Add(consent);
        
        try
        {
            await context.SaveChangesAsync();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error while saving consent: {Message}", ex.Message);
            
            // Check if it's a Cosmos DB "Not Found" error
            if (ex.InnerException?.Message?.Contains("Resource Not Found") == true ||
                ex.InnerException?.Message?.Contains("NotFound") == true)
            {
                throw new InvalidOperationException(
                    "The Cosmos DB container 'Consents' was not found. Please ensure the database and containers are initialized.",
                    ex);
            }
            throw;
        }

        // Update worker consent status
        var worker = await context.Workers
            .OrderByDescending(p => p.RegisteredDate)
            .FirstOrDefaultAsync(p => p.Id == workerId);
        if (worker != null && isConsented)
        {
            worker.HasConsent = true;
            worker.ConsentDate = TimeHelper.GetNow();
            
            // Also update User model for consistency
            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == workerId);
            if (user != null)
            {
                user.HasConsented = true;
                user.ConsentDate = worker.ConsentDate;
            }

            try
            {
                await context.SaveChangesAsync();
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error while updating consent status: {Message}", ex.Message);
            }
        }

        _logger.LogInformation("Consent recorded for worker {WorkerId}: Type={Type}, Consented={Consented}",
            workerId, consentType, isConsented);

        _auditService?.LogAdminAction(workerId, "system", "RecordConsent", workerId, consentType);

        return consent;
    }

    /// <summary>
    /// Withdraw consent for a worker
    /// </summary>
    public async Task<bool> WithdrawConsentAsync(string workerId, string consentType, string? notes = null)
    {
        using var context = await _factory.CreateDbContextAsync();
        var activeConsent = await context.Consents
            .Where(c => c.WorkerId == workerId 
                && c.ConsentType == consentType 
                && c.IsConsented 
                && c.WithdrawalDate == null)
            .OrderByDescending(c => c.ConsentDate)
            .FirstOrDefaultAsync();

        if (activeConsent != null)
        {
            activeConsent.IsConsented = false;
            activeConsent.WithdrawalDate = TimeHelper.GetNow();
            activeConsent.Notes = notes;
            await context.SaveChangesAsync();

            // Check if worker has any active consents
            var hasActiveConsent = await context.Consents
                .AnyAsync(c => c.WorkerId == workerId && c.IsConsented && c.WithdrawalDate == null);

            var worker = await context.Workers
            .OrderByDescending(p => p.RegisteredDate)
            .FirstOrDefaultAsync(p => p.Id == workerId);
            if (worker != null && !hasActiveConsent)
            {
                worker.HasConsent = false;
                
                // Also update User model for consistency
                var user = await context.Users.FirstOrDefaultAsync(u => u.Id == workerId);
                if (user != null)
                {
                    user.HasConsented = false;
                }

                await context.SaveChangesAsync();
            }

            _logger.LogInformation("Consent withdrawn for worker {WorkerId}: Type={Type}", workerId, consentType);
            _auditService?.LogAdminAction(workerId, "system", "WithdrawConsent", workerId, consentType);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Get consent history for a worker
    /// </summary>
    public async Task<List<Consent>> GetConsentHistoryAsync(string workerId)
    {
        using var context = await _factory.CreateDbContextAsync();
        return await context.Consents
            .Where(c => c.WorkerId == workerId)
            .OrderByDescending(c => c.ConsentDate)
            .ToListAsync();
    }

    /// <summary>
    /// Check if worker has active consent
    /// </summary>
    public async Task<bool> HasActiveConsentAsync(string workerId, string consentType = "All")
    {
        using var context = await _factory.CreateDbContextAsync();
        if (consentType == "All")
        {
            return await context.Consents
                .AnyAsync(c => c.WorkerId == workerId 
                    && c.IsConsented 
                    && c.WithdrawalDate == null);
        }

        return await context.Consents
            .AnyAsync(c => c.WorkerId == workerId 
                && c.ConsentType == consentType 
                && c.IsConsented 
                && c.WithdrawalDate == null);
    }
}
