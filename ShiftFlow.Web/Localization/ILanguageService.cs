using System.Text.RegularExpressions;

namespace ShiftFlow.Web.Localization;

public interface ILanguageService
{
    string Lang { get; }
    bool IsRTL { get; }
    string T(string key);
    string TDate(string formattedDate);
}

public class LanguageService : ILanguageService
{
    public const string CookieName = "shiftflow_lang";

    // Longest tokens first so "December" matches before "Dec" at the same position.
    private static readonly Regex DateTokenRegex = new(
        @"\b(Sunday|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|January|February|March|April|May|June|July|August|September|October|November|December|Sun|Mon|Tue|Wed|Thu|Fri|Sat|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\b",
        RegexOptions.Compiled);

    public LanguageService(IHttpContextAccessor accessor)
    {
        var cookieLang = accessor.HttpContext?.Request.Cookies[CookieName];
        Lang = cookieLang == "ar" ? "ar" : "en";
    }

    public string Lang { get; }
    public bool IsRTL => Lang == "ar";
    public string T(string key) => Translations.T(Lang, key);
    public string TDate(string formattedDate) =>
        Lang == "ar" ? DateTokenRegex.Replace(formattedDate, m => T(m.Value)) : formattedDate;
}
