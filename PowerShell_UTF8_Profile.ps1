# PowerShell Profile for UTF-8 Encoding
# This ensures PowerShell uses UTF-8 encoding as required by project rules
# Save this file to: C:\Users\Administrator\Documents\WindowsPowerShell\Microsoft.PowerShell_profile.ps1

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