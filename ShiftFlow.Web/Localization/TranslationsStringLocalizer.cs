using Microsoft.Extensions.Localization;

namespace ShiftFlow.Web.Localization;

/// <summary>
/// Bridges .NET DataAnnotations validation localization to the app's cookie-based Translations table.
/// Message templates AND string arguments (field names) are translated via Translations.Ar.
/// </summary>
public sealed class TranslationsStringLocalizer : IStringLocalizer
{
    private readonly IHttpContextAccessor _http;
    public TranslationsStringLocalizer(IHttpContextAccessor http) => _http = http;

    private string Lang =>
        _http.HttpContext?.Request.Cookies[LanguageService.CookieName] == "ar" ? "ar" : "en";

    public LocalizedString this[string name]
    {
        get { var v = Translations.T(Lang, name); return new LocalizedString(name, v, v == name); }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var lang = Lang;
            var fmt = Translations.T(lang, name);
            var args = arguments.Select(a => a is string s ? Translations.T(lang, s) : a).ToArray();
            var v = string.Format(fmt, args);
            return new LocalizedString(name, v, fmt == name);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        Enumerable.Empty<LocalizedString>();
}

public sealed class TranslationsStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly IHttpContextAccessor _http;
    public TranslationsStringLocalizerFactory(IHttpContextAccessor http) => _http = http;
    public IStringLocalizer Create(Type resourceSource) => new TranslationsStringLocalizer(_http);
    public IStringLocalizer Create(string baseName, string location) => new TranslationsStringLocalizer(_http);
}
