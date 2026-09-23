# 配布用LGPL版FFmpegを固定バージョン・固定ハッシュで準備します。

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$archiveName = "ffmpeg-n8.1.2-22-g94138f6973-win64-lgpl-8.1.zip"
$archiveUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-07-18-13-13/$archiveName"
$archiveSha256 = "268F45C3D6D17718BB84E3B0A7F3155D966D4B65F2FE8D059C8598A38BBE01FD"
$ffmpegSha256 = "9203AD8B3940926730575C9EE0845B5FEDCA59EC39C1D3A6161F4F6417A07C1A"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$vendorDirectory = Join-Path $repositoryRoot "vendor"
$downloadsDirectory = Join-Path $vendorDirectory "downloads"
$ffmpegDirectory = Join-Path $vendorDirectory "ffmpeg"
$archivePath = Join-Path $downloadsDirectory $archiveName
$ffmpegPath = Join-Path $ffmpegDirectory "ffmpeg.exe"

New-Item -ItemType Directory -Force -Path $downloadsDirectory, $ffmpegDirectory | Out-Null

if (Test-Path -LiteralPath $ffmpegPath) {
    $installedHash = (Get-FileHash -LiteralPath $ffmpegPath -Algorithm SHA256).Hash
    if ($installedHash -eq $ffmpegSha256) {
        & $ffmpegPath -hide_banner -version | Select-Object -First 1
        Write-Host "FFmpeg is ready: $ffmpegPath"
        exit 0
    }

    Remove-Item -LiteralPath $ffmpegPath -Force
}

function Test-ArchiveHash {
    if (-not (Test-Path -LiteralPath $archivePath)) {
        return $false
    }

    return (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -eq $archiveSha256
}

if (-not (Test-ArchiveHash)) {
    Write-Host "Downloading pinned LGPL FFmpeg build..."
    & curl.exe -L --fail --retry 5 --retry-delay 2 -C - -o $archivePath $archiveUrl
    if ($LASTEXITCODE -ne 0) {
        throw "FFmpeg download failed. Exit code: $LASTEXITCODE"
    }
}

if (-not (Test-ArchiveHash)) {
    throw "FFmpeg archive SHA-256 verification failed: $archivePath"
}

$extractDirectory = Join-Path $vendorDirectory ("extract-" + [Guid]::NewGuid().ToString("N"))
try {
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractDirectory
    $sourceFfmpeg = Get-ChildItem -LiteralPath $extractDirectory -Filter "ffmpeg.exe" -File -Recurse |
        Select-Object -First 1
    if ($null -eq $sourceFfmpeg) {
        throw "ffmpeg.exe was not found in the verified archive."
    }

    Copy-Item -LiteralPath $sourceFfmpeg.FullName -Destination $ffmpegPath -Force
    if ((Get-FileHash -LiteralPath $ffmpegPath -Algorithm SHA256).Hash -ne $ffmpegSha256) {
        throw "Extracted ffmpeg.exe SHA-256 verification failed."
    }
}
finally {
    if (Test-Path -LiteralPath $extractDirectory) {
        $resolvedVendor = (Resolve-Path $vendorDirectory).Path
        $resolvedExtract = (Resolve-Path $extractDirectory).Path
        if (-not $resolvedExtract.StartsWith($resolvedVendor + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove an extraction directory outside vendor: $resolvedExtract"
        }

        Remove-Item -LiteralPath $resolvedExtract -Recurse -Force
    }
}

& $ffmpegPath -hide_banner -version | Select-Object -First 1
if ($LASTEXITCODE -ne 0) {
    throw "Prepared ffmpeg.exe could not be executed."
}

Write-Host "FFmpeg is ready: $ffmpegPath"
