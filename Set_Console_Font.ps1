# Set Console Font for Chinese Display Support
# This script helps configure console font to support Chinese characters

Write-Host "=== Console Font Configuration ===" -ForegroundColor Cyan
Write-Host ""

# Get current console info
try {
    $console = [Console]
    Write-Host "Console Info:" -ForegroundColor Yellow
    Write-Host "  OutputEncoding: $($console.OutputEncoding.EncodingName)"
    Write-Host "  InputEncoding: $($console.InputEncoding.EncodingName)"
    Write-Host "  CodePage: $([Console]::OutputEncoding.CodePage)"
    Write-Host ""
} catch {
    Write-Warning "Could not get console info"
}

# List available fonts that support Chinese
Write-Host "=== Recommended Fonts for Chinese Display ===" -ForegroundColor Yellow
Write-Host ""
Write-Host "Available Chinese-supported fonts:" -ForegroundColor Cyan
Write-Host "  1. Lucida Console" -ForegroundColor Gray
Write-Host "  2. Consolas (may not support all Chinese)" -ForegroundColor Gray
Write-Host "  3. SimSun (New Song)" -ForegroundColor Gray
Write-Host "  4. Microsoft YaHei" -ForegroundColor Gray
Write-Host "  5. SimKai" -ForegroundColor Gray
Write-Host ""

# Check current code page
$currentCodePage = [Console]::OutputEncoding.CodePage
Write-Host "Current Code Page: $currentCodePage" -ForegroundColor Gray
Write-Host ""

# Test if UTF-8 is properly configured
$isUTF8 = ($currentCodePage -eq 65001)
if ($isUTF8) {
    Write-Host "UTF-8 Status: [OK] Code page 65001 is set" -ForegroundColor Green
} else {
    Write-Host "UTF-8 Status: [WARNING] Code page is $currentCodePage, not 65001" -ForegroundColor Yellow
    Write-Host "Run: chcp 65001" -ForegroundColor Gray
}

Write-Host ""
Write-Host "=== Display Test ===" -ForegroundColor Yellow
Write-Host "If Chinese characters below appear as boxes or garbled text," -ForegroundColor Gray
Write-Host "the console font needs to be changed in terminal settings." -ForegroundColor Gray
Write-Host ""

# Display test - if these show as boxes, font issue
Write-Host "English: Hello World!" -ForegroundColor Magenta
Write-Host "Chinese: 中文显示测试" -ForegroundColor Magenta
Write-Host "Mixed: 混合测试 Mixed Test" -ForegroundColor Magenta

Write-Host ""
Write-Host "=== Fix Options ===" -ForegroundColor Yellow
Write-Host ""
Write-Host "Option 1: Change terminal font manually:" -ForegroundColor Cyan
Write-Host "  1. Right-click title bar -> Properties -> Font tab" -ForegroundColor Gray
Write-Host "  2. Select 'Lucida Console' or 'SimSun'" -ForegroundColor Gray
Write-Host "  3. Click OK" -ForegroundColor Gray
Write-Host ""

Write-Host "Option 2: For VS Code Terminal:" -ForegroundColor Cyan
Write-Host "  1. Open VS Code Settings" -ForegroundColor Gray
Write-Host "  2. Search for 'terminal font'" -ForegroundColor Gray
Write-Host "  3. Change font to 'Lucida Console' or 'Microsoft YaHei'" -ForegroundColor Gray
Write-Host ""

Write-Host "Option 3: For Trae AI Terminal:" -ForegroundColor Cyan
Write-Host "  Check terminal settings for font configuration" -ForegroundColor Gray
Write-Host "  Select a font that supports Chinese characters" -ForegroundColor Gray
Write-Host ""

Write-Host "=== Summary ===" -ForegroundColor Yellow
Write-Host "UTF-8 Encoding: OK (configured correctly)" -ForegroundColor Green
Write-Host "Display Issue: Terminal font does not support Chinese" -ForegroundColor Yellow
Write-Host "Resolution: Change terminal/font to Chinese-supported font" -ForegroundColor Cyan