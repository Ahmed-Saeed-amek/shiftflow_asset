using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;

namespace ShiftFlow.Application.Services;

/// <summary>The one order-number allocator for Work Orders, Inspection Orders and Maintenance
/// Orders. Numbers are "{Prefix}-{year}-{seq:D4}" and the sequence is claimed with a
/// compare-and-swap UPDATE on <see cref="OrderNumberSequence"/>, so the value returned is owned
/// exclusively by the caller — no "load every number for the year, take the max" scan, and no
/// blanket DbUpdateException swallowing.</summary>
public static class OrderNumberGenerator
{
    private const int MaxAttempts = 10;

    public static string Format(string prefix, int year, int value) => $"{prefix}-{year}-{value:D4}";

    /// <summary>Claims and returns the next number for (prefix, year). Never touches the caller's
    /// change tracker, so it is safe to call before the order entity itself is added.</summary>
    public static async Task<string> NextAsync(ApplicationDbContext db, string prefix, int year)
    {
        if (string.IsNullOrWhiteSpace(prefix)) prefix = OrderNumberPrefixes.Inspection;
        prefix = prefix.Trim();

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var current = await db.Set<OrderNumberSequence>().AsNoTracking()
                .Where(s => s.Prefix == prefix && s.Year == year)
                .Select(s => new { s.Id, s.LastValue })
                .FirstOrDefaultAsync();

            if (current == null)
            {
                await TrySeedAsync(db, prefix, year);
                continue;
            }

            var next = current.LastValue + 1;
            // Compare-and-swap: only the caller whose UPDATE matches the value it read owns `next`.
            var rows = await db.Set<OrderNumberSequence>()
                .Where(s => s.Id == current.Id && s.LastValue == current.LastValue)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastValue, next));
            if (rows == 1) return Format(prefix, year, next);
            // Lost the race to a concurrent claim — re-read and try again.
        }
        throw new InvalidOperationException("Could not allocate an order number — please try again.");
    }

    /// <summary>Inserts the counter row for a prefix/year that has never been used, starting past
    /// any numbers already issued by the pre-sequence numbering scheme. Raw SQL so nothing lands in
    /// the caller's change tracker; a concurrent seed is expected and simply retried.</summary>
    private static async Task TrySeedAsync(ApplicationDbContext db, string prefix, int year)
    {
        var baseline = await BaselineAsync(db, prefix, year);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO [OrderNumberSequences] ([Prefix],[Year],[LastValue]) VALUES ({0},{1},{2})",
                prefix, year, baseline);
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            // Another request seeded the same prefix/year first — the retry loop picks it up.
        }
    }

    /// <summary>Highest sequence already used by existing rows for this prefix/year, across all
    /// three order tables (they share the prefix namespace). Runs once per prefix/year, only while
    /// seeding, so the in-memory scan never happens on a normal create.</summary>
    private static async Task<int> BaselineAsync(ApplicationDbContext db, string prefix, int year)
    {
        var start = $"{prefix}-{year}-";
        var numbers = new List<string>();
        numbers.AddRange(await db.WorkOrders.AsNoTracking()
            .Where(w => w.WorkOrderNumber.StartsWith(start)).Select(w => w.WorkOrderNumber).ToListAsync());
        numbers.AddRange(await db.InspectionOrders.AsNoTracking()
            .Where(o => o.OrderNumber.StartsWith(start)).Select(o => o.OrderNumber).ToListAsync());
        numbers.AddRange(await db.MaintenanceOrders.AsNoTracking()
            .Where(m => m.OrderNumber.StartsWith(start)).Select(m => m.OrderNumber).ToListAsync());

        var max = 0;
        foreach (var n in numbers)
            if (int.TryParse(n.AsSpan(start.Length), out var s) && s > max) max = s;
        return max;
    }

    /// <summary>Only a real unique/PK violation (SQL Server 2601/2627, SQLite constraint 19) counts
    /// as "someone else got there first" — every other database error propagates.</summary>
    internal static bool IsDuplicateKey(Exception? ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 2601 || sql.Number == 2627)) return true;
            // SQLite (tests) — read by reflection so the web project needn't reference the provider.
            if (e is DbException && e.GetType().Name == "SqliteException"
                && e.GetType().GetProperty("SqliteErrorCode")?.GetValue(e) is int code && (code == 19 || code == 2067))
                return true;
        }
        return false;
    }
}
