#Requires -Version 5.1
<#
.SYNOPSIS
    Builds a release-ready distributable ZIP for Ash and Ember.

.DESCRIPTION
    Compiles AshAndEmber.dll against the specified Bannerlord installation
    (Release configuration) then assembles the distributable folder structure and
    zips it as dist\AshAndEmber_v<version>.zip.

    The resulting ZIP contains bin\ folders for both Steam and Xbox platforms so
    a single archive works for all users.

.PARAMETER BannerlordPath
    Path to your Bannerlord root.  Defaults to the BannerlordPath environment
    variable (same convention as the build project).

.PARAMETER BannerlordBin
    Platform bin subfolder.  Defaults to Win64_Shipping_Client.

.EXAMPLE
    $env:BannerlordPath = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
    .\tools\pack.ps1

.EXAMPLE
    .\tools\pack.ps1 -BannerlordPath "D:\Games\Mount & Blade II Bannerlord"
#>
param(
    [string]$BannerlordPath = $env:BannerlordPath,
    [string]$BannerlordBin  = $(if ($env:BannerlordBin) { $env:BannerlordBin } else { "Win64_Shipping_Client" })
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot   # tools\ is one level below repo root
$ModName  = "AshAndEmber"

# ── Validate ──────────────────────────────────────────────────────────────────

if (-not $BannerlordPath) {
    Write-Error "BannerlordPath is required.  Set the environment variable or pass -BannerlordPath."
    exit 1
}
if (-not (Test-Path (Join-Path $BannerlordPath "bin"))) {
    Write-Error "No 'bin' subfolder found under '$BannerlordPath'. Is this the correct Bannerlord root?"
    exit 1
}

# ── Read version from SubModule.xml ──────────────────────────────────────────

$subModuleXml = Join-Path $RepoRoot "SubModule.xml"
$version = "unknown"
try {
    [xml]$xml = Get-Content $subModuleXml
    $version  = $xml.Module.Version.value -replace "^v", ""
} catch { }
Write-Host "Packaging version: $version"

# ── Build (Release) ───────────────────────────────────────────────────────────

Write-Host "Building..."
$env:BannerlordPath = $BannerlordPath
$env:BannerlordBin  = $BannerlordBin

$buildResult = & dotnet build "$RepoRoot\src\TheWitheringArt.csproj" -c Release -v quiet 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed:`n$buildResult"
    exit 1
}
Write-Host "Build succeeded."

$builtDll = Join-Path $RepoRoot "src\bin\Release\$ModName.dll"
if (-not (Test-Path $builtDll)) {
    Write-Error "Expected DLL not found at '$builtDll' after build."
    exit 1
}

# ── Assemble distributable ────────────────────────────────────────────────────

$distRoot   = Join-Path $RepoRoot "dist"
$stagingDir = Join-Path $distRoot $ModName

# Clean staging
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }

# Both platform bin folders — one DLL serves both platforms
$platformBins = @(
    "Win64_Shipping_Client",
    "Gaming.Desktop.x64_Shipping_Client"
)
foreach ($pb in $platformBins) {
    $pbDir = Join-Path $stagingDir "bin\$pb"
    New-Item -ItemType Directory -Force $pbDir | Out-Null
    Copy-Item $builtDll $pbDir -Force
}

# ModuleData
$dataDest = Join-Path $stagingDir "ModuleData"
New-Item -ItemType Directory -Force $dataDest | Out-Null
$dataSrc = Join-Path $RepoRoot "ModuleData"
if (Test-Path $dataSrc) {
    Copy-Item (Join-Path $dataSrc "*") $dataDest -Recurse -Force
}

# Root files
Copy-Item (Join-Path $RepoRoot "SubModule.xml") $stagingDir -Force
Copy-Item (Join-Path $RepoRoot "install.ps1")   $stagingDir -Force
Copy-Item (Join-Path $RepoRoot "README.md")     $stagingDir -Force

# ── Zip ───────────────────────────────────────────────────────────────────────

$zipPath = Join-Path $distRoot "${ModName}_v${version}.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $stagingDir -DestinationPath $zipPath

Write-Host ""
Write-Host "Release package ready:"
Write-Host "  $zipPath"
Write-Host ""
Write-Host "Contents:"
Get-ChildItem $stagingDir -Recurse | ForEach-Object {
    "  " + $_.FullName.Replace($stagingDir, "AshAndEmber")
}
