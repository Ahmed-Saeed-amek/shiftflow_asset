# ShiftFlow.Mobile

Thin .NET MAUI shell that loads the existing `ShiftFlow.Web` MVC site in a
full-screen `WebView`. No changes to `ShiftFlow.Web` are required — this is
a native wrapper, not a rewrite.

## Running against a local dev server

1. Start the web app: `dotnet run --project ../ShiftFlow.Web` (listens on
   `http://localhost:55249` / `https://localhost:55248` per
   `launchSettings.json`).
2. Pick a target and run `ShiftFlow.Mobile`:
   - **Android emulator**: `dotnet build -t:Run -f net10.0-android`, or F5 in
     Visual Studio with an Android target selected. The app points at
     `http://10.0.2.2:55249` — the emulator's special alias for the host
     machine's loopback (`10.0.2.2` is not the same as `localhost` from
     inside the emulator).
   - **iOS Simulator**: `http://localhost:55249` works directly since the
     simulator shares the host's network stack — but this only runs on a
     Mac (see below).
   - **Windows**: `net10.0-windows10.0.19041.0` target, `http://localhost:55249`.

Plain HTTP is used for local dev only, to avoid the self-signed dev-cert
trust problem HTTPS would introduce on-device. The Android cleartext
exception (`Platforms/Android/Resources/xml/network_security_config.xml`)
only whitelists `10.0.2.2`/`localhost` — addresses `ShiftFlowServerConfig`
never points at outside a `DEBUG` build, since release always uses the
fixed HTTPS `ProductionUrl`. (Android's `<debug-overrides>` element only
supports `<trust-anchors>`, not a cleartext `domain-config` — nesting one
there is a hard XML parse error, not a silent no-op, so it can't be used
to scope this by build config.)

Before a release build, set the real deployed HTTPS URL in
`ShiftFlowServerConfig.ProductionUrl`.

## iOS — requires a Mac

Apple's iOS Simulator only runs under Xcode on macOS. There is no
Windows-native iOS emulator, in MAUI or any other framework. The `ios`
workload is already installed here so the code is ready to build the
moment a Mac build host is available. Options, in order of convenience:

1. **Skip iOS locally** — build/verify Android only until a Mac is
   available.
2. **Visual Studio "Pair to Mac"** — build and launch the iOS Simulator
   remotely against a Mac on the same network or a cloud Mac
   (MacinCloud, MacStadium).
3. **Cloud device farm** (BrowserStack App Live, Sauce Labs) — sideload the
   built `.ipa`/`.app` for occasional manual verification without owning a
   Mac.

## Android SDK / emulator setup

The Android SDK lives at `C:\Program Files (x86)\Android\android-sdk`
(installed via Visual Studio). If the `emulator` package or a system image
isn't present yet:

```powershell
$sdk = "C:\Program Files (x86)\Android\android-sdk"
$env:JAVA_HOME = "C:\Program Files\Android\openjdk\jdk-21.0.8"
& "$sdk\cmdline-tools\latest\bin\sdkmanager.bat" `
    "emulator" "system-images;android-34;google_apis;x86_64" "platforms;android-34" `
    --sdk_root="$sdk"

& "$sdk\cmdline-tools\latest\bin\avdmanager.bat" create avd `
    -n ShiftFlow_Test -k "system-images;android-34;google_apis;x86_64" `
    -d "pixel_6" --sdk_root="$sdk"

& "$sdk\emulator\emulator.exe" -avd ShiftFlow_Test
```

Note: stock AVD device profiles (`avdmanager list device`) don't include a
real Samsung skin — those ship with Samsung's own tooling, not the
open Android SDK. A Pixel-class profile behaves equivalently for testing
ShiftFlow's responsive layout; swap the skin later in Android Studio's
Device Manager if a Samsung-specific screen size/DPI matters.
