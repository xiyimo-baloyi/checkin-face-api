using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Africanideasfaceapi.Services;

public class FaceApiHealthCheck : IHealthCheck
{
    private readonly FaceApiService _faceApiService;
    private readonly ILogger<FaceApiHealthCheck> _logger;

    public FaceApiHealthCheck(FaceApiService faceApiService, ILogger<FaceApiHealthCheck> logger)
    {
        _faceApiService = faceApiService;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_faceApiService == null)
            {
                return HealthCheckResult.Unhealthy("FaceApiService is not initialized");
            }

            // Ensure PersonGroup is ready so readiness implies Face API is warm (reduces morning cold-start failures)
            await _faceApiService.EnsurePersonGroupReadyAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy("Azure Face API is accessible");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Face API health check failed");
            return HealthCheckResult.Unhealthy("Azure Face API health check failed", ex);
        }
    }
}
