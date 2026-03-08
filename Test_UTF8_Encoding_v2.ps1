# UTF-8 Encoding Test Script v2
# Enhanced version with better display and validation

function Test-UTF8Encoding {
    Write-Host "=== UTF-8 Encoding Comprehensive Test ===" -ForegroundColor Cyan
    Write-Host ""
    
    # 1. Check all encoding settings
    Write-Host "1. Encoding Configuration Check:" -ForegroundColor Yellow
    
    $outputEnc = [Console]::OutputEncoding
    $inputEnc = [Console]::InputEncoding
    $pipeEnc = $OutputEncoding
    $codePageOutput = chcp
    
    Write-Host "   Output Encoding: $($outputEnc.EncodingName)" -ForegroundColor Gray
    Write-Host "   Input Encoding: $($inputEnc.EncodingName)" -ForegroundColor Gray
    Write-Host "   Pipe Encoding: $($pipeEnc.EncodingName)" -ForegroundColor Gray
    
    # Extract just the code page number
    if ($codePageOutput -match '(\d+)') {
        $codePage = $matches[1]
        Write-Host "   Code Page: $codePage" -ForegroundColor Gray
    }
    
    Write-Host ""
    
    # 2. Validate UTF-8 configuration
    Write-Host "2. UTF-8 Configuration Validation:" -ForegroundColor Yellow
    
    $isOutputUTF8 = $outputEnc.EncodingName -like "*UTF-8*"
    $isInputUTF8 = $inputEnc.EncodingName -like "*UTF-8*"
    $isPipeUTF8 = $pipeEnc.EncodingName -like "*UTF-8*"
    $isCodePageUTF8 = ($codePage -eq "65001")
    
    $allUTF8 = $isOutputUTF8 -and $isInputUTF8 -and $isPipeUTF8 -and $isCodePageUTF8
    
    if ($allUTF8) {
        Write-Host "   Status: [PASS] All encodings configured as UTF-8" -ForegroundColor Green
    } else {
        Write-Host "   Status: [FAIL] Some encodings not configured as UTF-8" -ForegroundColor Red
        
        Write-Host "   Details:" -ForegroundColor Gray
        Write-Host "     Output: $(if($isOutputUTF8){"[OK]"}else{"[FAIL]"})" -ForegroundColor $(if($isOutputUTF8){"Green"}else{"Red"})
        Write-Host "     Input: $(if($isInputUTF8){"[OK]"}else{"[FAIL]"})" -ForegroundColor $(if($isInputUTF8){"Green"}else{"Red"})
        Write-Host "     Pipe: $(if($isPipeUTF8){"[OK]"}else{"[FAIL]"})" -ForegroundColor $(if($isPipeUTF8){"Green"}else{"Red"})
        Write-Host "     Code Page: $(if($isCodePageUTF8){"[OK]"}else{"[FAIL]"})" -ForegroundColor $(if($isCodePageUTF8){"Green"}else{"Red"})
    }
    
    Write-Host ""
    
    # 3. Display test
    Write-Host "3. Character Display Test:" -ForegroundColor Yellow
    
    if ($allUTF8) {
        Write-Host "   Basic Latin: Hello World!" -ForegroundColor Magenta
        Write-Host "   Chinese: 中文显示测试" -ForegroundColor Magenta
        Write-Host "   Mixed: Mixed 中文 and English 混合" -ForegroundColor Magenta
        Write-Host "   Special: ©®™€¥" -ForegroundColor Cyan
        Write-Host "   Emoji: 😀🎉🌟✅❌" -ForegroundColor Cyan
    } else {
        Write-Host "   Display test skipped - UTF-8 not fully configured" -ForegroundColor Gray
    }
    
    Write-Host ""
    
    # 4. File encoding test
    Write-Host "4. File Encoding Test:" -ForegroundColor Yellow
    
    $testContent = @"
# Test file for UTF-8 encoding
English: Hello World
Chinese: 中文测试
Mixed: Mixed 中文 and English
Special: ©®™€¥
Emoji: 😀🎉🌟
"@
    
    $testFile = "test_utf8_encoding.txt"
    $testContent | Out-File -FilePath $testFile -Encoding UTF8
    
    if (Test-Path $testFile) {
        $fileSize = (Get-Item $testFile).Length
        Write-Host "   Test file created: $testFile ($fileSize bytes)" -ForegroundColor Gray
        
        # Check file encoding (basic check)
        $fileContent = Get-Content $testFile -Encoding UTF8 -TotalCount 3
        if ($fileContent) {
            Write-Host "   File read successfully with UTF-8 encoding" -ForegroundColor Green
        }
        
        # Clean up
        Remove-Item $testFile -Force
        Write-Host "   Test file cleaned up" -ForegroundColor Gray
    }
    
    Write-Host ""
    
    # 5. Summary and recommendations
    Write-Host "5. Summary:" -ForegroundColor Cyan
    
    if ($allUTF8) {
        Write-Host "   ✓ PowerShell is fully configured with UTF-8 encoding" -ForegroundColor Green
        Write-Host "   ✓ Ready for RimChat project development" -ForegroundColor Green
    } else {
        Write-Host "   ! PowerShell needs UTF-8 configuration" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "   Quick fix (current session):" -ForegroundColor Yellow
        Write-Host "   chcp 65001" -ForegroundColor Gray
        Write-Host "   [Console]::OutputEncoding = [System.Text.Encoding]::UTF8" -ForegroundColor Gray
        Write-Host "   [Console]::InputEncoding = [System.Text.Encoding]::UTF8" -ForegroundColor Gray
        Write-Host "   `$OutputEncoding = [System.Text.Encoding]::UTF8" -ForegroundColor Gray
        Write-Host ""
        Write-Host "   Permanent fix:" -ForegroundColor Yellow
        Write-Host "   Create profile: $PROFILE" -ForegroundColor Gray
        Write-Host "   Use template: .\PowerShell_UTF8_Profile.ps1" -ForegroundColor Gray
    }
    
    Write-Host ""
    Write-Host "=== Test Complete ===" -ForegroundColor Cyan
    Write-Host "Profile location: $PROFILE" -ForegroundColor Gray
    Write-Host "Project rules: UTF-8 encoding required" -ForegroundColor Gray
    
    return $allUTF8
}

# Run the test
Test-UTF8Encoding