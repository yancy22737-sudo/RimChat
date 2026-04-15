Get-ChildItem 'C:\Users\Administrator' -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Length -gt 100MB } |
    Sort-Object Length -Descending |
    Select-Object -First 50 FullName, @{N='SizeMB';E={[math]::Round($_.Length/1MB,2)}} |
    ForEach-Object { "{0,-80} {1,10} MB" -f $_.FullName, $_.SizeMB }
