$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ToolsDir = Join-Path $ProjectRoot ".tools"
$DownloadsDir = Join-Path $ToolsDir "downloads"
$DistDir = Join-Path $ProjectRoot "dist"
$PackageVersion = "1.0.4022.49"
$PackageBase = "Microsoft.Web.WebView2.$PackageVersion"

New-Item -ItemType Directory -Force -Path $ToolsDir, $DownloadsDir, $DistDir | Out-Null

function Download-File {
    param(
        [Parameter(Mandatory=$true)][string]$Url,
        [Parameter(Mandatory=$true)][string]$OutFile
    )

    if (Test-Path -LiteralPath $OutFile) {
        return
    }

    Write-Host "Downloading $Url"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        & $curl.Source -L --fail --retry 3 --retry-delay 2 -o $OutFile $Url
    } else {
        Invoke-WebRequest -Uri $Url -OutFile $OutFile
    }
}

function Ensure-WebView2Package {
    $extractDir = Join-Path $ToolsDir $PackageBase
    $coreDll = Join-Path $extractDir "lib\net462\Microsoft.Web.WebView2.Core.dll"
    if (Test-Path -LiteralPath $coreDll) {
        return $extractDir
    }

    $nupkg = Join-Path $DownloadsDir "$PackageBase.nupkg"
    $zip = Join-Path $DownloadsDir "$PackageBase.zip"
    Download-File `
        -Url "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$PackageVersion/microsoft.web.webview2.$PackageVersion.nupkg" `
        -OutFile $nupkg

    Copy-Item -LiteralPath $nupkg -Destination $zip -Force

    $tmp = "$extractDir.tmp"
    if (Test-Path -LiteralPath $tmp) {
        Remove-Item -LiteralPath $tmp -Recurse -Force
    }
    if (Test-Path -LiteralPath $extractDir) {
        Remove-Item -LiteralPath $extractDir -Recurse -Force
    }

    Expand-Archive -LiteralPath $zip -DestinationPath $tmp
    Move-Item -LiteralPath $tmp -Destination $extractDir
    return $extractDir
}

function Get-CscPath {
    $candidate = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path -LiteralPath $candidate)) {
        throw "Could not find csc.exe at $candidate"
    }
    return $candidate
}

$packageRoot = Ensure-WebView2Package
$csc = Get-CscPath
$source = Join-Path $ProjectRoot "CyclingTodayCleanPlayer.cs"
$outputExe = Join-Path $DistDir "CyclingTodayCleanPlayer.exe"
$webViewCore = Join-Path $packageRoot "lib\net462\Microsoft.Web.WebView2.Core.dll"
$webViewForms = Join-Path $packageRoot "lib\net462\Microsoft.Web.WebView2.WinForms.dll"
$loader = Join-Path $packageRoot "runtimes\win-x64\native\WebView2Loader.dll"
$cleaner = Join-Path $ProjectRoot "cleaner.js"
$icon = Join-Path $ProjectRoot "app.ico"

& $csc /nologo /target:winexe /platform:x64 /optimize+ `
    /out:$outputExe `
    /win32icon:$icon `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:$webViewCore `
    /reference:$webViewForms `
    $source

Copy-Item -LiteralPath $webViewCore -Destination (Join-Path $DistDir "Microsoft.Web.WebView2.Core.dll") -Force
Copy-Item -LiteralPath $webViewForms -Destination (Join-Path $DistDir "Microsoft.Web.WebView2.WinForms.dll") -Force
Copy-Item -LiteralPath $loader -Destination (Join-Path $DistDir "WebView2Loader.dll") -Force
Copy-Item -LiteralPath $cleaner -Destination (Join-Path $DistDir "cleaner.js") -Force
Copy-Item -LiteralPath $icon -Destination (Join-Path $DistDir "app.ico") -Force

Write-Host "Built: $outputExe"
