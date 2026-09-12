namespace ShiftFlow.Web.Authorization;

/// <summary>Shared "is this a fetch()/XHR call rather than a page navigation?" test. Used both by
/// ConfigureApplicationCookie (to answer 401/403 instead of a 302 the caller can't detect) and by
/// MustChangePasswordFilter, so the two stay in agreement.</summary>
public static class RequestKinds
{
    public static bool IsApiRequest(HttpRequest request) =>
        request.Headers.XRequestedWith == "XMLHttpRequest" ||
        !request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
}
