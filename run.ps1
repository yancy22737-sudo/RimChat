# RimChat Run Script
# Launch RimWorld from current Steam installation path

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$modRoot = "E:\SteamLibrary\steamapps\common\RimWorld\Mods\RimChat"
$rimWorldRoot = Split-Path (Split-Path $modRoot -Parent) -Parent
$rimWorldExePath = Join-Path $rimWorldRoot "RimWorldWin64.exe"

function Write-Status {
    param([string]$Message)
    Write-Host "[RimChat] $Message" -ForegroundColor Green
}

function Write-Err {
    param([string]$Message)
    Write-Host "[RimChat ERROR] $Message" -ForegroundColor Red
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RimChat Run Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if (Test-Path $rimWorldExePath) {
    Start-Process -FilePath $rimWorldExePath | Out-Null
    Write-Status "RimWorld launched: $rimWorldExePath"
}
else {
    Write-Err "RimWorld executable not found: $rimWorldExePath"
    exit 1
}
