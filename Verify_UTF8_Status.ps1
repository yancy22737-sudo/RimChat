# Verify UTF-8 Status Script
# Simple script to check current UTF-8 encoding configuration

Write-Host "=== PowerShell UTF-8 Configuration Status ===" -ForegroundColor Cyan
Write-Host ""

# Check current settings
$outputEnc = [Console]::OutputEncoding.EncodingName
$inputEnc = [Console]::InputEncoding.EncodingName
$pipeEnc = $OutputEncoding.EncodingName
$codePage = chcp

Write-Host "Current Settings:" -ForegroundColor Yellow
Write-Host "  Output Encoding: $outputEnc"
Write-Host "  Input Encoding: $inputEnc"
Write-Host "  Pipe Encoding: $pipeEnc"
Write-Host "  Code Page: $codePage"
Write-Host ""

# Check if UTF-8 is configured
$isUTF8 = ($outputEnc -like "*UTF-8*") -and ($inputEnc -like "*UTF-8*") -and ($pipeEnc -like "*UTF-8*") -and ($codePage -like "*65001*")

if ($isUTF8) {
    Write-Host "Status: [PASS] UTF-8 fully configured" -ForegroundColor Green
    Write-Host ""
    Write-Host "Display Test:" -ForegroundColor Yellow
    Write-Host "  English: Hello World!" -ForegroundColor Magenta
    Write-Host "  Chinese: 中文显示正常" -ForegroundColor Magenta
    Write-Host "  Mixed: Mixed 中文 and English" -ForegroundColor Magenta
    Write-Host ""
    Write-Host "Project Compliance: ✓ Meets UTF-8 encoding requirement" -ForegroundColor Green
} else {
    Write-Host "Status: [FAIL] UTF-8 not fully configured" -ForegroundColor Red
    Write-Host ""
    Write-Host "To fix:" -ForegroundColor Yellow
    Write-Host "  Run: .\PowerShell_UTF8_Profile.ps1" -ForegroundColor Gray
    Write-Host "  Or create profile at: $PROFILE" -ForegroundColor Gray
}

Write-Host ""
Write-Host "=== Verification Complete ===" -ForegroundColor Cyan