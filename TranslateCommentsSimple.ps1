# Simple PowerShell script to translate Chinese comments to English

# Function to translate Chinese text to English
function Translate-Text {
    param([string]$text)
    
    if ([string]::IsNullOrEmpty($text)) {
        return $text
    }
    
    # Simple translation mapping
    $translations = @{
        "提示词" = "prompt"
        "配置" = "configuration"
        "派系" = "faction"
        "全局" = "global"
        "特定" = "specific"
        "存储" = "store"
        "用于" = "used for"
        "显示" = "display"
        "名称" = "name"
        "系统" = "system"
        "对话" = "dialogue"
        "模板" = "template"
        "启用" = "enable"
        "是否" = "whether"
        "ID" = "ID"
        "为空" = "empty"
        "获取" = "get"
        "设置" = "settings"
        "文件夹" = "folder"
        "路径" = "path"
        "初始化" = "initialize"
    }
    
    $result = $text
    
    foreach ($key in $translations.Keys) {
        $result = $result.Replace($key, $translations[$key])
    }
    
    # Clean up punctuation
    $result = $result.Replace("。", ". ")
    $result = $result.Replace("，", ", ")
    $result = $result.Replace("：", ": ")
    $result = $result.Replace("；", "; ")
    
    # Fix double spaces
    while ($result.Contains("  ")) {
        $result = $result.Replace("  ", " ")
    }
    
    return $result.Trim()
}

# Process a single file
function Process-File {
    param([string]$filePath)
    
    Write-Host "Processing: $filePath"
    
    try {
        # Read file with UTF-8 encoding
        $content = [System.IO.File]::ReadAllText($filePath, [System.Text.Encoding]::UTF8)
        $originalContent = $content
        
        # Process XML documentation comments
        $pattern1 = '///\s*<summary>([^<]+)</summary>'
        $content = [regex]::Replace($content, $pattern1, {
            param($match)
            $inner = $match.Groups[1].Value.Trim()
            $translated = Translate-Text $inner
            return "/// <summary>$translated</summary>"
        }, [System.Text.RegularExpressions.RegexOptions]::Multiline)
        
        # Process single line comments
        $pattern2 = '//\s*([^\r\n]+)'
        $content = [regex]::Replace($content, $pattern2, {
            param($match)
            $comment = $match.Groups[1].Value.Trim()
            $translated = Translate-Text $comment
            return "// $translated"
        }, [System.Text.RegularExpressions.RegexOptions]::Multiline)
        
        # Only write if content changed
        if ($content -ne $originalContent) {
            # Create backup
            $backupPath = "$filePath.backup"
            [System.IO.File]::WriteAllText($backupPath, $originalContent, [System.Text.Encoding]::UTF8)
            
            # Write translated content
            [System.IO.File]::WriteAllText($filePath, $content, [System.Text.Encoding]::UTF8)
            
            Write-Host "  Updated and backed up to $backupPath" -ForegroundColor Green
        } else {
            Write-Host "  No changes needed" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# Main execution
if ($args.Count -eq 0) {
    Write-Host "Usage: .\TranslateCommentsSimple.ps1 <directory>"
    Write-Host "Example: .\TranslateCommentsSimple.ps1 RimChat"
    exit 1
}

$targetDir = $args[0]

if (-not (Test-Path $targetDir)) {
    Write-Host "Directory not found: $targetDir" -ForegroundColor Red
    exit 1
}

Write-Host "Starting comment translation for directory: $targetDir" -ForegroundColor Cyan

# Get all C# files
$csFiles = Get-ChildItem -Path $targetDir -Filter "*.cs" -Recurse -File

Write-Host "Found $($csFiles.Count) C# files" -ForegroundColor Yellow

foreach ($file in $csFiles) {
    Process-File $file.FullName
}

Write-Host "Translation completed!" -ForegroundColor Green