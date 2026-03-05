<#
.SYNOPSIS
    RimSearcher MCP Service Wrapper - Ensures stable service operation
.DESCRIPTION
    This script acts as a proxy for the MCP server, checking and ensuring 
    the rim-searcher service is available on each call.
    Solves the issue of MCP server disconnecting and returning null in Trae.
.PARAMETER Action
    Action type: start, stop, restart, status, run
.EXAMPLE
    .\rim-searcher-wrapper.ps1 -Action start
    .\rim-searcher-wrapper.ps1 -Action run
#>

param(
    [ValidateSet("start", "stop", "restart", "status", "run")]
    [string]$Action = "run"
)

$ErrorActionPreference = "Stop"

$RimSearcherDir = "E:\SteamLibrary\steamapps\common\RimWorld\RimSearcher"
$ServerExe = Join-Path $RimSearcherDir "RimSearcher.server_2.exe"
$ConfigFile = Join-Path $RimSearcherDir "config.json"
$ProcessName = "RimSearcher.server_2"
$LockFile = Join-Path $RimSearcherDir ".wrapper.lock"
$LogFile = Join-Path $RimSearcherDir "wrapper.log"
$MaxRetries = 3
$RetryDelay = 2
$HealthCheckTimeout = 10

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = "[$timestamp] [$Level] $Message"
    try {
        Add-Content -Path $LogFile -Value $logEntry -Encoding UTF8 -ErrorAction SilentlyContinue
    }
    catch {}
    if ($Level -eq "ERROR") { Write-Host $logEntry -ForegroundColor Red }
    elseif ($Level -eq "WARN") { Write-Host $logEntry -ForegroundColor Yellow }
    else { Write-Host $logEntry -ForegroundColor Green }
}

function Get-ServerProcess {
    $procs = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    if ($procs) {
        return $procs | Select-Object -First 1
    }
    return $null
}

function Test-ServerHealthy {
    param([int]$TimeoutSeconds = $HealthCheckTimeout)
    
    $process = Get-ServerProcess
    if (-not $process) { return $false }
    
    if ($process.Responding -eq $false) { return $false }
    
    return $true
}

function Stop-Server {
    Write-Log "Stopping RimSearcher service..."
    
    $process = Get-ServerProcess
    if ($process) {
        try {
            $process | Stop-Process -Force -ErrorAction Stop
            Start-Sleep -Milliseconds 500
            
            $process = Get-ServerProcess
            if ($process) {
                Write-Log "Process not responding, force killing..." -Level "WARN"
                taskkill /F /IM "$ProcessName.exe" 2>$null
                Start-Sleep -Milliseconds 500
            }
        }
        catch {
            Write-Log "Error stopping process: $_" -Level "ERROR"
            taskkill /F /IM "$ProcessName.exe" 2>$null
        }
    }
    
    if (Test-Path $LockFile) {
        Remove-Item $LockFile -Force -ErrorAction SilentlyContinue
    }
    
    Write-Log "RimSearcher service stopped"
}

function Start-Server {
    param([bool]$Force = $false)
    
    if (-not $Force) {
        $existing = Get-ServerProcess
        if ($existing -and (Test-ServerHealthy)) {
            Write-Log "RimSearcher service already running (PID: $($existing.Id))"
            return $true
        }
    }
    
    Write-Log "Starting RimSearcher service..."
    
    if (-not (Test-Path $ServerExe)) {
        Write-Log "Server executable not found: $ServerExe" -Level "ERROR"
        return $false
    }
    
    Stop-Server
    
    $env:RIMSEARCHER_CONFIG = $ConfigFile
    
    try {
        $startInfo = @{
            FilePath = $ServerExe
            WorkingDirectory = $RimSearcherDir
            WindowStyle = "Hidden"
            PassThru = $true
        }
        
        $process = Start-Process @startInfo
        
        Start-Sleep -Milliseconds 1000
        
        $started = Get-ServerProcess
        if (-not $started) {
            Write-Log "Service failed to start" -Level "ERROR"
            return $false
        }
        
        $started.Id | Out-File -FilePath $LockFile -Encoding UTF8 -ErrorAction SilentlyContinue
        
        Write-Log "RimSearcher service started (PID: $($started.Id))"
        return $true
    }
    catch {
        Write-Log "Error starting service: $_" -Level "ERROR"
        return $false
    }
}

function Restart-Server {
    Write-Log "Restarting RimSearcher service..."
    Stop-Server
    Start-Sleep -Milliseconds 500
    return Start-Server -Force $true
}

function Get-ServerStatus {
    $process = Get-ServerProcess
    
    $status = [PSCustomObject]@{
        Running = $false
        Healthy = $false
        PID = $null
        StartTime = $null
        MemoryMB = $null
    }
    
    if ($process) {
        $status.Running = $true
        $status.PID = $process.Id
        $status.StartTime = $process.StartTime
        $status.MemoryMB = [math]::Round($process.WorkingSet64 / 1MB, 2)
        $status.Healthy = Test-ServerHealthy
    }
    
    return $status
}

function Invoke-WithRetry {
    param([scriptblock]$Action, [int]$MaxAttempts = $MaxRetries)
    
    $attempt = 1
    while ($attempt -le $MaxAttempts) {
        try {
            $result = & $Action
            if ($result) { return $result }
        }
        catch {
            Write-Log "Attempt $attempt/$MaxAttempts failed: $_" -Level "WARN"
        }
        
        if (-not (Test-ServerHealthy)) {
            Write-Log "Service unhealthy, attempting restart..."
            Restart-Server | Out-Null
        }
        
        Start-Sleep -Seconds $RetryDelay
        $attempt++
    }
    
    return $false
}

switch ($Action) {
    "start" {
        Start-Server
    }
    "stop" {
        Stop-Server
    }
    "restart" {
        Restart-Server
    }
    "status" {
        $status = Get-ServerStatus
        Write-Host "Running: $($status.Running)"
        Write-Host "Healthy: $($status.Healthy)"
        Write-Host "PID: $($status.PID)"
        Write-Host "StartTime: $($status.StartTime)"
        Write-Host "MemoryMB: $($status.MemoryMB) MB"
    }
    "run" {
        $success = Invoke-WithRetry -Action {
            if (-not (Test-ServerHealthy)) {
                Start-Server -Force $true
            }
            return (Test-ServerHealthy)
        }
        
        if ($success) {
            $status = Get-ServerStatus
            Write-Output "RimSearcher service ready (PID: $($status.PID))"
            exit 0
        }
        else {
            Write-Output "RimSearcher service failed to start"
            exit 1
        }
    }
}
