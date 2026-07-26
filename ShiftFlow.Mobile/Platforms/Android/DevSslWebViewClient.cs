#if DEBUG && ANDROID
using Android.Net.Http;
using Android.Webkit;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace ShiftFlow.Mobile.Platforms.Android;

/// <summary>
/// Debug-only: the local ShiftFlow.Web dev server serves a self-signed HTTPS
/// dev cert. The emulator doesn't trust it, so ASP.NET Core's HTTP->HTTPS
/// redirect otherwise fails the WebView's SSL handshake and leaves a blank
/// page. This proceeds past that specific, known dev host only.
/// </summary>
internal sealed class DevSslWebViewClient(WebViewHandler handler) : MauiWebViewClient(handler)
{
    public override void OnReceivedSslError(global::Android.Webkit.WebView? view, SslErrorHandler? handler, SslError? error)
    {
        var host = error?.Url is { } url ? global::Android.Net.Uri.Parse(url)?.Host : null;
        if (host is "10.0.2.2" or "localhost")
        {
            handler?.Proceed();
            return;
        }

        base.OnReceivedSslError(view, handler, error);
    }
}
#endif
