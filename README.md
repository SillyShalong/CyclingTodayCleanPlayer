# CyclingTodayPlayer

Unified source tree for the Cycling Today clean player.

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
- Sends one automatic unmute click or tap after loading.

These apps do not extract HLS or DASH streams, bypass DRM, or bypass access controls.

## Build

Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\windows\build-windows.ps1
```

Android:

```powershell
powershell -ExecutionPolicy Bypass -File .\android\build-apk.ps1
```
