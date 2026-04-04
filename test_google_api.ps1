$apiKey = "AIzaSyBg6Jmm4nE6OGxJZtxuzd82XQIip5bDEcg"
$url = "https://generativelanguage.googleapis.com/v1beta/models?key=$apiKey"
try {
    $response = Invoke-WebRequest -Uri $url -Method GET -TimeoutSec 15
    Write-Host "Status: $($response.StatusCode)"
    Write-Host "Content Length: $($response.Content.Length)"
    $content = $response.Content
    if ($content.Length -gt 500) {
        Write-Host "First 500 chars: $($content.Substring(0, 500))"
    } else {
        Write-Host "Content: $content"
    }
} catch {
    Write-Host "Error: $($_.Exception.Message)"
}
