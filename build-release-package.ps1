$ErrorActionPreference = "Stop"

$androidRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$pcRoot = Join-Path $androidRoot "pc-app"
$version = "1.0.0"
$releaseRoot = Join-Path $androidRoot "release"
$packageDir = Join-Path $releaseRoot "Nexus-Remote-$version"
$zipPath = Join-Path $releaseRoot "Nexus-Remote-$version.zip"

New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

$files = @(
    @{ Source = (Join-Path $pcRoot "bin\Release\NexusRemotePC-Setup.msi"); Target = "NexusRemotePC-Setup.msi" }
    @{ Source = (Join-Path $androidRoot "app\build\outputs\apk\release\app-release.apk"); Target = "NexusRemote-Android.apk" }
    @{ Source = (Join-Path $androidRoot "app\build\outputs\bundle\release\app-release.aab"); Target = "NexusRemote-Android.aab" }
    @{ Source = (Join-Path $androidRoot "README.md"); Target = "README.md" }
    @{ Source = (Join-Path $androidRoot "INSTALL_WINDOWS.md"); Target = "INSTALL_WINDOWS.md" }
    @{ Source = (Join-Path $androidRoot "INSTALL_ANDROID.md"); Target = "INSTALL_ANDROID.md" }
    @{ Source = (Join-Path $androidRoot "FAQ.md"); Target = "FAQ.md" }
    @{ Source = (Join-Path $androidRoot "CHANGELOG.md"); Target = "CHANGELOG.md" }
)

foreach ($item in $files) {
    if (-not (Test-Path $item.Source)) {
        throw "Missing file: $($item.Source)"
    }
    Copy-Item -LiteralPath $item.Source -Destination (Join-Path $packageDir $item.Target) -Force
}

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath

Write-Host "Release package folder: $packageDir"
Write-Host "Release package zip: $zipPath"
