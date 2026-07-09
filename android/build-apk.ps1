$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ToolsDir = Join-Path $ProjectRoot ".tools"
$DownloadsDir = Join-Path $ToolsDir "downloads"
$DistDir = Join-Path $ProjectRoot "dist"
$AndroidSdkRoot = Join-Path $ToolsDir "android-sdk"
$GradleUserHome = Join-Path $ToolsDir "gradle-user-home"
$GradleVersion = "8.10.2"

New-Item -ItemType Directory -Force -Path $ToolsDir, $DownloadsDir, $DistDir, $GradleUserHome | Out-Null

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

function Expand-Zip-Once {
    param(
        [Parameter(Mandatory=$true)][string]$Zip,
        [Parameter(Mandatory=$true)][string]$Destination
    )

    if (Test-Path -LiteralPath $Destination) {
        return
    }

    $tmp = "$Destination.tmp"
    if (Test-Path -LiteralPath $tmp) {
        Remove-Item -LiteralPath $tmp -Recurse -Force
    }

    Expand-Archive -LiteralPath $Zip -DestinationPath $tmp
    Move-Item -LiteralPath $tmp -Destination $Destination
}

function Ensure-Jdk {
    $jdkRoot = Join-Path $ToolsDir "jdk17"
    $marker = Join-Path $jdkRoot "bin\javac.exe"
    if (Test-Path -LiteralPath $marker) {
        return $jdkRoot
    }

    $zip = Join-Path $DownloadsDir "jdk17.zip"
    Download-File `
        -Url "https://api.adoptium.net/v3/binary/latest/17/ga/windows/x64/jdk/hotspot/normal/eclipse?project=jdk" `
        -OutFile $zip

    $extract = Join-Path $ToolsDir "jdk17-extract"
    if (Test-Path -LiteralPath $extract) {
        Remove-Item -LiteralPath $extract -Recurse -Force
    }
    Expand-Archive -LiteralPath $zip -DestinationPath $extract
    $inner = Get-ChildItem -LiteralPath $extract -Directory | Select-Object -First 1
    if (-not $inner) {
        throw "JDK archive did not contain a directory."
    }
    Move-Item -LiteralPath $inner.FullName -Destination $jdkRoot
    Remove-Item -LiteralPath $extract -Recurse -Force
    return $jdkRoot
}

function Ensure-Gradle {
    $gradleRoot = Join-Path $ToolsDir "gradle-$GradleVersion"
    $marker = Join-Path $gradleRoot "bin\gradle.bat"
    if (Test-Path -LiteralPath $marker) {
        return $gradleRoot
    }

    $zip = Join-Path $DownloadsDir "gradle-$GradleVersion-bin.zip"
    Download-File `
        -Url "https://services.gradle.org/distributions/gradle-$GradleVersion-bin.zip" `
        -OutFile $zip

    Expand-Zip-Once -Zip $zip -Destination $gradleRoot
    $nested = Join-Path $gradleRoot "gradle-$GradleVersion"
    if (Test-Path -LiteralPath $nested) {
        Get-ChildItem -LiteralPath $nested -Force | Move-Item -Destination $gradleRoot
        Remove-Item -LiteralPath $nested -Recurse -Force
    }
    return $gradleRoot
}

function Ensure-Android-Sdk {
    $cmdlineRoot = Join-Path $AndroidSdkRoot "cmdline-tools\latest"
    $sdkManager = Join-Path $cmdlineRoot "bin\sdkmanager.bat"
    if (-not (Test-Path -LiteralPath $sdkManager)) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $cmdlineRoot) | Out-Null
        $zip = Join-Path $DownloadsDir "android-commandline-tools.zip"
        $urls = @(
            "https://dl.google.com/android/repository/commandlinetools-win-13114758_latest.zip",
            "https://dl.google.com/android/repository/commandlinetools-win-12266719_latest.zip",
            "https://dl.google.com/android/repository/commandlinetools-win-11076708_latest.zip"
        )

        if (-not (Test-Path -LiteralPath $zip)) {
            $downloaded = $false
            foreach ($url in $urls) {
                try {
                    Download-File -Url $url -OutFile $zip
                    $downloaded = $true
                    break
                } catch {
                    if (Test-Path -LiteralPath $zip) {
                        Remove-Item -LiteralPath $zip -Force
                    }
                    Write-Host "Failed: $url"
                }
            }
            if (-not $downloaded) {
                throw "Could not download Android command line tools."
            }
        }

        $extract = Join-Path $ToolsDir "android-commandline-tools-extract"
        if (Test-Path -LiteralPath $extract) {
            Remove-Item -LiteralPath $extract -Recurse -Force
        }
        Expand-Archive -LiteralPath $zip -DestinationPath $extract
        $inner = Join-Path $extract "cmdline-tools"
        New-Item -ItemType Directory -Force -Path $cmdlineRoot | Out-Null
        Get-ChildItem -LiteralPath $inner -Force | Move-Item -Destination $cmdlineRoot
        Remove-Item -LiteralPath $extract -Recurse -Force
    }

    return $AndroidSdkRoot
}

$JdkHome = Ensure-Jdk
$GradleHome = Ensure-Gradle
$SdkHome = Ensure-Android-Sdk

$env:JAVA_HOME = $JdkHome
$env:ANDROID_HOME = $SdkHome
$env:ANDROID_SDK_ROOT = $SdkHome
$env:GRADLE_USER_HOME = $GradleUserHome
$env:Path = "$JdkHome\bin;$SdkHome\cmdline-tools\latest\bin;$SdkHome\platform-tools;$GradleHome\bin;$env:Path"

$sdkManager = Join-Path $SdkHome "cmdline-tools\latest\bin\sdkmanager.bat"
Write-Host "Installing Android SDK packages"
1..100 | ForEach-Object { "y" } | & $sdkManager --sdk_root=$SdkHome "platform-tools" "platforms;android-35" "build-tools;35.0.0" | Out-Host

Write-Host "Accepting Android SDK licenses"
1..100 | ForEach-Object { "y" } | & $sdkManager --sdk_root=$SdkHome --licenses | Out-Host

$gradle = Join-Path $GradleHome "bin\gradle.bat"
Write-Host "Building APK"
Push-Location $ProjectRoot
try {
    & $gradle --no-daemon "-Dorg.gradle.java.home=$JdkHome" "-Dorg.gradle.jvmargs=-Xmx2g -Dfile.encoding=UTF-8" ":app:assembleDebug"
    if ($LASTEXITCODE -ne 0) {
        throw "Gradle build failed with exit code $LASTEXITCODE"
    }
} finally {
    Pop-Location
}

$apk = Join-Path $ProjectRoot "app\build\outputs\apk\debug\app-debug.apk"
if (-not (Test-Path -LiteralPath $apk)) {
    throw "APK was not produced: $apk"
}

$outApk = Join-Path $DistDir "CyclingTodayAndroidPlayer-debug.apk"
Copy-Item -LiteralPath $apk -Destination $outApk -Force
Write-Host "APK: $outApk"

