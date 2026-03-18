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

function Copy-DirectoryContents {
    param(
        [string]$SourceDir,
        [string]$DestDir
    )

    if (-not (Test-Path $SourceDir)) {
        return
    }

    $sourceFullPath = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $SourceDir).Path)
    $destFullPath = [System.IO.Path]::GetFullPath($DestDir)
    if ($sourceFullPath -ieq $destFullPath) {
        return
    }

    New-Item -ItemType Directory -Path $DestDir -Force | Out-Null
    Get-ChildItem -Path $SourceDir -Force | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $DestDir -Recurse -Force
    }
}

function Clear-DirectoryContents {
    param([string]$DirPath)

    if (-not (Test-Path $DirPath)) {
        New-Item -ItemType Directory -Path $DirPath -Force | Out-Null
        return
    }

    Get-ChildItem -Path $DirPath -Force | ForEach-Object {
        Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Normalize-NpcPromptRoot {
    param([string]$NpcRoot)

    if (-not (Test-Path $NpcRoot)) {
        return
    }

    New-Item -ItemType Directory -Path $NpcRoot -Force | Out-Null
    $saveDirs = Get-ChildItem -Path $NpcRoot -Recurse -Directory -Force |
        Where-Object { $_.Name -like "Save_*" }

    foreach ($saveDir in $saveDirs) {
        $targetDir = Join-Path $NpcRoot $saveDir.Name
        $saveDirPath = [System.IO.Path]::GetFullPath($saveDir.FullName)
        $targetDirPath = [System.IO.Path]::GetFullPath($targetDir)
        if ($saveDirPath -ieq $targetDirPath) {
            continue
        }

        Copy-DirectoryContents -SourceDir $saveDir.FullName -DestDir $targetDir
    }

    Get-ChildItem -Path $NpcRoot -Directory -Force | ForEach-Object {
        if ($_.Name -notlike "Save_*") {
            Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
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
$tempPromptBackup = Join-Path $env:TEMP "RimChat_PromptBackup"
$tempPublishedFileIdBackup = Join-Path $env:TEMP "RimChat_PublishedFileIdBackup.txt"
$publishedFileIdDestPath = Join-Path $destRoot "About\\PublishedFileId.txt"
$publishedFileIdSourcePath = Join-Path $sourceRoot "About\\PublishedFileId.txt"
if (Test-Path $tempPromptBackup) {
    Remove-Item -Path $tempPromptBackup -Recurse -Force -ErrorAction SilentlyContinue
}
if (Test-Path $tempPublishedFileIdBackup) {
    Remove-Item -Path $tempPublishedFileIdBackup -Force -ErrorAction SilentlyContinue
}
if (Test-Path $publishedFileIdDestPath) {
    Write-Info "Backing up existing About/PublishedFileId.txt before deploy..."
    Copy-Item -Path $publishedFileIdDestPath -Destination $tempPublishedFileIdBackup -Force
}
if (Test-Path "$destRoot\\Prompt\\NPC") {
    Write-Info "Backing up existing Prompt/NPC before deploy..."
    Normalize-NpcPromptRoot -NpcRoot "$destRoot\\Prompt\\NPC"
    New-Item -ItemType Directory -Path $tempPromptBackup -Force | Out-Null
    if (Test-Path "$destRoot\\Prompt\\NPC") {
        Copy-DirectoryContents -SourceDir "$destRoot\\Prompt\\NPC" -DestDir "$tempPromptBackup\\NPC"
    }
}
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
if (Test-Path "$sourceRoot\doc\VersionLog.txt") {
    Copy-Item "$sourceRoot\doc\VersionLog.txt" "$destRoot\VersionLog.txt" -Force
}
if (Test-Path "$sourceRoot\doc\VersionLog_en.txt") {
    Copy-Item "$sourceRoot\doc\VersionLog_en.txt" "$destRoot\VersionLog_en.txt" -Force
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

# Always start from a clean custom prompt folder to avoid stale prompt overlays.
Write-Info "Clearing Prompt/Custom..."
Clear-DirectoryContents -DirPath "$destRoot\\Prompt\\Custom"

if (Test-Path "$tempPromptBackup\\NPC") {
    Write-Info "Restoring backed-up Prompt/NPC..."
    Copy-DirectoryContents -SourceDir "$tempPromptBackup\\NPC" -DestDir "$destRoot\\Prompt\\NPC"
    Normalize-NpcPromptRoot -NpcRoot "$destRoot\\Prompt\\NPC"
}
if (Test-Path $tempPublishedFileIdBackup) {
    Write-Info "Restoring existing About/PublishedFileId.txt..."
    $publishedFileIdDestDir = Split-Path -Path $publishedFileIdDestPath -Parent
    New-Item -ItemType Directory -Path $publishedFileIdDestDir -Force | Out-Null
    Copy-Item -Path $tempPublishedFileIdBackup -Destination $publishedFileIdDestPath -Force
    Remove-Item -Path $tempPublishedFileIdBackup -Force -ErrorAction SilentlyContinue
}
elseif (Test-Path $publishedFileIdSourcePath) {
    Write-Info "Restoring About/PublishedFileId.txt from source repository..."
    $publishedFileIdDestDir = Split-Path -Path $publishedFileIdDestPath -Parent
    New-Item -ItemType Directory -Path $publishedFileIdDestDir -Force | Out-Null
    Copy-Item -Path $publishedFileIdSourcePath -Destination $publishedFileIdDestPath -Force
}
else {
    Write-Info "PublishedFileId.txt not found in backup or source; workshop update may create a new item."
}
if (Test-Path $tempPromptBackup) {
    Remove-Item -Path $tempPromptBackup -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Build and deploy complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
