# CyclingTodayPlayer

Unified source tree for the Cycling Today clean player. 

can watch tour de france

## Layout

- `windows/`
  Windows WebView2 player source and build script.
- `android/`
  Android WebView player source and APK build script.

## Behavior

Both apps keep the public embedded player in its normal page context, then crop the visible page down to the video area.

- Opens directly on the video instead of the full website.
- Blocks popup windows and common ad/tracking hosts.
- Removes non-player iframes such as chat.
- Detects the current live iframe from Cycling Today instead of using an obsolete hardcoded player URL.
- Sends one automatic unmute click or tap after loading; Android normalizes browser coordinates for different screen densities.
- On Windows, Direct Send scans the local network and requires the user to choose a discovered TV before casting.
- On Windows, Direct Send automatically reconnects a dropped DLNA relay and changes to Stop Cast while the session is active.
- On Android, Refresh restarts player detection, Full Screen activates the live player's native full-screen control, and Cast Screen opens the system wireless-display chooser.

The Windows app can detect an HLS/DASH request already made by the embedded player and relay it to the user-selected DLNA or Chromecast target using the same in-memory request context. It does not bypass DRM or access controls.

## Build

One-click build for Windows and Android:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-all.ps1
```

You can also double-click `build-all.bat`.

The one-click script downloads build dependencies into local ignored tool folders. For Windows it downloads the official Go toolchain, builds the headless `go2tv-lite` target plus the device-discovery helper from the MIT-licensed Go2TV source, and downloads an LGPL shared FFmpeg build. It then produces:

- `windows/dist/CyclingTodayCleanPlayer.exe`
- `windows/CyclingTodayCleanPlayer-windows.zip`
- `android/dist/CyclingTodayAndroidPlayer-debug.apk`

Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\windows\build-windows.ps1
```

Android:

```powershell
powershell -ExecutionPolicy Bypass -File .\android\build-apk.ps1
```

## GitHub downloads

Every push to `main` runs GitHub Actions and uploads a Windows ZIP and Android APK to the workflow run's **Artifacts** section for 30 days. Pushing a tag such as `v1.1.0` also creates a GitHub Release and attaches both files to the **Releases** page for direct download.
