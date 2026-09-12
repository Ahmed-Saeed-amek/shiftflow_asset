using ShiftFlow.Web.Services;

namespace ShiftFlow.Web.Localization;

public static class LocalizeExtensions
{
    /// <summary>Returns the Arabic name when RTL is active and one exists, otherwise the English name.</summary>
    public static string LocalizedName(this ILanguageService loc, string en, string? ar) =>
        loc.IsRTL && !string.IsNullOrWhiteSpace(ar) ? ar! : en;

    /// <summary>
    /// Display label for an identity role name — Arabic when the page is Arabic and the role has an
    /// Arabic name, otherwise the PascalCase name humanized ("OperationsManager" → "Operations
    /// Manager"). Use this everywhere a role is shown to a user; never render ApplicationRole.Name
    /// raw. See <see cref="RoleDisplay"/>.
    /// </summary>
    public static string Role(this ILanguageService loc, string roleName, string? nameAr = null) =>
        RoleDisplay.Name(roleName, nameAr, loc.Lang);
}
