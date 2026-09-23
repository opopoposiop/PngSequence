# PNG Sequence Video ForgeのWindows x64向け単体配布版を作成します。

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "prepare-ffmpeg.ps1")
$projectPath = Join-Path $PSScriptRoot "PngSequenceAvi.csproj"

dotnet publish `
  $projectPath `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true

if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed. Exit code: $LASTEXITCODE"
}

Write-Host ""
Write-Host "Build completed."
Write-Host "src\bin\Release\net8.0-windows\win-x64\publish\PngSequenceAvi.exe"
