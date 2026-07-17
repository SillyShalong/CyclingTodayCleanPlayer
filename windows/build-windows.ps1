$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ToolsDir = Join-Path $ProjectRoot ".tools"
$DownloadsDir = Join-Path $ToolsDir "downloads"
$DistDir = Join-Path $ProjectRoot "dist"
$PackageVersion = "1.0.4022.49"
$PackageBase = "Microsoft.Web.WebView2.$PackageVersion"
$Go2TvVersion = "2.4.0"
$GoVersion = "1.26.4"
$GoWindowsAmd64Sha256 = "3ca8fb4630b07c419cbdd51f754e31363cfcfb83b3a5354d9e895c90be2cc345"
$FfmpegAsset = "ffmpeg-master-latest-win64-lgpl-shared.zip"

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

function Ensure-GoToolchain {
    $root = Join-Path $ToolsDir "go-$GoVersion"
    $goExe = Join-Path $root "go\bin\go.exe"
    if (Test-Path -LiteralPath $goExe) {
        return $goExe
    }

    $zip = Join-Path $DownloadsDir "go${GoVersion}.windows-amd64.zip"
    Download-File `
        -Url "https://go.dev/dl/go${GoVersion}.windows-amd64.zip" `
        -OutFile $zip

    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLowerInvariant()
    if ($actualHash -ne $GoWindowsAmd64Sha256) {
        throw "Go archive checksum mismatch. expected=$GoWindowsAmd64Sha256 actual=$actualHash"
    }

    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $root
    if (-not (Test-Path -LiteralPath $goExe)) {
        throw "Go archive did not contain go.exe"
    }
    return $goExe
}

function Ensure-Go2Tv {
    $root = Join-Path $ToolsDir "go2tv-lite-$Go2TvVersion"
    $exe = Join-Path $root "go2tv-lite.exe"
    $discoverExe = Join-Path $root "go2tv-discover.exe"
    if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $discoverExe)) {
        $goExe = Ensure-GoToolchain
        $sourceZip = Join-Path $DownloadsDir "go2tv-v${Go2TvVersion}-source.zip"
        Download-File `
            -Url "https://github.com/alexballas/go2tv/archive/refs/tags/v$Go2TvVersion.zip" `
            -OutFile $sourceZip

        $sourceExtract = Join-Path $ToolsDir "go2tv-v${Go2TvVersion}-source"
        if (Test-Path -LiteralPath $sourceExtract) {
            Remove-Item -LiteralPath $sourceExtract -Recurse -Force
        }
        Expand-Archive -LiteralPath $sourceZip -DestinationPath $sourceExtract
        $goMod = Get-ChildItem -LiteralPath $sourceExtract -Recurse -File -Filter "go.mod" | Select-Object -First 1
        if (-not $goMod) {
            throw "Go2TV source archive did not contain go.mod"
        }

        New-Item -ItemType Directory -Force -Path $root | Out-Null
        $goPath = Join-Path $ToolsDir "go-path"
        $goCache = Join-Path $ToolsDir "go-build-cache"
        New-Item -ItemType Directory -Force -Path $goPath, $goCache | Out-Null
        $oldGoPath = $env:GOPATH
        $oldGoCache = $env:GOCACHE
        $oldGoToolchain = $env:GOTOOLCHAIN
        $oldCgoEnabled = $env:CGO_ENABLED
        try {
            $env:GOPATH = $goPath
            $env:GOCACHE = $goCache
            $env:GOTOOLCHAIN = "local"
            $env:CGO_ENABLED = "0"
            Push-Location $goMod.DirectoryName
            try {
                if (-not (Test-Path -LiteralPath $exe)) {
                    & $goExe build -trimpath -ldflags "-s -w -X main.version=$Go2TvVersion" -o $exe ".\cmd\go2tv-lite"
                    if ($LASTEXITCODE -ne 0) {
                        throw "Go2TV lite build failed with exit code $LASTEXITCODE"
                    }
                }

                $discoverySource = Join-Path $ProjectRoot "go2tv-discover.go"
                & $goExe build -trimpath -ldflags "-s -w" -o $discoverExe $discoverySource
                if ($LASTEXITCODE -ne 0) {
                    throw "Go2TV discovery helper build failed with exit code $LASTEXITCODE"
                }
            } finally {
                Pop-Location
            }
        } finally {
            $env:GOPATH = $oldGoPath
            $env:GOCACHE = $oldGoCache
            $env:GOTOOLCHAIN = $oldGoToolchain
            $env:CGO_ENABLED = $oldCgoEnabled
        }
    }

    $license = Join-Path $DownloadsDir "go2tv-v${Go2TvVersion}-LICENSE.txt"
    Download-File `
        -Url "https://raw.githubusercontent.com/alexballas/go2tv/v$Go2TvVersion/LICENSE" `
        -OutFile $license

    return $root
}
function Ensure-Ffmpeg {
    $root = Join-Path $ToolsDir "ffmpeg-btbn-lgpl-shared"
    $bin = Join-Path $root "bin"
    $exe = Join-Path $bin "ffmpeg.exe"
    if (-not (Test-Path -LiteralPath $exe)) {
        $zip = Join-Path $DownloadsDir $FfmpegAsset
        Download-File `
            -Url "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/$FfmpegAsset" `
            -OutFile $zip

        $tmp = "$root.tmp"
        if (Test-Path -LiteralPath $tmp) {
            Remove-Item -LiteralPath $tmp -Recurse -Force
        }
        if (Test-Path -LiteralPath $root) {
            Remove-Item -LiteralPath $root -Recurse -Force
        }

        Expand-Archive -LiteralPath $zip -DestinationPath $tmp
        $found = Get-ChildItem -LiteralPath $tmp -Recurse -File -Filter "ffmpeg.exe" | Select-Object -First 1
        if (-not $found) {
            throw "FFmpeg archive did not contain ffmpeg.exe"
        }

        New-Item -ItemType Directory -Force -Path $bin | Out-Null
        Get-ChildItem -LiteralPath $found.DirectoryName -File | Where-Object {
            $_.Name -eq "ffmpeg.exe" -or $_.Extension -eq ".dll"
        } | Copy-Item -Destination $bin -Force
        Remove-Item -LiteralPath $tmp -Recurse -Force
    }

    $ffmpegLicense = Join-Path $DownloadsDir "FFmpeg-COPYING.LGPLv2.1.txt"
    Download-File `
        -Url "https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/COPYING.LGPLv2.1" `
        -OutFile $ffmpegLicense
    $buildLicense = Join-Path $DownloadsDir "BtbN-FFmpeg-Builds-LICENSE.txt"
    Download-File `
        -Url "https://raw.githubusercontent.com/BtbN/FFmpeg-Builds/master/LICENSE" `
        -OutFile $buildLicense

    return $bin
}
function Get-CscPath {
    $candidate = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path -LiteralPath $candidate)) {
        throw "Could not find csc.exe at $candidate"
    }
    return $candidate
}

$packageRoot = Ensure-WebView2Package
$go2TvRoot = Ensure-Go2Tv
$go2TvExe = Join-Path $go2TvRoot "go2tv-lite.exe"
$go2TvDiscoverExe = Join-Path $go2TvRoot "go2tv-discover.exe"
$ffmpegBin = Ensure-Ffmpeg
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

if ($LASTEXITCODE -ne 0) {
    throw "C# build failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $webViewCore -Destination (Join-Path $DistDir "Microsoft.Web.WebView2.Core.dll") -Force
Copy-Item -LiteralPath $webViewForms -Destination (Join-Path $DistDir "Microsoft.Web.WebView2.WinForms.dll") -Force
Copy-Item -LiteralPath $loader -Destination (Join-Path $DistDir "WebView2Loader.dll") -Force
Copy-Item -LiteralPath $cleaner -Destination (Join-Path $DistDir "cleaner.js") -Force
Copy-Item -LiteralPath $icon -Destination (Join-Path $DistDir "app.ico") -Force
Copy-Item -LiteralPath $go2TvExe -Destination (Join-Path $DistDir "go2tv-lite.exe") -Force
Copy-Item -LiteralPath $go2TvDiscoverExe -Destination (Join-Path $DistDir "go2tv-discover.exe") -Force
$legacyGo2Tv = Join-Path $DistDir "go2tv.exe"
if (Test-Path -LiteralPath $legacyGo2Tv) { Remove-Item -LiteralPath $legacyGo2Tv -Force }
Get-ChildItem -LiteralPath $ffmpegBin -File | Copy-Item -Destination $DistDir -Force
Copy-Item -LiteralPath (Join-Path $DownloadsDir "FFmpeg-COPYING.LGPLv2.1.txt") -Destination (Join-Path $DistDir "FFmpeg-COPYING.LGPLv2.1.txt") -Force
Copy-Item -LiteralPath (Join-Path $DownloadsDir "BtbN-FFmpeg-Builds-LICENSE.txt") -Destination (Join-Path $DistDir "BtbN-FFmpeg-Builds-LICENSE.txt") -Force
Copy-Item -LiteralPath (Join-Path $DownloadsDir "go2tv-v${Go2TvVersion}-LICENSE.txt") -Destination (Join-Path $DistDir "go2tv-LICENSE.txt") -Force
$legacyDlnaTarget = Join-Path $DistDir "dlna-target.txt"
if (Test-Path -LiteralPath $legacyDlnaTarget) { Remove-Item -LiteralPath $legacyDlnaTarget -Force }

Write-Host "Built: $outputExe"
