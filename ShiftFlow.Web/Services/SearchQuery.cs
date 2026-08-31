namespace ShiftFlow.Web.Services;

/// <summary>
/// Caps a free-text search query before it's used in an EF Core Contains()/LIKE filter against a
/// fixed-length nvarchar column (e.g. WorkOrderNumber is nvarchar(30)). Confirmed live: pasting a
/// long (e.g. 5000-character) string into a search box raised an unhandled
/// "String or binary data would be truncated" SqlException, returning a raw stack trace and
/// server file path straight to the client. No search term meaningfully exceeds this length
/// anyway, so cap it well below any of the columns it's compared against instead of trying to
/// track each column's own max length at every call site.
/// </summary>
public static class SearchQuery
{
    public const int MaxLength = 100;

    public static string? Cap(string? q) =>
        string.IsNullOrEmpty(q) || q.Length <= MaxLength ? q : q[..MaxLength];
}
