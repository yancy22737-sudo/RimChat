# PowerShell UTF-8 编码永久设置指南

## 概述
根据项目规则要求"确保power shell使用UTF-8编码格式"，本指南将帮助您永久设置PowerShell使用UTF-8编码。

## 当前编码状态检查
在开始设置前，请先检查当前编码状态：
```powershell
# 检查当前编码设置
[Console]::OutputEncoding.EncodingName
[Console]::InputEncoding.EncodingName
$OutputEncoding.EncodingName
chcp
```

## 手动创建配置文件步骤

### 步骤1：创建配置文件目录
1. 打开文件资源管理器
2. 导航到：`C:\Users\Administrator\Documents\`
3. 创建新文件夹：`WindowsPowerShell`

### 步骤2：创建配置文件
1. 在 `WindowsPowerShell` 文件夹中创建新文件
2. 文件名：`Microsoft.PowerShell_profile.ps1`
3. 文件内容（复制以下代码）：

```powershell
# PowerShell Profile for UTF-8 Encoding
# This ensures PowerShell uses UTF-8 encoding as required by project rules

# Set console code page to UTF-8
chcp 65001 | Out-Null

# Set PowerShell encoding to UTF-8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "PowerShell UTF-8 encoding configured successfully." -ForegroundColor Green
Write-Host "Current encoding settings:" -ForegroundColor Cyan
Write-Host "  Output Encoding: $([Console]::OutputEncoding.EncodingName)" -ForegroundColor Gray
Write-Host "  Input Encoding: $([Console]::InputEncoding.EncodingName)" -ForegroundColor Gray
Write-Host "  Pipe Encoding: $($OutputEncoding.EncodingName)" -ForegroundColor Gray
Write-Host "  Code Page: $(chcp)" -ForegroundColor Gray
```

### 步骤3：使用PowerShell命令创建（推荐）
或者，您可以直接在PowerShell中运行以下命令：

```powershell
# 创建目录（如果不存在）
New-Item -ItemType Directory -Force -Path "C:\Users\Administrator\Documents\WindowsPowerShell"

# 创建配置文件
@'
# PowerShell Profile for UTF-8 Encoding
chcp 65001 | Out-Null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
Write-Host "PowerShell UTF-8 encoding configured." -ForegroundColor Green
'@ | Out-File -FilePath "C:\Users\Administrator\Documents\WindowsPowerShell\Microsoft.PowerShell_profile.ps1" -Encoding UTF8
```

## 验证设置

### 方法1：重启PowerShell
1. 关闭所有PowerShell窗口
2. 重新打开PowerShell
3. 应该看到绿色提示："PowerShell UTF-8 encoding configured successfully."

### 方法2：手动验证
```powershell
# 验证编码设置
[Console]::OutputEncoding.EncodingName  # 应该显示 "Unicode (UTF-8)"
[Console]::InputEncoding.EncodingName   # 应该显示 "Unicode (UTF-8)"
$OutputEncoding.EncodingName            # 应该显示 "Unicode (UTF-8)"
chcp                                     # 应该显示 "活动代码页: 65001"
```

### 方法3：测试中文显示
```powershell
# 测试中文文本显示
Write-Host "UTF-8编码测试：中文显示正常" -ForegroundColor Yellow
```

## 故障排除

### 问题1：配置文件未生效
**症状**：重启PowerShell后没有看到绿色提示
**解决方案**：
1. 检查文件路径是否正确：`C:\Users\Administrator\Documents\WindowsPowerShell\Microsoft.PowerShell_profile.ps1`
2. 检查文件扩展名是否为 `.ps1`
3. 运行：`Test-Path $PROFILE` 确认文件存在

### 问题2：执行策略阻止
**症状**：看到执行策略错误
**解决方案**：
```powershell
# 临时允许脚本执行
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

# 或永久允许
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope LocalMachine
```

### 问题3：编码仍然不正确
**症状**：设置后编码仍然显示GB2312
**解决方案**：
1. 确保使用了正确的UTF-8编码保存文件
2. 尝试在配置文件中添加以下代码：
```powershell
# 强制设置编码
$PSDefaultParameterValues['Out-File:Encoding'] = 'utf8'
$PSDefaultParameterValues['*:Encoding'] = 'utf8'
```

## 临时解决方案（无需配置文件）
如果不想创建永久配置文件，可以在每次启动PowerShell时运行：

```powershell
# 临时设置UTF-8编码
chcp 65001
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
```

## 项目文件说明
- `PowerShell_UTF8_Profile.ps1`：配置文件模板，可直接使用
- 本指南文件：`PowerShell_UTF8_Setup_Guide.md`

## 完成验证
成功设置后，每次启动PowerShell都会自动配置UTF-8编码，确保符合项目开发规范。