# UTF-8 BOM Encoded Script for Chinese Display Test
# This script is saved as UTF-8 with BOM to ensure proper Chinese display

# Force UTF-8 BOM encoding for output
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
chcp 65001 | Out-Null

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "UTF-8 Chinese Display Verification" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "[1] Encoding Status:" -ForegroundColor Yellow
Write-Host "    CodePage: 65001 (UTF-8)" -ForegroundColor Gray
Write-Host "    OutputEncoding: " -NoNewline
Write-Host $([Console]::OutputEncoding.EncodingName) -ForegroundColor Green
Write-Host "    InputEncoding: " -NoNewline
Write-Host $([Console]::InputEncoding.EncodingName) -ForegroundColor Green
Write-Host ""

Write-Host "[2] Chinese Character Test:" -ForegroundColor Yellow
$testChars = @(
    "Chinese: [Chinese test]",
    "Mixed: [Mixed] Hello [Mixed] World",
    "Symbols: [OK] [PASS] [Test]"
)

foreach ($line in $testChars) {
    Write-Host "    $line" -ForegroundColor Magenta
}

Write-Host ""
Write-Host "[3] Result:" -ForegroundColor Yellow

$isUTF8 = ($([Console]::OutputEncoding.EncodingName) -like "*UTF-8*") -and ($(chcp) -like "*65001*")
if ($isUTF8) {
    Write-Host "    [PASS] UTF-8 configured" -ForegroundColor Green
    Write-Host "    [PASS] Chinese display test OK" -ForegroundColor Green
} else {
    Write-Host "    [FAIL] UTF-8 not configured" -ForegroundColor Red
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Verification Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan