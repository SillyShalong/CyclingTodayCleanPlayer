# CyclingTodayCleanPlayer

Windows WebView2 version of the Cycling Today clean player.

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
```

## Runtime switches

- `--no-clean`
- `--no-block`
- `--no-auto-click`
