#Requires -Version 5.1
<#
.SYNOPSIS
    Installs Ash and Ember into your Bannerlord Modules folder.

.DESCRIPTION
    Auto-detects your Bannerlord installation (Steam via registry, Steam via default
    path, or Xbox Game Pass via C:\XboxGames scan).  Override with -BannerlordPath
    if your game is installed somewhere non-standard.

.PARAMETER BannerlordPath
    Full path to the Bannerlord root directory (the folder that contains 'bin' and
    'Modules' subfolders).  Leave empty for auto-detection.

.EXAMPLE
    # Auto-detect:
    .\install.ps1

.EXAMPLE
    # Custom path (e.g. game on D:\):
    .\install.ps1 -BannerlordPath "D:\Games\Mount & Blade II Bannerlord"

.EXAMPLE
    # Xbox / Game Pass with explicit GUID folder:
    .\install.ps1 -BannerlordPath "C:\XboxGames\Mount & Blade II- Bannerlord\Content"
#>
param(
    [string]$BannerlordPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ModName    = "AshAndEmber"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

# ── Locate game root ─────────────────────────────────────────────────────────

function Find-BannerlordPath {
    # 1. Steam via registry (most reliable for non-default library locations)
    $regPaths = @(
        "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam",
        "HKCU:\Software\Valve\Steam"
    )
    foreach ($rp in $regPaths) {
        try {
            $steamRoot = (Get-ItemProperty $rp -ErrorAction Stop).InstallPath
            $candidate = Join-Path $steamRoot "steamapps\common\Mount & Blade II Bannerlord"
            if (Test-Path (Join-Path $candidate "bin")) { return $candidate }
        } catch { }
    }

    # 2. Steam default path
    $steamDefault = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
    if (Test-Path (Join-Path $steamDefault "bin")) { return $steamDefault }

    # 3. Xbox / Game Pass — GUID folder varies, scan C:\XboxGames\
    if (Test-Path "C:\XboxGames") {
        foreach ($dir in Get-ChildItem "C:\XboxGames" -Directory -ErrorAction SilentlyContinue) {
            $candidate = Join-Path $dir.FullName "Content"
            if (Test-Path (Join-Path $candidate "bin")) { return $candidate }
        }
    }

    return $null
}

if (-not $BannerlordPath) {
    $BannerlordPath = Find-BannerlordPath
    if (-not $BannerlordPath) {
        Write-Error @"
Could not auto-detect your Bannerlord installation.
Please run the script with -BannerlordPath, e.g.:
  .\install.ps1 -BannerlordPath "D:\Games\Mount & Blade II Bannerlord"
"@
        exit 1
    }
    Write-Host "Detected Bannerlord at: $BannerlordPath"
}

if (-not (Test-Path (Join-Path $BannerlordPath "bin"))) {
    Write-Error "Path '$BannerlordPath' does not look like a Bannerlord root (no 'bin' subfolder found)."
    exit 1
}

# ── Determine platform (Steam vs Xbox) ───────────────────────────────────────

$binFolders = @(
    "Win64_Shipping_Client",
    "Gaming.Desktop.x64_Shipping_Client"
)

$detectedBin = $null
foreach ($bf in $binFolders) {
    if (Test-Path (Join-Path $BannerlordPath "bin\$bf")) {
        $detectedBin = $bf
        break
    }
}

if (-not $detectedBin) {
    Write-Error "Could not find a known platform bin folder under '$BannerlordPath\bin\'."
    exit 1
}
Write-Host "Platform bin: $detectedBin"

# ── Locate the pre-built DLL ─────────────────────────────────────────────────
# When installing from a release ZIP the DLL is already in the bin subfolder
# next to this script.  If running from the repo after a build it lives in
# src\bin\Release\ or src\bin\Debug\.

$dllCandidates = @(
    (Join-Path $ScriptRoot "bin\$detectedBin\$ModName.dll"),
    (Join-Path $ScriptRoot "bin\Win64_Shipping_Client\$ModName.dll"),
    (Join-Path $ScriptRoot "bin\Gaming.Desktop.x64_Shipping_Client\$ModName.dll"),
    (Join-Path $ScriptRoot "src\bin\Release\$ModName.dll"),
    (Join-Path $ScriptRoot "src\bin\Debug\$ModName.dll")
)

$sourceDll = $null
foreach ($c in $dllCandidates) {
    if (Test-Path $c) { $sourceDll = $c; break }
}

if (-not $sourceDll) {
    Write-Error @"
DLL not found. Either:
  a) Download a release ZIP (which includes the pre-built DLL), or
  b) Build from source first:
       `$env:BannerlordPath = '$BannerlordPath'
       dotnet build src\TheWitheringArt.csproj
"@
    exit 1
}
Write-Host "Using DLL: $sourceDll"

# ── Install ───────────────────────────────────────────────────────────────────

$modDest    = Join-Path $BannerlordPath "Modules\$ModName"
$modBinDest = Join-Path $modDest "bin\$detectedBin"
$modDataDest = Join-Path $modDest "ModuleData"

New-Item -ItemType Directory -Force $modBinDest  | Out-Null
New-Item -ItemType Directory -Force $modDataDest | Out-Null

Copy-Item (Join-Path $ScriptRoot "SubModule.xml") $modDest -Force
Copy-Item $sourceDll $modBinDest -Force

$dataSource = Join-Path $ScriptRoot "ModuleData"
if (Test-Path $dataSource) {
    Copy-Item (Join-Path $dataSource "*") $modDataDest -Recurse -Force
}

Write-Host ""
Write-Host "Installed successfully to:"
Write-Host "  $modDest"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Launch Mount & Blade II: Bannerlord"
Write-Host "  2. Click Mods in the launcher"
Write-Host "  3. Enable 'Ash and Ember' and click Play"
