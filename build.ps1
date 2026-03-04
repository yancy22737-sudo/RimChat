# RimDiplomacy Build and Deploy Script
# One-click build and deploy to RimWorld Mods folder

$ErrorActionPreference = "Stop"

# Configuration
$sourceRoot = "C:\Users\Administrator\source\repos\RimDiplomacy"
$destRoot = "E:\SteamLibrary\steamapps\common\RimWorld\Mods\RimDiplomacy"

function Write-Status {
    param([string]$Message)
    Write-Host "[RimDiplomacy] $Message" -ForegroundColor Green
}

function Write-Err {
    param([string]$Message)
    Write-Host "[RimDiplomacy ERROR] $Message" -ForegroundColor Red
}

function Write-Info {
    param([string]$Message)
    Write-Host "[RimDiplomacy INFO] $Message" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RimDiplomacy Build System" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check for running RimWorld process
Write-Status "Checking for running RimWorld process..."
Get-Process RimWorldWin64 -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Status "RimWorld process stopped (if was running)"

# Step 2: Build project
Write-Status "Building project..."
dotnet build "$sourceRoot\RimDiplomacy\RimDiplomacy.csproj" -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Err "Build failed."
    exit 1
}

# Step 3: Verify DLL (project outputs directly to 1.6\Assemblies\net48)
$dllSource = "$sourceRoot\1.6\Assemblies\net48\RimDiplomacy.dll"
$dllDest = "$sourceRoot\1.6\Assemblies"

if (-not (Test-Path $dllSource)) {
    Write-Err "DLL not found at $dllSource"
    exit 1
}

# Step 4: Copy DLL from net48 subfolder to Assemblies root
Write-Status "Copying DLL to 1.6/Assemblies..."
if (-not (Test-Path $dllDest)) {
    New-Item -ItemType Directory -Path $dllDest -Force | Out-Null
}
Copy-Item $dllSource $dllDest -Force

# Step 5: Deploy to Game Mod Folder
Write-Status "Deploying to Game Mod Folder: $destRoot"
if (Test-Path $destRoot) {
    # Clear destination to avoid stale files (important for XML/Patch moves)
    Remove-Item -Path $destRoot -Recurse -Force | Out-Null
}
New-Item -ItemType Directory -Path $destRoot -Force | Out-Null

# Copy About
Write-Info "Copying About..."
if (Test-Path "$sourceRoot\About") {
    Copy-Item "$sourceRoot\About" "$destRoot" -Recurse -Force
}

# Copy 1.6 (includes Assemblies, Defs, Languages)
Write-Info "Copying 1.6..."
if (Test-Path "$sourceRoot\1.6") {
    Copy-Item "$sourceRoot\1.6" "$destRoot" -Recurse -Force
}

# Copy VersionLog files
Write-Info "Copying VersionLog files..."
if (Test-Path "$sourceRoot\VersionLog.txt") {
    Copy-Item "$sourceRoot\VersionLog.txt" "$destRoot\VersionLog.txt" -Force
}
if (Test-Path "$sourceRoot\VersionLog_en.txt") {
    Copy-Item "$sourceRoot\VersionLog_en.txt" "$destRoot\VersionLog_en.txt" -Force
}

# Copy README
if (Test-Path "$sourceRoot\README.md") {
    Copy-Item "$sourceRoot\README.md" "$destRoot\README.md" -Force
}

# Copy Prompt folder (Required for AI logic)
Write-Info "Copying Prompt folder..."
if (Test-Path "$sourceRoot\Prompt") {
    Copy-Item "$sourceRoot\Prompt" "$destRoot" -Recurse -Force
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Build and deploy complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
