# PowerShell UTF-8 Encoding Profile
# Permanent configuration for RimChat project compliance
# Save to: C:\Users\Administrator\Documents\WindowsPowerShell\Microsoft.PowerShell_profile.ps1

# ============================================
# UTF-8 ENCODING CONFIGURATION
# ============================================

# Set console code page to UTF-8 (65001)
try {
    chcp 65001 | Out-Null
} catch {
    Write-Warning "Failed to set code page to 65001"
}

# Set PowerShell output encoding to UTF-8
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
} catch {
    Write-Warning "Failed to set output encoding to UTF-8"
}

# Set PowerShell input encoding to UTF-8
try {
    [Console]::InputEncoding = [System.Text.Encoding]::UTF8
} catch {
    Write-Warning "Failed to set input encoding to UTF-8"
}

# Set PowerShell pipe encoding to UTF-8
try {
    $OutputEncoding = [System.Text.Encoding]::UTF8
} catch {
    Write-Warning "Failed to set pipe encoding to UTF-8"
}

# Set default file encoding for Out-File and similar commands
try {
    $PSDefaultParameterValues['Out-File:Encoding'] = 'utf8'
    $PSDefaultParameterValues['*:Encoding'] = 'utf8'
} catch {
    Write-Warning "Failed to set default file encoding parameters"
}

# ============================================
# VERIFICATION AND STATUS DISPLAY
# ============================================

# Display configuration status
Write-Host "=== PowerShell UTF-8 Configuration ===" -ForegroundColor Cyan

# Get current settings
$outputEnc = [Console]::OutputEncoding.EncodingName
$inputEnc = [Console]::InputEncoding.EncodingName
$pipeEnc = $OutputEncoding.EncodingName
$codePage = chcp

# Check if all settings are UTF-8
$isOutputUTF8 = $outputEnc -like "*UTF-8*"
$isInputUTF8 = $inputEnc -like "*UTF-8*"
$isPipeUTF8 = $pipeEnc -like "*UTF-8*"
$isCodePageUTF8 = $codePage -like "*65001*"

$allUTF8 = $isOutputUTF8 -and $isInputUTF8 -and $isPipeUTF8 -and $isCodePageUTF8

if ($allUTF8) {
    Write-Host "Status: [SUCCESS] UTF-8 fully configured" -ForegroundColor Green
    Write-Host "Compliance: Meets RimChat project requirements" -ForegroundColor Green
} else {
    Write-Host "Status: [WARNING] Some settings not UTF-8" -ForegroundColor Yellow
    
    Write-Host "Details:" -ForegroundColor Gray
    Write-Host "  Output: $(if($isOutputUTF8){"OK"}else{"NOT UTF-8"})" -ForegroundColor $(if($isOutputUTF8){"Green"}else{"Red"})
    Write-Host "  Input: $(if($isInputUTF8){"OK"}else{"NOT UTF-8"})" -ForegroundColor $(if($isInputUTF8){"Green"}else{"Red"})
    Write-Host "  Pipe: $(if($isPipeUTF8){"OK"}else{"NOT UTF-8"})" -ForegroundColor $(if($isPipeUTF8){"Green"}else{"Red"})
    Write-Host "  Code Page: $(if($isCodePageUTF8){"OK"}else{"NOT UTF-8"})" -ForegroundColor $(if($isCodePageUTF8){"Green"}else{"Red"})
}

Write-Host ""
Write-Host "Current Settings:" -ForegroundColor Yellow
Write-Host "  Output: $outputEnc"
Write-Host "  Input: $inputEnc"
Write-Host "  Pipe: $pipeEnc"
Write-Host "  Code Page: $codePage"

Write-Host ""
Write-Host "=== Ready for RimChat Development ===" -ForegroundColor Green
Write-Host "Project Rule: 'Ensure power shell uses UTF-8 encoding format'" -ForegroundColor Gray