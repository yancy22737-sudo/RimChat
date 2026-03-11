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
        } catch {
            # Ignore npm lookup errors and continue with other candidates.
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
    $targetPath = Join-Path $gitNexusRoot "dist\core\ingestion\tree-sitter-queries.js"

    if (-not (Test-Path $targetPath)) {
        throw "Target file not found: $targetPath"
    }

    $content = Get-Content -Raw -Path $targetPath

    $oldLine1 = "  (base_list (simple_base_type (identifier) @heritage.extends))) @heritage"
    $newLine1 = "  (base_list (type (identifier) @heritage.extends))) @heritage"
    $oldLine2 = "  (base_list (simple_base_type (generic_name (identifier) @heritage.extends)))) @heritage"
    $newLine2 = "  (base_list (type (generic_name (identifier) @heritage.extends)))) @heritage"

    $replaced = 0
    if ($content.Contains($oldLine1)) {
        $content = $content.Replace($oldLine1, $newLine1)
        $replaced++
    }
    if ($content.Contains($oldLine2)) {
        $content = $content.Replace($oldLine2, $newLine2)
        $replaced++
    }

    if ($replaced -eq 0) {
        $alreadyPatched = $content.Contains($newLine1) -and $content.Contains($newLine2)
        if ($alreadyPatched) {
            Write-Output "No changes applied: GitNexus C# query hotfix is already present."
            exit 0
        }
        throw "Expected C# heritage query lines were not found. Manual inspection required: $targetPath"
    }

    if ($replaced -ne 2) {
        throw "Partial patch detected ($replaced/2). Aborting to avoid inconsistent state."
    }

    [System.IO.File]::WriteAllText($targetPath, $content, [System.Text.UTF8Encoding]::new($false))

    $verify = Get-Content -Raw -Path $targetPath
    if ($verify.Contains("simple_base_type")) {
        throw "Verification failed: 'simple_base_type' still exists in $targetPath"
    }

    Write-Output "Patched successfully: $targetPath"
    Write-Output "Replacements applied: 2"
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
