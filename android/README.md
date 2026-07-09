# CyclingTodayAndroidPlayer

Android WebView version of the Cycling Today clean player.

## Behavior

- Opens `https://cycling.today/` through Android WebView.
- Keeps the public embedded player iframe in its original page context.
- Visually crops/scales the page so the app opens directly on the video area.
- Blocks popup windows and common ad/tracking hosts.
- Removes non-player iframes such as chat so chat notification sounds do not play.
- Sends one automatic tap near the player unmute control after loading.

This app does not extract HLS/DASH streams, bypass DRM, or bypass access controls.

## Build

From this directory:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-apk.ps1
```

The debug APK will be:

```text
dist\\CyclingTodayAndroidPlayer-debug.apk
```

Install with:

```powershell
adb install -r dist\\CyclingTodayAndroidPlayer-debug.apk
```

