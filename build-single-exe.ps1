# PNG Sequence AVI Forge の単体配布版を作るスクリプトです。
#
# ユーザーが変更してよい箇所:
# - Release/Debugの構成。ただし配布用はReleaseを推奨します。
# - win-x64。別CPU向けにする場合は、その環境で必ず起動確認してください。
#
# 変更不可の箇所:
# - --self-contained true と PublishSingleFile/IncludeNativeLibrariesForSelfExtract。
#   配布先に.NETがなくても単体exeで起動できる条件です。
#
# Codex用覚書:
# - HOW: dotnet publishでランタイムとネイティブDLLを1つのexeへまとめます。
# - WHY NOT: 通常のdotnet build成果物だけを「単体exe」として案内しません。
#   実行に複数のDLLが必要だからです。

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "prepare-ffmpeg.ps1")

dotnet publish `
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
Write-Host "bin\Release\net8.0-windows\win-x64\publish\PngSequenceAvi.exe"
