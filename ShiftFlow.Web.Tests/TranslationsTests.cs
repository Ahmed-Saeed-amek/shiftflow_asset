using System.Text.RegularExpressions;
using ShiftFlow.Web.Localization;

namespace ShiftFlow.Web.Tests;

/// <summary>
/// Guards the Arabic dictionary in ShiftFlow.Web/Localization/Translations.cs:
/// no key may be defined twice, every key a literal Loc.T("...") call asks for must exist,
/// and no value may be blank or left as the untranslated English key.
/// </summary>
public class TranslationsTests
{
    /// <summary>Keys that are deliberately identical in both languages (brand/format names).</summary>
    private static readonly HashSet<string> IdenticalByDesign = new(StringComparer.Ordinal)
    {
        "PDF",
    };

    /// <summary>Keys intentionally left untranslated (no dictionary entry, English is correct).</summary>
    private static readonly HashSet<string> EnglishOnlyKeys = new(StringComparer.Ordinal)
    {
        "…", // loading placeholder rendered identically in both languages
    };

    private static readonly Regex EntryRegex = new(
        """Ar\["((?:[^"\\]|\\.)*)"\]\s*=\s*"((?:[^"\\]|\\.)*)"\s*;""",
        RegexOptions.Compiled);

    private static readonly Regex LocCallRegex = new(
        """(?<![\w.])_?[Ll]oc\.T\(\s*"((?:[^"\\\n]|\\.)*)"\s*[,)]""",
        RegexOptions.Compiled);

    private static string WebRoot { get; } = FindWebRoot();

    private static string FindWebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "ShiftFlow.Web");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "Localization", "Translations.cs")))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ShiftFlow.Web folder from " + AppContext.BaseDirectory);
    }

    private static string TranslationsSourcePath => Path.Combine(WebRoot, "Localization", "Translations.cs");

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(WebRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                        && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));

    private static string Unescape(string literal) =>
        Regex.Replace(literal, @"\\(.)", m => m.Groups[1].Value switch
        {
            "n" => "\n",
            "t" => "\t",
            "r" => "\r",
            var c => c,
        });

    [Fact]
    public void SourceFileHasNoDuplicateKeys()
    {
        var text = File.ReadAllText(TranslationsSourcePath);
        var duplicates = EntryRegex.Matches(text)
            .Select(m => Unescape(m.Groups[1].Value))
            .GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} (x{g.Count()})")
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Translations.cs defines these keys more than once:\n  " + string.Join("\n  ", duplicates));
    }

    [Fact]
    public void EveryParsedEntryIsInTheDictionary()
    {
        var text = File.ReadAllText(TranslationsSourcePath);
        var parsed = EntryRegex.Matches(text).Select(m => Unescape(m.Groups[1].Value)).ToList();

        Assert.NotEmpty(parsed);
        Assert.Equal(parsed.Count, Translations.Ar.Count);
        Assert.DoesNotContain(parsed, k => !Translations.Ar.ContainsKey(k));
    }

    [Fact]
    public void EveryKeyUsedByLocCallsExists()
    {
        var missing = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in SourceFiles())
        {
            if (string.Equals(file, TranslationsSourcePath, StringComparison.OrdinalIgnoreCase)) continue;

            foreach (Match m in LocCallRegex.Matches(File.ReadAllText(file)))
            {
                var key = Unescape(m.Groups[1].Value);
                if (key.Length == 0) continue;
                seen.Add(key);
                if (EnglishOnlyKeys.Contains(key) || Translations.Ar.ContainsKey(key)) continue;
                missing[key] = Path.GetRelativePath(WebRoot, file);
            }
        }

        // Guards the scan itself: if the regex ever stops matching, the assertion below
        // would pass vacuously instead of catching missing translations.
        Assert.True(seen.Count > 300, $"Only {seen.Count} literal Loc.T(\"...\") keys were found - the scan looks broken.");

        Assert.True(missing.Count == 0,
            "These keys are used by Loc.T(\"...\") but have no Arabic translation:\n  "
            + string.Join("\n  ", missing.Select(kv => $"\"{kv.Key}\"  ({kv.Value})")));
    }

    [Fact]
    public void NoValueIsBlankOrLeftInEnglish()
    {
        var bad = Translations.Ar
            .Where(kv => string.IsNullOrWhiteSpace(kv.Value)
                         || (string.Equals(kv.Key, kv.Value, StringComparison.Ordinal)
                             && !IdenticalByDesign.Contains(kv.Key)))
            .Select(kv => $"\"{kv.Key}\" => \"{kv.Value}\"")
            .ToList();

        Assert.True(bad.Count == 0,
            "These entries are empty or identical to their English key:\n  " + string.Join("\n  ", bad));
    }
}
