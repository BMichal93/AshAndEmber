#Requires -Version 5.1
<#
.SYNOPSIS
    Installs Ash and Ember into your Bannerlord Modules folder.

.DESCRIPTION
    Auto-detects your Bannerlord installation (Steam via registry, Steam default
    paths, or Xbox Game Pass via C:\XboxGames scan). Override with -BannerlordPath
    if your game is in a non-standard location.

    By default uses the pre-built Release DLL included with the mod.
    Use -BuildFirst to compile from source (requires .NET SDK 6+).

.PARAMETER BannerlordPath
    Full path to the Bannerlord root directory (contains 'bin' and 'Modules').
    Leave empty for auto-detection.

.PARAMETER BuildFirst
    Rebuild AshAndEmber.dll from source before installing. Requires dotnet SDK.

.EXAMPLE
    .\install.ps1

.EXAMPLE
    .\install.ps1 -BannerlordPath "D:\Games\Mount & Blade II Bannerlord"

.EXAMPLE
    .\install.ps1 -BuildFirst
#>
param(
    [string] $BannerlordPath = "",
    [switch] $BuildFirst
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ModName    = "AshAndEmber"
$ModVer     = "v0.10"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
# Module files live in the AshAndEmber\ subfolder next to this script.
# (Allows users to copy-paste that folder directly into Modules\ without running the script.)
$ModRoot    = Join-Path $ScriptRoot $ModName

function Write-Step ($n, $msg) { Write-Host "`n[$n] $msg" -ForegroundColor Cyan }
function Write-OK   ($msg)     { Write-Host "    OK  $msg" -ForegroundColor Green }
function Write-Warn ($msg)     { Write-Host "    !!  $msg" -ForegroundColor Yellow }
function Write-Fail ($msg)     { Write-Host "    XX  $msg" -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "  ============================================" -ForegroundColor Cyan
Write-Host "   Ash and Ember  $ModVer  —  Installer"       -ForegroundColor Cyan
Write-Host "  ============================================" -ForegroundColor Cyan

# ─────────────────────────────────────────────────────────────────────────────
# STEP 1 — Locate Bannerlord
# ─────────────────────────────────────────────────────────────────────────────
Write-Step 1 "Locating Bannerlord..."

function Find-BannerlordPath {
    # Steam: registry (handles non-default library folders)
    foreach ($rp in @("HKLM:\SOFTWARE\WOW6432Node\Valve\Steam","HKCU:\Software\Valve\Steam")) {
        try {
            $steamRoot = (Get-ItemProperty $rp -EA Stop).InstallPath
            $c = Join-Path $steamRoot "steamapps\common\Mount & Blade II Bannerlord"
            if (Test-Path (Join-Path $c "bin")) { return $c }
        } catch {}
    }
    # Steam: common default paths
    foreach ($c in @(
        "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord",
        "C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord",
        "D:\Steam\steamapps\common\Mount & Blade II Bannerlord",
        "D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord",
        "E:\Steam\steamapps\common\Mount & Blade II Bannerlord",
        "E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord"
    )) { if (Test-Path (Join-Path $c "bin")) { return $c } }

    # Xbox Game Pass: known GUID (most installs land here)
    $xboxKnown = "C:\XboxGames\C5E01182-C50B-4253-8B15-A3376314D4DD\Content"
    if (Test-Path (Join-Path $xboxKnown "bin")) { return $xboxKnown }

    # Xbox Game Pass: generic scan of C:\XboxGames\
    if (Test-Path "C:\XboxGames") {
        foreach ($dir in Get-ChildItem "C:\XboxGames" -Directory -EA SilentlyContinue) {
            $c = Join-Path $dir.FullName "Content"
            if (Test-Path (Join-Path $c "bin")) { return $c }
        }
    }
    return $null
}

if (-not $BannerlordPath) {
    $BannerlordPath = Find-BannerlordPath
    if (-not $BannerlordPath) {
        Write-Host ""
        Write-Warn "Auto-detection failed. Enter the full path to your Bannerlord root directory."
        Write-Warn "(It must contain a 'bin' folder.)"
        $BannerlordPath = Read-Host "    Path"
    }
}

if (-not (Test-Path (Join-Path $BannerlordPath "bin"))) {
    Write-Fail "'$BannerlordPath' does not look like a valid Bannerlord root (no bin\ folder)."
}
Write-OK "Game root: $BannerlordPath"

# Detect platform
$detectedBin = $null
foreach ($bf in @("Win64_Shipping_Client","Gaming.Desktop.x64_Shipping_Client")) {
    if (Test-Path (Join-Path $BannerlordPath "bin\$bf")) { $detectedBin = $bf; break }
}
if (-not $detectedBin) { Write-Fail "Cannot determine platform (no known bin\ subfolder found)." }
Write-OK "Platform:   $detectedBin"

# ─────────────────────────────────────────────────────────────────────────────
# STEP 2 — Build (optional) or locate pre-built DLL
# ─────────────────────────────────────────────────────────────────────────────
Write-Step 2 "Preparing DLL..."

$csprojPath = Join-Path $ScriptRoot "src\TheWitheringArt.csproj"

if ($BuildFirst) {
    if (-not (Test-Path $csprojPath)) { Write-Fail "src\TheWitheringArt.csproj not found. Cannot build." }
    $dotnetExe = (Get-Command dotnet -EA SilentlyContinue)?.Source
    if (-not $dotnetExe) { Write-Fail ".NET SDK not found. Install from https://dot.net or omit -BuildFirst." }

    Write-Warn "Building from source (this may take a moment)..."
    $env:BannerlordPath = $BannerlordPath
    $env:BannerlordBin  = $detectedBin
    $out = & dotnet build $csprojPath -c Release --no-incremental -nologo 2>&1
    $dllBuilt = Join-Path $ScriptRoot "src\bin\Release\$ModName.dll"
    if (-not (Test-Path $dllBuilt)) {
        $out | ForEach-Object { Write-Host "    $_" }
        Write-Fail "Build failed — DLL not produced."
    }
    Write-OK "Built: $dllBuilt"
}

# Find best available DLL (prefer pre-built inside AshAndEmber\bin\, fall back to src\bin\)
$sourceDll = $null
foreach ($c in @(
    (Join-Path $ModRoot    "bin\$detectedBin\$ModName.dll"),
    (Join-Path $ModRoot    "bin\Win64_Shipping_Client\$ModName.dll"),
    (Join-Path $ModRoot    "bin\Gaming.Desktop.x64_Shipping_Client\$ModName.dll"),
    (Join-Path $ScriptRoot "src\bin\Release\$ModName.dll"),
    (Join-Path $ScriptRoot "src\bin\Debug\$ModName.dll")
)) { if (Test-Path $c) { $sourceDll = $c; break } }

if (-not $sourceDll) {
    Write-Fail @"
No DLL found. Options:
  a) Run with -BuildFirst to compile from source (needs .NET SDK).
  b) Download a release package that includes the pre-built DLL.
  c) Build manually:  dotnet build src\TheWitheringArt.csproj -c Release
"@
}
Write-OK "DLL:        $sourceDll"

# ─────────────────────────────────────────────────────────────────────────────
# STEP 3 — Install files
# ─────────────────────────────────────────────────────────────────────────────
Write-Step 3 "Installing mod files..."

$modDest     = Join-Path $BannerlordPath "Modules\$ModName"
$modBinDest  = Join-Path $modDest "bin\$detectedBin"
$modDataDest = Join-Path $modDest "ModuleData"

$null = New-Item -ItemType Directory -Force $modBinDest
$null = New-Item -ItemType Directory -Force $modDataDest

# SubModule.xml
$subXml = Join-Path $ModRoot "SubModule.xml"
if (-not (Test-Path $subXml)) { Write-Fail "SubModule.xml not found in $ModRoot." }
Copy-Item $subXml $modDest -Force
Write-OK "SubModule.xml"

# ModuleData
$dataDir = Join-Path $ModRoot "ModuleData"
if (Test-Path $dataDir) {
    Copy-Item (Join-Path $dataDir "*") $modDataDest -Recurse -Force
    Get-ChildItem $dataDir -File | ForEach-Object { Write-OK "ModuleData\$($_.Name)" }
} else {
    Write-Warn "ModuleData\ not found — XML data files skipped."
}

# DLL
try {
    Copy-Item $sourceDll $modBinDest -Force
    Write-OK "bin\$detectedBin\$ModName.dll"
} catch {
    Write-Fail "Could not copy DLL: $_`n    Is Bannerlord running? Close it and try again."
}

# ─────────────────────────────────────────────────────────────────────────────
# STEP 4 — Verify
# ─────────────────────────────────────────────────────────────────────────────
Write-Step 4 "Verifying..."

$required = @(
    (Join-Path $modDest     "SubModule.xml"),
    (Join-Path $modBinDest  "$ModName.dll"),
    (Join-Path $modDataDest "troops.xml"),
    (Join-Path $modDataDest "items.xml")
)
$ok = $true
foreach ($f in $required) {
    if (Test-Path $f) { Write-OK $f.Replace($BannerlordPath, "").TrimStart("\") }
    else              { Write-Warn "Missing: $($f.Replace($BannerlordPath,'').TrimStart('\'))"; $ok = $false }
}

# ─────────────────────────────────────────────────────────────────────────────
# Done
# ─────────────────────────────────────────────────────────────────────────────
Write-Host ""
if ($ok) {
    Write-Host "  ============================================" -ForegroundColor Green
    Write-Host "   Installation complete!  ($modDest)"         -ForegroundColor Green
    Write-Host "  ============================================" -ForegroundColor Green
} else {
    Write-Host "  ============================================" -ForegroundColor Yellow
    Write-Host "   Installation finished with warnings."        -ForegroundColor Yellow
    Write-Host "  ============================================" -ForegroundColor Yellow
}

Write-Host @"

  Load order (Bannerlord launcher → Mods tab):
    Native  →  SandBoxCore  →  Sandbox  →  StoryMode  →  Ash and Ember

  IMPORTANT: Start a NEW game after enabling the mod.
             Loading an existing save that predates the mod may cause issues.

"@
