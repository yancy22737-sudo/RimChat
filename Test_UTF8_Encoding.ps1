# UTF-8 Encoding Test Script
# Used to verify if PowerShell is correctly configured with UTF-8 encoding

Write-Host "=== UTF-8 Encoding Test ===" -ForegroundColor Cyan
Write-Host ""

# 1. Check current encoding settings
Write-Host "1. Current Encoding Settings:" -ForegroundColor Yellow
$outputEncoding = [Console]::OutputEncoding.EncodingName
$inputEncoding = [Console]::InputEncoding.EncodingName
$pipeEncoding = $OutputEncoding.EncodingName
$codePage = chcp

Write-Host "   Output Encoding: $outputEncoding" -ForegroundColor Gray
Write-Host "   Input Encoding: $inputEncoding" -ForegroundColor Gray
Write-Host "   Pipe Encoding: $pipeEncoding" -ForegroundColor Gray
Write-Host "   Code Page: $codePage" -ForegroundColor Gray
Write-Host ""

# 2. Check if UTF-8 requirements are met
$isUTF8 = ($outputEncoding -like "*UTF-8*") -and ($inputEncoding -like "*UTF-8*") -and ($codePage -like "*65001*")
if ($isUTF8) {
    Write-Host "2. Encoding Status: ✓ Configured as UTF-8" -ForegroundColor Green
} else {
    Write-Host "2. Encoding Status: ✗ Not configured as UTF-8" -ForegroundColor Red
    Write-Host "   Need to configure UTF-8 encoding according to the guide" -ForegroundColor Yellow
}
Write-Host ""

# 3. Test Chinese display (if UTF-8 is configured)
Write-Host "3. Display Test:" -ForegroundColor Yellow
if ($isUTF8) {
    Write-Host "   UTF-8 Test: Chinese display normal" -ForegroundColor Magenta
    Write-Host "   English test: Display normal" -ForegroundColor Magenta
    Write-Host "   Mixed test: Mixed Chinese and English" -ForegroundColor Magenta
} else {
    Write-Host "   Display test skipped (UTF-8 not configured)" -ForegroundColor Gray
}
Write-Host ""

# 4. Provide repair suggestions
if (-not $isUTF8) {
    Write-Host "=== Repair Suggestions ===" -ForegroundColor Red
    Write-Host "1. Temporary fix (valid for current session):" -ForegroundColor Yellow
    Write-Host "   chcp 65001" -ForegroundColor Gray
    Write-Host "   [Console]::OutputEncoding = [System.Text.Encoding]::UTF8" -ForegroundColor Gray
    Write-Host "   [Console]::InputEncoding = [System.Text.Encoding]::UTF8" -ForegroundColor Gray
    Write-Host "   `$OutputEncoding = [System.Text.Encoding]::UTF8" -ForegroundColor Gray
    Write-Host ""
    Write-Host "2. Permanent fix:" -ForegroundColor Yellow
    Write-Host "   Please refer to PowerShell_UTF8_Setup_Guide.md file" -ForegroundColor Gray
    Write-Host "   Or run the following command to create profile:" -ForegroundColor Gray
    Write-Host "   .\PowerShell_UTF8_Profile.ps1" -ForegroundColor Gray
}

Write-Host ""
Write-Host "=== Test Complete ===" -ForegroundColor Cyan
Write-Host "Profile location: $PROFILE" -ForegroundColor Gray
Write-Host "Guide file: PowerShell_UTF8_Setup_Guide.md" -ForegroundColor Gray