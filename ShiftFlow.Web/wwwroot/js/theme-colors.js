// Resolves a site.css design token to a usable CSS colour. Tokens are stored as raw HSL triples
// ("201 96% 32%"), so anything without a "(" gets wrapped in hsl(). Charts and legends read their
// colours through this instead of hard-coding hex values, so they follow the theme.
(function () {
    function themeColor(token, fallback) {
        var v = getComputedStyle(document.documentElement).getPropertyValue(token).trim();
        if (!v) return fallback || 'hsl(var(--primary))';
        return v.indexOf('(') >= 0 ? v : 'hsl(' + v + ')';
    }
    window.themeColor = themeColor;
    window.themeColors = function (tokens) { return tokens.map(function (t) { return themeColor(t); }); };
})();
