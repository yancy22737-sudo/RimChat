# Simple Chinese Display Test
# Direct output without reading from file

Write-Host "=== Direct Chinese Output Test ===" -ForegroundColor Cyan

# Test 1: Direct string output
Write-Host ""
Write-Host "Test 1: Direct string output" -ForegroundColor Yellow
Write-Host "Chinese: 中文测试" -ForegroundColor Green
Write-Host "Mixed: Hello 中文 World" -ForegroundColor Green

# Test 2: Using Write-Output
Write-Host ""
Write-Host "Test 2: Using Write-Output" -ForegroundColor Yellow
Write-Output "中文测试"

# Test 3: Check actual encoding
Write-Host ""
Write-Host "Test 3: Encoding Check" -ForegroundColor Yellow
Write-Host "OutputEncoding: $([Console]::OutputEncoding.EncodingName)"
Write-Host "InputEncoding: $([Console]::InputEncoding.EncodingName)"
Write-Host "CodePage: $(chcp)"

# Test 4: String manipulation
Write-Host ""
Write-Host "Test 4: String encoding check" -ForegroundColor Yellow
$testString = "中文测试"
Write-Host "String: $testString"
Write-Host "String Length: $($testString.Length)"
Write-Host "Bytes: $([System.Text.Encoding]::UTF8.GetBytes($testString))"

Write-Host ""
Write-Host "=== Test Complete ===" -ForegroundColor Cyan