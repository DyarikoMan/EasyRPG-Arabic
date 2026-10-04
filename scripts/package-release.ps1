param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Dist = Join-Path $RepoRoot "dist"

$BuildDir = Join-Path $RepoRoot "build\windows-x64-vs2022-release"
$PlayerExe = Join-Path $BuildDir "Release\Player.exe"

$ArabicRoot = Join-Path $RepoRoot "arabic"
$TemplateRoot = Join-Path $ArabicRoot "template"

if (-not $SkipBuild) {
    Write-Host "Building EasyRPG-Arabic..." -ForegroundColor Cyan

    & cmake --build $BuildDir --config Release

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed."
    }
}

if (-not (Test-Path $PlayerExe)) {
    throw "Player.exe not found: $PlayerExe"
}

if (Test-Path $Dist) {
    Remove-Item $Dist -Recurse -Force
}

New-Item -ItemType Directory -Force $Dist | Out-Null

# ------------------------------------------------------------
# Direct Player.exe asset
# ------------------------------------------------------------

Copy-Item $PlayerExe (Join-Path $Dist "Player.exe") -Force

# ------------------------------------------------------------
# Runtime package
# ------------------------------------------------------------

$RuntimeStage = Join-Path $Dist "_runtime"

New-Item -ItemType Directory -Force $RuntimeStage | Out-Null

Copy-Item $PlayerExe `
    (Join-Path $RuntimeStage "Player.exe") `
    -Force

Copy-Item `
    (Join-Path $TemplateRoot "Language") `
    $RuntimeStage `
    -Recurse `
    -Force

$RuntimeReadme = @"
EasyRPG-Arabic
==============

Copy Player.exe and the Language folder into your RPG Maker 2000/2003 game directory.

Your game-specific PO translations go inside:

Language\ar\

Basic layout:

Player.exe
Language\
  ar\
    Meta.ini
    Font\
      Font.ttf
    RPG_RT.ldb.po
    Map0001.po
    ...

Arabic is selected automatically when the Arabic translation is available.

Features:
- Arabic shaping
- RTL text
- Unicode BiDi for Arabic + Latin + numbers
- RTL dialogue typing
- Right-aligned Arabic command menus
- Bundled Arabic font at 16px
"@

[System.IO.File]::WriteAllText(
    (Join-Path $RuntimeStage "README.txt"),
    $RuntimeReadme,
    [System.Text.UTF8Encoding]::new($false)
)

$RuntimeZip = Join-Path $Dist "EasyRPG-Arabic-Windows-x64.zip"

Compress-Archive `
    -Path "$RuntimeStage\*" `
    -DestinationPath $RuntimeZip `
    -CompressionLevel Optimal

# ------------------------------------------------------------
# Translation toolkit
# ------------------------------------------------------------

$ToolkitStage = Join-Path $Dist "_toolkit"

New-Item -ItemType Directory -Force $ToolkitStage | Out-Null

Copy-Item `
    (Join-Path $ArabicRoot "template") `
    $ToolkitStage `
    -Recurse `
    -Force

Copy-Item `
    (Join-Path $ArabicRoot "terminology") `
    $ToolkitStage `
    -Recurse `
    -Force

if (Test-Path (Join-Path $ArabicRoot "tools")) {
    Copy-Item `
        (Join-Path $ArabicRoot "tools") `
        $ToolkitStage `
        -Recurse `
        -Force
}

if (Test-Path (Join-Path $ArabicRoot "README.md")) {
    Copy-Item `
        (Join-Path $ArabicRoot "README.md") `
        $ToolkitStage `
        -Force
}

$ToolkitZip = Join-Path $Dist "Arabic-Translation-Toolkit.zip"

Compress-Archive `
    -Path "$ToolkitStage\*" `
    -DestinationPath $ToolkitZip `
    -CompressionLevel Optimal

# Remove staging directories
Remove-Item $RuntimeStage -Recurse -Force
Remove-Item $ToolkitStage -Recurse -Force

Write-Host ""
Write-Host "Release package created!" -ForegroundColor Green
Write-Host ""
Write-Host "  $Dist\Player.exe"
Write-Host "  $RuntimeZip"
Write-Host "  $ToolkitZip"