using System.Text;

namespace ShiftFlow.Web.Services;

/// <summary>
/// Roles are stored as PascalCase identity names ("OperationsManager", "HR") because that is what
/// [Authorize(Roles = …)] and the seed data use — but showing that raw string to a user reads as a
/// code constant leaking through the UI. This turns it into a display label, and prefers the
/// role's Arabic name when the page is Arabic and one is stored.
///
/// Call it through <c>Loc.Role(name, nameAr)</c> in views rather than directly.
/// </summary>
public static class RoleDisplay
{
    /// <summary>
    /// Arabic name when <paramref name="lang"/> is "ar" and one exists, otherwise the English role
    /// name with spaces inserted before interior capitals ("OperationsManager" → "Operations
    /// Manager"). Runs of capitals are kept together so acronyms survive ("HRManager" → "HR
    /// Manager", "HR" → "HR"), and a name that already contains spaces is returned untouched.
    /// </summary>
    public static string Name(string roleName, string? nameAr, string lang)
    {
        if (string.Equals(lang, "ar", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(nameAr))
            return nameAr!;
        return Humanize(roleName);
    }

    /// <summary>"OperationsManager" → "Operations Manager". Public so non-view code (exports,
    /// PDF/Excel headers) can produce the same label without going through ILanguageService.</summary>
    public static string Humanize(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return string.Empty;
        var name = roleName.Trim();
        if (name.Contains(' ')) return name;

        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            // Break before a capital that starts a new word: either the previous char is
            // lowercase/digit ("OperationsManager"), or this capital begins a word after an
            // acronym ("HRManager" — break before the M, not between H and R).
            if (i > 0 && char.IsUpper(c) &&
                (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
