# ShiftFlow Mobile Deployment

`ShiftFlow.Mobile` is a thin .NET MAUI shell that loads the existing
`ShiftFlow.Web` MVC site in a full-screen `WebView`. It is a native wrapper,
not a rewrite — `ShiftFlow.Web` is unmodified. This doc covers everything
needed to set up a fresh machine and run it, in the order it needs to
happen, including the exact problems hit while building this the first time
and how they were solved (so a fresh machine doesn't have to rediscover
them).

## 1. Dependencies

| Requirement | Notes |
|---|---|
| Windows 10/11 | Android emulator + MAUI Android target. iOS needs a Mac — see [§6](#6-ios--requires-a-mac). |
| .NET SDK 10 | `dotnet --version` should report `10.0.x`. `ShiftFlow.Mobile` targets `net10.0-android` (and `-ios`/`-maccatalyst`/`-windows` where applicable); `ShiftFlow.Web` stays on `net8.0` — both build fine side by side from the same SDK. |
| .NET MAUI workloads | `android`, `ios`, `maccatalyst`, `maui-windows`. Install with `dotnet workload install maui` if `dotnet workload list` doesn't already show them (they're bundled with the Visual Studio ".NET Multi-platform App UI development" component, so a VS install may already have them). |
| JDK 21 | Needed by the Android SDK command-line tools (`sdkmanager`/`avdmanager`). A VS Android install typically already includes one, e.g. `C:\Program Files\Android\openjdk\jdk-21.0.8`. |
| Android SDK | platform-tools, `platforms;android-34`, `emulator`, `system-images;android-34;google_apis;x86_64`. See [§2](#2-android-sdk--emulator-setup) — a partial SDK (just platform-tools/platforms, no emulator) is a common starting state from a plain VS install. |

## 2. Android SDK & emulator setup

### 2.1 Locate or install the SDK

Check for an existing SDK first (a Visual Studio Android install typically
puts one at `C:\Program Files (x86)\Android\android-sdk`):

```powershell
Test-Path "C:\Program Files (x86)\Android\android-sdk\cmdline-tools\latest\bin\sdkmanager.bat"
```

**Important gotcha:** `C:\Program Files (x86)\...` is admin-write-protected.
A non-admin user can *read* the existing SDK there (enough to build) but
**cannot install new packages into it** — `sdkmanager` will silently loop or
fail with `Failed to read or create install properties file`. Two options:

- Run the install steps below **elevated** (as admin), targeting that
  existing SDK path, **or**
- Install a **second, user-writable SDK** at `%LOCALAPPDATA%\Android\Sdk`
  (recommended if you can't/don't want to elevate — this is what was used
  to build this the first time). The commands below assume this path;
  substitute the Program Files path + run elevated if you'd rather use one
  SDK.

If there's no SDK at all yet, `%LOCALAPPDATA%\Android\Sdk` is also just the
normal non-elevated Android Studio default — same steps apply, you just
need `cmdline-tools` first (grab the "Command line tools only" zip from
https://developer.android.com/studio and unzip so the layout is
`<sdk>\cmdline-tools\latest\bin\sdkmanager.bat`).

### 2.2 Accept licenses (must happen before installing anything)

`sdkmanager`'s interactive license prompts don't reliably receive input
through a plain PowerShell pipe (`"y" | sdkmanager ...` frequently drops
input mid-flow and leaves packages "license not accepted"). Redirect from
a real file instead — this is the reliable method:

```powershell
$sdk = "$env:LOCALAPPDATA\Android\Sdk"
$env:JAVA_HOME = "C:\Program Files\Android\openjdk\jdk-21.0.8"
$yesFile = "$env:TEMP\yes_answers.txt"
[System.IO.File]::WriteAllLines($yesFile, (1..40 | ForEach-Object { "y" }))

cmd /c "`"C:\Program Files (x86)\Android\android-sdk\cmdline-tools\latest\bin\sdkmanager.bat`" --licenses --sdk_root=`"$sdk`" < `"$yesFile`""
```

(The `sdkmanager.bat` binary itself can be read from wherever an existing
SDK has it — `--sdk_root` controls where packages actually get written.)

### 2.3 Install packages

```powershell
cmd /c "`"C:\Program Files (x86)\Android\android-sdk\cmdline-tools\latest\bin\sdkmanager.bat`" `"platform-tools`" `"platforms;android-34`" `"emulator`" `"system-images;android-34;google_apis;x86_64`" `"cmdline-tools;latest`" --sdk_root=`"$sdk`" < `"$yesFile`""
```

This downloads a few GB — expect several minutes. Install
`cmdline-tools;latest` into the **new** SDK root too (last item above): the
copy of `avdmanager.bat`/`sdkmanager.bat` under a given SDK resolves *its
own* root from its install location, not from `--sdk_root`/env vars alone,
so AVD creation below needs to run from a copy that actually lives inside
the target SDK.

**If `avdmanager create avd` fails with `Could not load devices.xml`:**
some system images ship without this optional file. Create an empty stub:

```powershell
@'
<?xml version="1.0" encoding="UTF-8"?>
<d:devices xmlns:d="http://schemas.android.com/sdk/devices/7"/>
'@ | Out-File "$sdk\system-images\android-34\google_apis\x86_64\devices.xml" -Encoding utf8
```

### 2.4 Create and boot the AVD

```powershell
$env:JAVA_HOME = "C:\Program Files\Android\openjdk\jdk-21.0.8"
echo no | & "$sdk\cmdline-tools\latest\bin\avdmanager.bat" create avd `
    -n ShiftFlow_Test -k "system-images;android-34;google_apis;x86_64" -d pixel_6 --force

& "$sdk\emulator\emulator.exe" -avd ShiftFlow_Test -no-snapshot -no-boot-anim
```

`pixel_6` is a stock AVD device profile (`avdmanager list device`) — the
open Android SDK doesn't ship real Samsung device skins (those are
Samsung's own tooling). A Pixel-class profile is representative for
testing ShiftFlow's responsive layout; swap the skin later in Android
Studio's Device Manager if a Samsung-specific screen size/DPI matters.

Wait for full boot before deploying:

```powershell
& "$sdk\platform-tools\adb.exe" shell getprop sys.boot_completed   # returns 1 when ready
```

## 3. Run ShiftFlow.Web

```powershell
dotnet run --project ShiftFlow.Web
```

Listens on `http://localhost:55249` / `https://localhost:55248` per
`ShiftFlow.Web/Properties/launchSettings.json`. The mobile app needs this
running before it'll show anything but a connection error.

## 4. Run ShiftFlow.Mobile on the emulator

```powershell
cd ShiftFlow.Mobile
dotnet build -t:Run -f net10.0-android -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk"
```

`-p:AndroidSdkDirectory=...` is only needed if you have two SDKs (the
read-only Program Files one plus the writable one) — it tells the build
which one to deploy/run through. Omit it if you only have one SDK.

This builds, installs, and launches the app on whichever device `adb sees`
(the booted AVD). First build is slow (minutes); incremental rebuilds
after a code-only change are much faster.

### How the app finds the server

The emulator is a separate virtual machine from your host — it can't reach
`localhost`. `ShiftFlowServerConfig.cs` points a DEBUG Android build at
`http://10.0.2.2:55249`, which is the Android emulator's special alias for
the host machine's own loopback. (iOS Simulator, by contrast, shares the
host's network stack directly, so it'd use `http://localhost:55249` — see
`ShiftFlow.Mobile/README.md`.)

Plain HTTP avoids the self-signed ASP.NET Core dev-cert trust problem
HTTPS would introduce on-device. Before a release build, set the real
deployed HTTPS URL in `ShiftFlowServerConfig.ProductionUrl`.

## 5. Problems hit building this the first time (and their fixes)

These are already fixed in the current code — kept here so a future
`git pull` that reintroduces something similar isn't a mystery:

1. **App crashes instantly on launch**, logcat shows
   `Nested domain-config not allowed in debug-overrides`
   — Android's `<debug-overrides>` element only supports `<trust-anchors>`,
   not a `<domain-config>` with `cleartextTrafficPermitted`. It's a hard
   XML parse error, not a silent no-op. Fixed by moving the cleartext
   exception in `Platforms/Android/Resources/xml/network_security_config.xml`
   to the top level instead of nesting it in `debug-overrides` — safe here
   because it only whitelists `10.0.2.2`/`localhost`, addresses the app
   never points at outside `DEBUG` builds anyway.

2. **App launches but the WebView is blank**, logcat shows
   `SSL error code 1, net_error -202`
   — `ShiftFlow.Web` has `UseHttpsRedirection()`, so even a plain-HTTP
   request 307-redirects to the HTTPS port, and the emulator doesn't trust
   the self-signed dev cert. Fixed with a debug-only
   `Platforms/Android/DevSslWebViewClient.cs` that proceeds past the SSL
   error, but only for the known local-dev hosts (`10.0.2.2`/`localhost`)
   — wired in via `WebViewHandler.Mapper` in `MauiProgram.cs`, `#if DEBUG`
   gated so it never ships in release.

3. **App crashes on launch**,
   `IllegalArgumentException: ... requires ... TextAppearance ... Theme.MaterialComponents`
   in `NavigationBarView`/`ShellItemRenderer`
   — a MAUI Shell + Android Material Components theming bug. This app has
   no flyout/tabs/routing to justify Shell's overhead anyway, so the fix
   was to drop Shell entirely: `App.xaml.cs` now does
   `new Window(new NavigationPage(new MainPage()))` instead of
   `new Window(new AppShell())`, and `AppShell.xaml`/`.cs` were deleted.

4. **App crashes on launch**,
   `IllegalArgumentException: No view found for id ... jumpToStart ... NavigationRootManager`
   — a bare `ContentPage` as the window root isn't well-supported on
   Android (its internal `NavigationRootManager` expects view IDs that
   `NavigationPage`/`Shell` normally provide). Wrapping in `NavigationPage`
   (see #3) is also the fix for this.

5. **Same "jumpToStart" crash persisted across multiple rebuilds** even
   after applying fix #4 — turned out to be **stale incremental Android
   resource IDs**: adding a brand-new `Resources/xml/` folder mid-stream
   (for fix #1) can confuse `aapt2`'s incremental resource-ID cache in
   MAUI/Xamarin Android builds. Fixed with a full clean rebuild:
   ```powershell
   cd ShiftFlow.Mobile
   Remove-Item bin, obj -Recurse -Force
   dotnet build -t:Run -f net10.0-android -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk"
   ```
   If a fresh-looking crash doesn't match anything in this list after a
   platform/resource change, try a clean rebuild before debugging further.

6. **`Start-Job`-backgrounded install/build commands silently produced no
   output and never finished** — PowerShell `Start-Job` runspaces are
   killed when the invoking PowerShell process exits, so anything run that
   way inside a short-lived shell session dies with it. Use
   `Start-Process` (a real detached OS process) for anything that needs to
   outlive the current shell invocation, e.g.:
   ```powershell
   Start-Process -FilePath "cmd.exe" -ArgumentList $cmdArgs -WindowStyle Hidden -PassThru
   ```

7. **`avdmanager list device --sdk_root=...`** — `--sdk_root` isn't a
   valid flag for `avdmanager` (unlike `sdkmanager`). It resolves its SDK
   root from environment variables (`ANDROID_SDK_ROOT`/`ANDROID_HOME`) or
   its own install location instead — see §2.3's note about running
   `avdmanager` from a copy installed inside the target SDK.

8. **Reading `adb logcat` output via PowerShell redirection (`>`)** — the
   default `>` redirection encoding is UTF-16LE, which breaks naive
   `grep`/text search (every ASCII byte gets a null byte after it, so
   nothing matches). Use `| Out-File -Encoding utf8` instead when piping
   logcat (or any command output) to a file you intend to search.

## 6. iOS — requires a Mac

Apple's iOS Simulator only runs under Xcode on macOS. There is no
Windows-native iOS emulator, in MAUI or any other framework — this is an
Apple platform restriction, not something fixable in tooling. The `ios`
workload is already referenced in `ShiftFlow.Mobile.csproj` (target
frameworks include `net10.0-ios` on non-Linux hosts) so the code is ready
to build the moment a Mac build host is available. Options, in order of
convenience:

1. **Skip iOS locally** — build/verify Android only until a Mac is
   available.
2. **Visual Studio "Pair to Mac"** — build and launch the iOS Simulator
   remotely against a Mac on the same network or a cloud Mac
   (MacinCloud, MacStadium).
3. **Cloud device farm** (BrowserStack App Live, Sauce Labs) — sideload the
   built `.ipa`/`.app` for occasional manual verification without owning a
   Mac.

## 7. Quick reference — full sequence on a fresh machine

```powershell
# 1. Clone
git clone https://github.com/Ahmed-Saeed-amek/ShiftFlow--Shifts.git
cd ShiftFlow--Shifts

# 2. Confirm/install MAUI workloads
dotnet workload list
# dotnet workload install maui   # if android/ios/maccatalyst/maui-windows are missing

# 3. Android SDK — see §2 for the full accept-licenses / install-packages / create-AVD steps
#    (skip if a working SDK + emulator/system-image are already present)

# 4. Boot the emulator
& "$env:LOCALAPPDATA\Android\Sdk\emulator\emulator.exe" -avd ShiftFlow_Test

# 5. In one terminal: run the web app
dotnet run --project ShiftFlow.Web

# 6. In another terminal: run the mobile shell against it
cd ShiftFlow.Mobile
dotnet build -t:Run -f net10.0-android -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk"
```

See also `ShiftFlow.Mobile/README.md` for the same run instructions scoped
to just the mobile project.
