using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Africanideasfaceapi.Helpers;

/// <summary>
/// Provides custom resilience patterns (retries with exponential backoff)
/// to handle transient failures in external APIs and databases.
/// </summary>
public static class ResilienceHelper
{
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> action,
        ILogger logger,
        string operationName,
        int maxRetries = 3,
        int initialDelayMs = 1000)
    {
        int retryCount = 0;
        Exception? lastException = null;

        while (true)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                lastException = ex;
                retryCount++;

                if (retryCount >= maxRetries)
                {
                    logger.LogError(ex, "[RESILIENCE] {Operation} failed after {Count} retries.", operationName, retryCount);
                    throw;
                }

                int delay = initialDelayMs * (int)Math.Pow(2, retryCount - 1);
                logger.LogWarning(ex, "[RESILIENCE] {Operation} failed. Retrying in {Delay}ms... (Attempt {Count}/{Max})", 
                    operationName, delay, retryCount, maxRetries);

                await Task.Delay(delay);
            }
        }
    }

    public static async Task ExecuteWithRetryAsync(
        Func<Task> action,
        ILogger logger,
        string operationName,
        int maxRetries = 3,
        int initialDelayMs = 1000)
    {
        await ExecuteWithRetryAsync<object?>(async () => 
        {
            await action();
            return null;
        }, logger, operationName, maxRetries, initialDelayMs);
    }

    private static bool IsTransient(Exception ex)
    {
        // Add logic to detect transient errors (e.g. HTTP 429, 5xx, or network timeouts)
        // For Cosmos DB / Face API, certain status codes signify transient issues.
        
        string message = ex.ToString().ToLower();
        
        // Face API / Generic HTTP Transient
        if (message.Contains("429") || message.Contains("500") || message.Contains("502") || message.Contains("503") || message.Contains("504"))
            return true;
            
        // Network / Timeout
        if (ex is TaskCanceledException || ex is HttpRequestException || ex is TimeoutException)
            return true;

        // Cosmos DB specific transient (if using Microsoft.Azure.Cosmos directly)
        if (message.Contains("requesttimeout") || message.Contains("servicenotavailable"))
            return true;

        return false;
    }
}
