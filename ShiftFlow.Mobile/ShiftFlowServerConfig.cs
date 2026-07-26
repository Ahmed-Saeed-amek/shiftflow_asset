namespace ShiftFlow.Mobile;

/// <summary>
/// Resolves the URL the WebView shell should load. Debug builds point at a
/// locally-running ShiftFlow.Web instance; release builds point at the real
/// deployed host.
/// </summary>
internal static class ShiftFlowServerConfig
{
    // Set this to the real deployed HTTPS URL before a release build.
    private const string ProductionUrl = "https://CHANGE-ME.example.com";

    public static string ResolveUrl()
    {
#if DEBUG
        // Android emulator: 10.0.2.2 is the special alias for the host machine's
        // loopback (localhost), matching ShiftFlow.Web's http profile port in
        // launchSettings.json. Plain HTTP avoids the self-signed dev-cert trust
        // problem that HTTPS would introduce on-device.
#if ANDROID
        return "http://10.0.2.2:55249";
#elif IOS
        // iOS Simulator shares the host's network stack, so localhost works directly.
        return "http://localhost:55249";
#else
        return "http://localhost:55249";
#endif
#else
        return ProductionUrl;
#endif
    }
}
