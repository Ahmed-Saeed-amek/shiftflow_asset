namespace ShiftFlow.Application.AI;

/// <summary>Per-user fixed-window throttle for the YouTube repair-video lookup, independent of
/// (and stricter than) the AI chat endpoint's own "ai" rate-limit policy. Registered as a
/// singleton so the window persists across requests/tool calls, unlike the request-scoped
/// AssetRepairGuidanceService that uses it.</summary>
public interface IYouTubeQuotaTracker
{
    bool TryConsume(string userId);
}

public sealed class YouTubeQuotaTracker : IYouTubeQuotaTracker
{
    private const int PermitLimit = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, (int Count, DateTime WindowStart)> _state = new();
    private readonly object _lock = new();

    public bool TryConsume(string userId)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (_state.TryGetValue(userId, out var entry) && now - entry.WindowStart < Window)
            {
                if (entry.Count >= PermitLimit) return false;
                _state[userId] = (entry.Count + 1, entry.WindowStart);
                return true;
            }
            _state[userId] = (1, now);
            return true;
        }
    }
}
