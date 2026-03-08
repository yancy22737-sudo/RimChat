# RimChat Build and Deploy Script
# One-click build and deploy to RimWorld Mods folder

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

# Configuration
$sourceRoot = $PSScriptRoot
$destRoot = "E:\SteamLibrary\steamapps\common\RimWorld\Mods\RimChat"

function Write-Status {
    param([string]$Message)
    Write-Host "[RimChat] $Message" -ForegroundColor Green
}

function Write-Err {
    param([string]$Message)
    Write-Host "[RimChat ERROR] $Message" -ForegroundColor Red
}

function Write-Info {
    param([string]$Message)
    Write-Host "[RimChat INFO] $Message" -ForegroundColor Cyan
}

function Invoke-EncodingGuard {
    param([string]$Root)

    $suspiciousUiPattern = '(Widgets\.(Label|ButtonText|CheckboxLabeled)|Messages\.Message|Dialog_MessageBox)\s*\([^\r\n]*"[^"\r\n]*(闂|鍙|缂|鏂|锟|顭|姊|宸叉姌鍙)[^"\r\n]*"'
    $uiHits = @()
    Get-ChildItem "$Root\RimChat" -Recurse -Filter *.cs | ForEach-Object {
        $matches = Select-String -Path $_.FullName -Pattern $suspiciousUiPattern
        foreach ($m in $matches) {
            if (-not $m.Line.TrimStart().StartsWith("//")) {
                $uiHits += $m
            }
        }
    }

    if ($uiHits.Count -gt 0) {
        Write-Err "Encoding guard failed: detected suspicious mojibake in UI string literals."
        $uiHits | Select-Object -First 20 | ForEach-Object {
            Write-Host ("  {0}:{1}: {2}" -f $_.Path, $_.LineNumber, $_.Line.Trim()) -ForegroundColor Yellow
        }
        throw "Please replace mojibake literals with proper localization keys."
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RimChat Build System" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check for running RimWorld process
Write-Status "Checking for running RimWorld process..."
Get-Process RimWorldWin64 -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Status "RimWorld process stopped (if was running)"

# Step 1.5: Encoding guard
Write-Status "Running encoding guard..."
Invoke-EncodingGuard -Root $sourceRoot

# Step 2: Build project
Write-Status "Building project..."
dotnet build "$sourceRoot\RimChat\RimChat.csproj" -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Err "Build failed."
    exit 1
}

# Step 3: Verify DLL (project outputs directly to 1.6\Assemblies\net48)
$dllSource = "$sourceRoot\1.6\Assemblies\net48\RimChat.dll"
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
