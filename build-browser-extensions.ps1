$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$extRoot = Join-Path $repoRoot "browser-extension"
$distRoot = Join-Path $extRoot "dist"
$iconsRoot = Join-Path $extRoot "icons"
$srcIcon = Join-Path $extRoot "source-assets\\icon-source.png"

New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
New-Item -ItemType Directory -Force -Path $iconsRoot | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function New-ResizedIcon {
    param(
        [string]$SourcePath,
        [string]$OutputPath,
        [int]$Size
    )

    $bitmap = [System.Drawing.Bitmap]::FromFile($SourcePath)
    try {
        $target = New-Object System.Drawing.Bitmap $Size, $Size
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($target)
            try {
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.DrawImage($bitmap, 0, 0, $Size, $Size)
            }
            finally {
                $graphics.Dispose()
            }
            $target.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $target.Dispose()
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

16, 32, 48, 96, 128 | ForEach-Object {
    New-ResizedIcon -SourcePath $srcIcon -OutputPath (Join-Path $iconsRoot "icon-$_.png") -Size $_
}

function New-BrowserPackage {
    param(
        [string]$Name,
        [string]$ManifestPath,
        [string]$ArchivePath,
        [string]$ManifestFileName = "manifest.json",
        [bool]$CreateArchive = $true
    )

    $workDir = Join-Path $distRoot $Name
    if (Test-Path $workDir) {
        Remove-Item $workDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $workDir | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $workDir "icons") | Out-Null

    Copy-Item $ManifestPath (Join-Path $workDir $ManifestFileName) -Force
    Copy-Item (Join-Path $extRoot "background.js") $workDir -Force
    Copy-Item (Join-Path $extRoot "content.js") $workDir -Force
    Copy-Item (Join-Path $extRoot "popup.html") $workDir -Force
    Copy-Item (Join-Path $extRoot "popup.css") $workDir -Force
    Copy-Item (Join-Path $extRoot "popup.js") $workDir -Force
    Copy-Item (Join-Path $iconsRoot "*") (Join-Path $workDir "icons") -Force

    if (-not $CreateArchive) {
        return
    }

    if (Test-Path $ArchivePath) {
        Remove-Item $ArchivePath -Force
    }

    $zipPath = if ($ArchivePath.EndsWith(".xpi", [StringComparison]::OrdinalIgnoreCase)) {
        [System.IO.Path]::ChangeExtension($ArchivePath, ".zip")
    } else {
        $ArchivePath
    }

    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    $archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $basePath = (Resolve-Path $workDir).Path
        Get-ChildItem -Path $workDir -Recurse -File | ForEach-Object {
            $fullPath = $_.FullName
            $relativePath = $fullPath.Substring($basePath.Length).TrimStart('\', '/')
            $entryName = $relativePath -replace '\\', '/'
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $fullPath, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
    }

    if ($ArchivePath.EndsWith(".xpi", [StringComparison]::OrdinalIgnoreCase)) {
        Move-Item $zipPath $ArchivePath -Force
    }
}

New-BrowserPackage `
    -Name "chrome-unpacked" `
    -ManifestPath (Join-Path $extRoot "manifest.chromium.json") `
    -ArchivePath (Join-Path $distRoot "Nexus-Remote-Browser-Bridge-Chrome.zip")

New-BrowserPackage `
    -Name "yandex-unpacked" `
    -ManifestPath (Join-Path $extRoot "manifest.chromium.json") `
    -ArchivePath (Join-Path $distRoot "Nexus-Remote-Browser-Bridge-Yandex.zip")

New-BrowserPackage `
    -Name "chromium-unpacked" `
    -ManifestPath (Join-Path $extRoot "manifest.chromium.json") `
    -ArchivePath (Join-Path $distRoot "Nexus-Remote-Browser-Bridge-Chromium.zip")

New-BrowserPackage `
    -Name "firefox-unpacked" `
    -ManifestPath (Join-Path $extRoot "manifest.firefox.json") `
    -ArchivePath (Join-Path $distRoot "Nexus-Remote-Browser-Bridge-Firefox.xpi")

Write-Host "Browser extension packages built:"
Get-ChildItem $distRoot | Where-Object { -not $_.PSIsContainer } | Select-Object Name, Length, LastWriteTime
