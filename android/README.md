# CyclingTodayAndroidPlayer

Android WebView version of the Cycling Today clean player.

## Behavior

- Opens `https://cycling.today/` and dynamically selects the largest live-player iframe.
- Waits for and selects the current player from the Cycling Today page; it never switches to an obsolete hardcoded embed URL.
- Shows page-loading, player-detection, blocked-request, and touch-coordinate status in the app.
- Writes the complete current-run log to the app-private `files/last-run.log` file.
- Blocks popup windows and common ad/tracking hosts and removes non-player iframes.
- Converts browser coordinates to normalized WebView coordinates so unmute/resume works across Android densities and page scales.
- Tries one automatic unmute touch; each manual `Unmute / Resume` press cycles through player overlay, center, volume, and control-bar targets.
- After unmute, the app automatically activates the live player's own full-screen control. `Full Screen` retries that player control manually; Android Back exits native player full screen.
- `Cast Screen` opens Android's system wireless-display scan so the user chooses a TV; no TV address is hardcoded.
- `Refresh` reloads the current source page and restarts player detection and auto-unmute.
- Recovers the Activity if the Android System WebView renderer exits.

The app keeps the public embedded player in its page context. It does not extract streams, bypass DRM, bypass access controls, or simulate ad interactions.

## Proxy

Use `Proxy` in the control bar to select Direct, HTTP, or SOCKS5 and enter a host and port. The setting is saved in the app's private preferences and applies only to this player's WebView. Proxy authentication is not supported.

## Build

From this directory:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-apk.ps1
```

The script downloads JDK 17, Gradle, Android command-line tools, SDK platform 35, and Build Tools 35 into the ignored `.tools/` directory. The debug APK is written to:

```text
dist\CyclingTodayAndroidPlayer-debug.apk
```

Install with:

```powershell
adb install -r dist\CyclingTodayAndroidPlayer-debug.apk
```

Read the latest app log from a debug installation with:

```powershell
adb shell run-as com.lijialun.cyclingtoday cat files/last-run.log
```

`Cast Screen` depends on the phone or tablet exposing Android wireless-display settings. It performs screen mirroring; the Windows FFmpeg/Go2TV direct-DLNA relay is not bundled into the APK.
