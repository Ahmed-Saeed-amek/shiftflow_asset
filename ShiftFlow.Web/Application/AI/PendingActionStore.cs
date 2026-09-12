using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace ShiftFlow.Application.AI;

/// <summary>A destructive or bulk action the assistant has described but not yet performed,
/// waiting on the user pressing Confirm (or saying "yes, do it"). <see cref="Execute"/> closes over
/// the already-validated arguments (ids, not model text), so nothing the model said is re-parsed at
/// confirm time.
///
/// Execute takes an IServiceProvider rather than capturing services directly: confirmation arrives
/// on a *later* HTTP request, and a captured request-scoped DbContext/service would be disposed by
/// then. The confirming request hands in its own scope, so every service is resolved fresh.
/// <see cref="RequiredPermission"/> is re-checked at that point too — permissions can be revoked
/// between proposing and confirming.</summary>
public sealed record PendingAction(
    string Token,
    string UserId,
    DateTime CreatedAtUtc,
    string Description,
    string? RequiredPermission,
    Func<IServiceProvider, CancellationToken, Task<object>> Execute);

public interface IPendingActionStore
{
    /// <summary>Registers an action and returns its single-use token (32 URL-safe chars).</summary>
    string Create(string userId, string description, string? requiredPermission, Func<IServiceProvider, CancellationToken, Task<object>> execute);

    /// <summary>Looks the token up without consuming it. Returns null when it is unknown, expired,
    /// or owned by a different user.</summary>
    PendingAction? Peek(string token, string userId);

    /// <summary>Consumes the token. Returns null on the same three conditions as
    /// <see cref="Peek"/>; a token owned by someone else is left in place rather than destroyed,
    /// so one user can never cancel another's pending action by guessing its token.</summary>
    PendingAction? Take(string token, string userId);

    /// <summary>Drops a pending action the user declined. Returns false if there was nothing of
    /// theirs to drop.</summary>
    bool Discard(string token, string userId);
}

/// <summary>IMemoryCache-backed, 10-minute sliding-free (absolute) TTL, process-local. Tokens are
/// short-lived by design: a stale "delete these 40 orders" confirmation must not stay armed.
/// Process-local means a web-farm deployment needs a distributed store instead — see
/// NOTES-ai-backend.md.</summary>
public sealed class PendingActionStore : IPendingActionStore
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private const string KeyPrefix = "ai:pending:";

    private readonly IMemoryCache _cache;
    public PendingActionStore(IMemoryCache cache) => _cache = cache;

    public string Create(string userId, string description, string? requiredPermission, Func<IServiceProvider, CancellationToken, Task<object>> execute)
    {
        var token = NewToken();
        var action = new PendingAction(token, userId, DateTime.UtcNow, description, requiredPermission, execute);
        _cache.Set(KeyPrefix + token, action, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl });
        return token;
    }

    public PendingAction? Peek(string token, string userId) =>
        !string.IsNullOrEmpty(token)
        && _cache.TryGetValue(KeyPrefix + token, out PendingAction? action)
        && action is not null
        && action.UserId == userId
            ? action : null;

    public PendingAction? Take(string token, string userId)
    {
        var action = Peek(token, userId);
        if (action is null) return null;
        _cache.Remove(KeyPrefix + token);
        return action;
    }

    public bool Discard(string token, string userId)
    {
        if (Peek(token, userId) is null) return false;
        _cache.Remove(KeyPrefix + token);
        return true;
    }

    /// <summary>32 chars from a 24-byte cryptographic random, base64url-encoded (no padding, no
    /// '+'/'/' so it survives being echoed through JSON/URLs unescaped).</summary>
    private static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
