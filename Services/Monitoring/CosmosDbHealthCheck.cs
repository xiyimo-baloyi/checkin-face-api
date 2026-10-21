using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Africanideasfaceapi.Data;

namespace Africanideasfaceapi.Services.Monitoring;

public class CosmosDbHealthCheck : IHealthCheck
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ILogger<CosmosDbHealthCheck> _logger;

    public CosmosDbHealthCheck(IDbContextFactory<AppDbContext> dbContextFactory, ILogger<CosmosDbHealthCheck> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            
            // Verify connectivity by attempting a minimal query.
            // EF Core Cosmos provider does not support CanConnectAsync.
            await dbContext.Users.FirstOrDefaultAsync(cancellationToken);

            return HealthCheckResult.Healthy("Cosmos DB is responsive.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cosmos DB health check failed.");
            return HealthCheckResult.Unhealthy("Cosmos DB health check failed.", ex);
        }
    }
}
