namespace ShiftFlow.Web.Localization;

public static class LocalizeExtensions
{
    /// <summary>Returns the Arabic name when RTL is active and one exists, otherwise the English name.</summary>
    public static string LocalizedName(this ILanguageService loc, string en, string? ar) =>
        loc.IsRTL && !string.IsNullOrWhiteSpace(ar) ? ar! : en;
}
