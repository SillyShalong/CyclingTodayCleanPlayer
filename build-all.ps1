param(
    [switch]$SkipWindows,
    [switch]$SkipAndroid,
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$WindowsDir = Join-Path $ProjectRoot "windows"
$AndroidDir = Join-Path $ProjectRoot "android"

function Invoke-Step {
    param(
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)][scriptblock]$Action
    )

    Write-Host ""
    Write-Host "== $Name =="
    & $Action
}

function Assert-Under-Project {
    param([Parameter(Mandatory=$true)][string]$Path)

    $project = [System.IO.Path]::GetFullPath($ProjectRoot)
    $target = [System.IO.Path]::GetFullPath($Path)
    if (-not $target.StartsWith($project + [System.IO.Path]::DirectorySeparatorChar)) {
        throw "Refusing to operate outside project: $target"
    }
}

function Stop-WindowsPlayer {
    $processes = Get-Process -Name "CyclingTodayCleanPlayer" -ErrorAction SilentlyContinue
    if (-not $processes) {
        return
    }

    Write-Host "Closing running CyclingTodayCleanPlayer instances so package files are not locked."
    foreach ($process in $processes) {
        try { [void]$process.CloseMainWindow() } catch {}
    }

    Start-Sleep -Seconds 2
    $processes = Get-Process -Name "CyclingTodayCleanPlayer" -ErrorAction SilentlyContinue
    if ($processes) {
        $processes | Stop-Process -Force
    }
}

function New-WindowsZip {
    $dist = Join-Path $WindowsDir "dist"
    $zip = Join-Path $WindowsDir "CyclingTodayCleanPlayer-windows.zip"
    $stage = Join-Path $ProjectRoot ".package-staging\windows"

    $exe = Join-Path $dist "CyclingTodayCleanPlayer.exe"
    if (-not (Test-Path -LiteralPath $exe)) {
        throw "Windows build output was not found: $exe"
    }

    Assert-Under-Project -Path $stage
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Get-ChildItem -LiteralPath $dist -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $stage -Force
    }

    if (Test-Path -LiteralPath $zip) {
        Remove-Item -LiteralPath $zip -Force
    }

    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
    Remove-Item -LiteralPath $stage -Recurse -Force
    Write-Host "Windows zip: $zip"
}

if (-not $SkipWindows) {
    Invoke-Step -Name "Build Windows player and download WebView2 SDK" -Action {
        & (Join-Path $WindowsDir "build-windows.ps1")
    }

    if (-not $SkipZip) {
        Invoke-Step -Name "Package Windows zip" -Action {
            Stop-WindowsPlayer
            New-WindowsZip
        }
    }
}

if (-not $SkipAndroid) {
    Invoke-Step -Name "Build Android APK and download Android dependencies" -Action {
        & (Join-Path $AndroidDir "build-apk.ps1")
    }
}

Write-Host ""
Write-Host "Build complete."
if (-not $SkipWindows) {
    Write-Host "Windows exe: $(Join-Path $WindowsDir 'dist\CyclingTodayCleanPlayer.exe')"
    if (-not $SkipZip) {
        Write-Host "Windows zip: $(Join-Path $WindowsDir 'CyclingTodayCleanPlayer-windows.zip')"
    }
}
if (-not $SkipAndroid) {
    Write-Host "Android APK: $(Join-Path $AndroidDir 'dist\CyclingTodayAndroidPlayer-debug.apk')"
}