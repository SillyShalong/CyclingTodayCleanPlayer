# CyclingTodayCleanPlayer

Windows WebView2 version of the Cycling Today clean player.

The app dynamically selects the current Cycling Today live iframe, including OK.ru `videoembed`, and never falls back to the obsolete domain-protected player URL.

## Build

From this directory:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-windows.ps1
```

Output files:

```text
dist\CyclingTodayCleanPlayer.exe
dist\Microsoft.Web.WebView2.Core.dll
dist\Microsoft.Web.WebView2.WinForms.dll
dist\WebView2Loader.dll
dist\go2tv-lite.exe
dist\go2tv-discover.exe
dist\ffmpeg.exe
```

The script downloads the official Go toolchain, builds the headless `go2tv-lite` command and the small `go2tv-discover` helper against Go2TV v2.4.0 source, and downloads the BtbN LGPL shared FFmpeg build. Downloads, source trees, and compiler caches stay under the ignored `.tools` directory.

## Full screen

After the live iframe loads, the app activates the player's own bottom-right full-screen control and verifies WebView2 entered element full screen. Press `Esc` to leave player full screen; `F11` still toggles application-window full screen manually.

## Runtime switches

- `--no-clean`
- `--no-block`
- `--no-auto-click`

## Cast to TV

The Windows player has three casting modes:

- `Miracast` always opens the native Windows wireless display panel (`Win+K`).
- `Direct Send` scans the current network with Go2TV, shows every compatible DLNA/Chromecast target, and casts only after the user selects one. It then uses FFmpeg to remux the detected live stream and pipes it into the bundled headless `go2tv-lite` server. While active, the button changes to `Stop Cast`.
- `Auto Cast` performs the same scan and selection when a stream is available, otherwise it falls back to Miracast.

If Go2TV or FFmpeg exits unexpectedly, or if the FFmpeg-to-Go2TV pipe closes while both processes still appear alive, the player cleans up both relay processes and reconnects after 2.5 seconds. Three consecutive rapid restart failures stop the loop; a stable run of at least 30 seconds resets that failure counter. Use `Stop Cast` to end the session intentionally.

There is no built-in TV address and `dlna-target.txt` is no longer used. The last user-selected URL is stored under `%LOCALAPPDATA%\CyclingTodayCleanPlayer\last-cast-target.txt` only to preselect that device when it is found again; every new session still scans before casting.
Go2TV is distributed under the MIT license. FFmpeg and the BtbN build scripts are bundled with their license texts. Some renderers, including this TCL model, may continue reporting `STOPPED` through AVTransport while they are actively fetching and playing the media; an established connection from the TV to the local Go2TV port is the reliable runtime check.