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

    // This tracker is a singleton, so without pruning every user id that ever called the tool
    // stayed in the dictionary for the process's lifetime — an unbounded leak keyed by user.
    private DateTime _lastPrune = DateTime.UtcNow;
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(5);

    public bool TryConsume(string userId)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            Prune(now);

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

    /// <summary>Drops entries whose window has elapsed (they are equivalent to absent ones).
    /// Sweeping on every call would be O(n) per request, so it is amortized over an interval;
    /// caller must hold _lock.</summary>
    private void Prune(DateTime now)
    {
        if (now - _lastPrune < PruneInterval) return;
        _lastPrune = now;

        var expired = _state.Where(kv => now - kv.Value.WindowStart >= Window)
                            .Select(kv => kv.Key)
                            .ToList();
        foreach (var key in expired) _state.Remove(key);
    }
}
