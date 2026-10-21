namespace Africanideasfaceapi.Services;

/// <summary>
/// Keeps the app and dependencies (Cosmos, Face API) warm by periodically calling the readiness endpoint.
/// Reduces morning cold-start failures when the first user hits the app after overnight recycle.
/// </summary>
public class WarmupService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WarmupService> _logger;

    public WarmupService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WarmupService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Calls /health/ready to warm the app and its dependencies. Call this from a recurring job (e.g. every 10 minutes).
    /// </summary>
    public async Task RunWarmupAsync()
    {
        var baseUrl = (_configuration["AppBaseUrl"] ?? "").TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
        {
            _logger.LogDebug("Warmup skipped: AppBaseUrl not set.");
            return;
        }

        var url = $"{baseUrl}/health/ready";
        int maxRetries = 3;
        int delaySeconds = 5;

        for (int i = 0; i <= maxRetries; i++)
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                var response = await client.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Warmup successful: {Url} returned {Status}.", url, response.StatusCode);
                    return; // Success!
                }
                
                _logger.LogWarning("Warmup attempt {Attempt}/{Max}: {Url} returned {Status}.", i + 1, maxRetries + 1, url, response.StatusCode);
            }
            catch (Exception ex)
            {
                if (i == maxRetries)
                {
                    _logger.LogError(ex, "Warmup failed after {Max} attempts: {Url}.", maxRetries + 1, url);
                }
                else
                {
                    _logger.LogWarning("Warmup attempt {Attempt} failed: {Message}. Retrying in {Delay}s...", i + 1, ex.Message, delaySeconds);
                }
            }

            if (i < maxRetries)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                delaySeconds *= 2; // Exponential backoff
            }
        }
    }
}
