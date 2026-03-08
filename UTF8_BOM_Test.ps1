# UTF-8 with BOM Test Script
# Test Chinese display with UTF-8 BOM encoding

Write-Host "=== UTF-8 BOM Test ===" -ForegroundColor Cyan
Write-Host ""

# Test direct Chinese output
Write-Host "Test: Chinese characters" -ForegroundColor Yellow
Write-Host "Chinese test: " -NoNewline
Write-Host "Chinese display OK" -ForegroundColor Green

Write-Host ""
Write-Host "Test: Mixed content" -ForegroundColor Yellow  
Write-Host "Mixed: Mixed Chinese and English test" -ForegroundColor Green

Write-Host ""
Write-Host "=== Complete ===" -ForegroundColor Cyan