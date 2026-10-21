using Microsoft.Extensions.Caching.Memory;

namespace Africanideasfaceapi.Services;

public class LivenessSessionService
{
    private const string CachePrefix = "liveness:";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(5);

    private readonly IMemoryCache _cache;

    public LivenessSessionService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public string CreateSession()
    {
        var token = Guid.NewGuid().ToString("N");
        _cache.Set(CachePrefix + token, new LivenessSessionState { CreatedUtc = DateTime.UtcNow }, SessionLifetime);
        return token;
    }

    public void MarkCompleted(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        _cache.Set(
            CachePrefix + token,
            new LivenessSessionState { CreatedUtc = DateTime.UtcNow, CompletedUtc = DateTime.UtcNow },
            SessionLifetime);
    }

    public bool ValidateAndConsume(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var key = CachePrefix + token;
        if (!_cache.TryGetValue(key, out LivenessSessionState? state) || state?.CompletedUtc == null)
        {
            return false;
        }

        _cache.Remove(key);
        return true;
    }

    private sealed class LivenessSessionState
    {
        public DateTime CreatedUtc { get; init; }
        public DateTime? CompletedUtc { get; init; }
    }
}
