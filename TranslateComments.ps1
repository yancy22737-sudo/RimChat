# PowerShell script to translate Chinese comments to English in C# files

# Translation dictionary
$translationDict = @{
    # Common terms
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
    "管理器" = "manager"
    "应用" = "apply"
    "补丁" = "patch"
    "和谐" = "harmony"
    "动态" = "dynamic"
    "方法" = "method"
    "查找" = "lookup"
    "状态" = "state"
    "结果" = "result"
    "成功" = "success"
    "响应" = "response"
    "错误" = "error"
    "进度" = "progress"
    "开始时间" = "start time"
    "持续时间" = "duration"
    "请求" = "request"
    "处理" = "processing"
    "完成" = "completed"
    "空闲" = "idle"
    "等待" = "pending"
    "处理中" = "processing"
    "已完成" = "completed"
    "错误" = "error"
    "通道" = "channel"
    "未知" = "unknown"
    "外交" = "diplomacy"
    "角色扮演" = "RPG"
    "模组" = "mod"
    "成功" = "successfully"
    "日志" = "log"
    "消息" = "message"
    "类别" = "category"
    "翻译" = "translate"
    "窗口" = "window"
    "内容" = "contents"
    "矩形" = "rectangle"
    
    # UI related
    "界面" = "interface"
    "用户" = "user"
    "按钮" = "button"
    "标签" = "label"
    "文本" = "text"
    "输入" = "input"
    "输出" = "output"
    "选择" = "select"
    "保存" = "save"
    "加载" = "load"
    "编辑" = "edit"
    "编辑器" = "editor"
    "文件" = "file"
    
    # Game specific
    "殖民地" = "colony"
    "世界" = "world"
    "事件" = "event"
    "记录" = "record"
    "领袖" = "leader"
    "记忆" = "memory"
    "会话" = "session"
    "存在" = "presence"
    "状态" = "state"
    "关系" = "relation"
    "好感度" = "goodwill"
    "成本" = "cost"
    "计算器" = "calculator"
    "上下文" = "context"
    "值" = "values"
    "行为" = "behavior"
    "阈值" = "threshold"
    "基于" = "based on"
    "规则" = "rules"
    
    # AI specific
    "人工智能" = "AI"
    "聊天" = "chat"
    "服务" = "service"
    "异步" = "async"
    "提供者" = "provider"
    "解析器" = "parser"
    "执行器" = "executor"
    "客户端" = "client"
    "上下文" = "context"
    "压缩" = "compression"
    "服务" = "service"
    "驱动" = "driver"
    "工作" = "job"
    "关系" = "relation"
    "响应" = "response"
    "应用程序接口" = "API"
}

function Translate-Comment {
    param([string]$comment)
    
    if ([string]::IsNullOrEmpty($comment)) {
        return $comment
    }
    
    $result = $comment
    
    # Replace known terms
    foreach ($key in $translationDict.Keys) {
        $result = $result.Replace($key, $translationDict[$key])
    }
    
    # Clean up common patterns
    $result = $result.Replace(" - ", " - ")
    $result = $result.Replace("（", " (")
    $result = $result.Replace("）", ") ")
    $result = $result.Replace("《", '"')
    $result = $result.Replace("》", '"')
    $result = $result.Replace("【", "[")
    $result = $result.Replace("】", "]")
    $result = $result.Replace("。", ". ")
    $result = $result.Replace("，", ", ")
    $result = $result.Replace("；", "; ")
    $result = $result.Replace("：", ": ")
    $result = $result.Replace("？", "? ")
    $result = $result.Replace("！", "! ")
    $result = $result.Replace("、", ", ")
    
    # Fix double spaces
    while ($result.Contains("  ")) {
        $result = $result.Replace("  ", " ")
    }
    
    # Capitalize first letter if it's a sentence
    if ($result.Length -gt 0 -and [char]::IsLower($result[0])) {
        $result = [char]::ToUpper($result[0]) + $result.Substring(1)
    }
    
    return $result.Trim()
}

function Process-File {
    param([string]$filePath)
    
    if (-not (Test-Path $filePath)) {
        Write-Host "File not found: $filePath" -ForegroundColor Red
        return
    }
    
    try {
        # Read file content
        $content = Get-Content $filePath -Raw -Encoding UTF8
        
        # Process XML documentation comments (///)
        $content = [regex]::Replace($content, '///\s*<summary>([^<]+)</summary>', {
            param($match)
            $innerText = $match.Groups[1].Value.Trim()
            $translated = Translate-Comment $innerText
            return "/// <summary>$translated</summary>"
        }, [System.Text.RegularExpressions.RegexOptions]::Multiline)
        
        # Process single line comments (//)
        $content = [regex]::Replace($content, '//\s*([^\r\n]+)', {
            param($match)
            $comment = $match.Groups[1].Value.Trim()
            $translated = Translate-Comment $comment
            return "// $translated"
        }, [System.Text.RegularExpressions.RegexOptions]::Multiline)
        
        # Process multi-line comments (/* */) - single line
        $content = [regex]::Replace($content, '/\*\s*([^*]+)\s*\*/', {
            param($match)
            $comment = $match.Groups[1].Value.Trim()
            $translated = Translate-Comment $comment
            return "/* $translated */"
        }, [System.Text.RegularExpressions.RegexOptions]::Multiline)
        
        # Backup original file
        $backupPath = "$filePath.backup"
        Copy-Item $filePath $backupPath -Force
        
        # Write processed content
        [System.IO.File]::WriteAllText($filePath, $content, [System.Text.Encoding]::UTF8)
        
        Write-Host "Processed: $filePath (backup saved to $backupPath)" -ForegroundColor Green
    }
    catch {
        Write-Host "Error processing $filePath : $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Process-Directory {
    param([string]$directoryPath)
    
    if (-not (Test-Path $directoryPath)) {
        Write-Host "Directory not found: $directoryPath" -ForegroundColor Red
        return
    }
    
    # Get all C# files
    $csFiles = Get-ChildItem $directoryPath -Filter "*.cs" -Recurse -File
    
    Write-Host "Found $($csFiles.Count) C# files to process" -ForegroundColor Yellow
    
    foreach ($file in $csFiles) {
        Process-File $file.FullName
    }
    
    Write-Host "Processing completed!" -ForegroundColor Green
}

# Main execution
if ($args.Count -eq 0) {
    Write-Host "Usage: .\TranslateComments.ps1 <directory_or_file_path>"
    Write-Host "Example: .\TranslateComments.ps1 C:\MyProject"
    Write-Host "Example: .\TranslateComments.ps1 C:\MyProject\MyFile.cs"
    exit 1
}

$path = $args[0]

if (Test-Path $path -PathType Leaf) {
    Process-File $path
}
elseif (Test-Path $path -PathType Container) {
    Process-Directory $path
}
else {
    Write-Host "Path not found: $path" -ForegroundColor Red
}