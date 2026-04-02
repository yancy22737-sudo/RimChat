$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
$ErrorActionPreference = "Stop"

function Get-GitNexusRoot {
    $candidates = @()

    if ($env:APPDATA) {
        $candidates += (Join-Path $env:APPDATA "npm\node_modules\gitnexus")
    }

    $npm = Get-Command npm -ErrorAction SilentlyContinue
    if ($npm) {
        try {
            $npmRoot = (npm root -g).Trim()
            if ($npmRoot) {
                $candidates += (Join-Path $npmRoot "gitnexus")
            }
        }
        catch {
            # Ignore npm lookup errors and keep fallback candidates.
        }
    }

    foreach ($path in $candidates | Select-Object -Unique) {
        if (Test-Path $path) {
            return $path
        }
    }

    throw "GitNexus installation directory not found."
}

try {
    $gitNexusRoot = Get-GitNexusRoot
    $targetPath = Join-Path $gitNexusRoot "dist\core\ingestion\import-resolvers\csharp.js"

    if (-not (Test-Path $targetPath)) {
        throw "Target file not found: $targetPath"
    }

    $content = Get-Content -Raw -Path $targetPath

    $oldBlock = @"
        if (resolvedFiles.length > 1) {
            const dirSuffix = resolveCSharpNamespaceDir(rawImportPath, csharpConfigs);
            if (dirSuffix) {
                return { kind: 'package', files: resolvedFiles, dirSuffix };
            }
        }
        if (resolvedFiles.length > 0)
            return { kind: 'files', files: resolvedFiles };
"@

    $newBlock = @"
        if (resolvedFiles.length === 1) {
            return { kind: 'files', files: resolvedFiles };
        }
        // Fail fast for namespace-wide fan-out: skip ambiguous multi-file mapping
        // instead of emitting bulk IMPORTS edges that distort dependency graphs.
        if (resolvedFiles.length > 1) {
            return null;
        }
"@

    $patched = $false
    if ($content.Contains($oldBlock)) {
        $content = $content.Replace($oldBlock, $newBlock)
        $patched = $true
    }

    $alreadyPatched = $content.Contains("skip ambiguous multi-file mapping") -and
        $content.Contains("if (resolvedFiles.length === 1)")

    if (-not $patched -and -not $alreadyPatched) {
        throw "Expected C# import resolver block not found. Manual inspection required: $targetPath"
    }

    if ($patched) {
        [System.IO.File]::WriteAllText($targetPath, $content, [System.Text.UTF8Encoding]::new($false))
    }

    $verify = Get-Content -Raw -Path $targetPath
    if (-not $verify.Contains("skip ambiguous multi-file mapping")) {
        throw "Verification failed: precision-import hotfix marker is missing."
    }
    if ($verify.Contains("return { kind: 'package', files: resolvedFiles, dirSuffix };")) {
        throw "Verification failed: namespace package fan-out path still exists."
    }

    if ($patched) {
        Write-Output "Patched successfully: $targetPath"
        Write-Output "Mode: precise C# import edges (multi-file namespace fan-out disabled)"
    }
    else {
        Write-Output "No changes applied: C# import precision hotfix is already present."
    }

    exit 0
}
catch {
    Write-Error $_
    exit 1
}
