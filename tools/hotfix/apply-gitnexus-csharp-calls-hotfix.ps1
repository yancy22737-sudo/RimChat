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

function Add-LineAfterIfMissing {
    param(
        [string]$Content,
        [string]$AnchorLine,
        [string]$LineToAdd
    )

    if ($Content.Contains($LineToAdd)) {
        return @{ Content = $Content; Changed = $false; Already = $true }
    }

    if (-not $Content.Contains($AnchorLine)) {
        throw "Anchor line not found: $AnchorLine"
    }

    $newContent = $Content.Replace($AnchorLine, $AnchorLine + "`r`n" + $LineToAdd)
    return @{ Content = $newContent; Changed = $true; Already = $false }
}

try {
    $gitNexusRoot = Get-GitNexusRoot
    $targetPath = Join-Path $gitNexusRoot "dist\core\ingestion\tree-sitter-queries.js"

    if (-not (Test-Path $targetPath)) {
        throw "Target file not found: $targetPath"
    }

    $content = Get-Content -Raw -Path $targetPath
    $changedCount = 0

    $patches = @(
        @{
            Anchor = "(invocation_expression function: (identifier) @call.name) @call"
            NewLine = "(invocation_expression function: (generic_name (identifier) @call.name)) @call"
        },
        @{
            Anchor = "(invocation_expression function: (member_access_expression name: (identifier) @call.name)) @call"
            NewLine = "(invocation_expression function: (member_access_expression name: (generic_name (identifier) @call.name))) @call"
        },
        @{
            Anchor = "(object_creation_expression type: (identifier) @call.name) @call"
            NewLine = "(object_creation_expression type: (generic_name (identifier) @call.name)) @call"
        },
        @{
            Anchor = "(variable_declaration type: (identifier) @call.name (variable_declarator (implicit_object_creation_expression) @call))"
            NewLine = "(variable_declaration type: (generic_name (identifier) @call.name) (variable_declarator (implicit_object_creation_expression) @call))"
        }
    )

    foreach ($patch in $patches) {
        $result = Add-LineAfterIfMissing -Content $content -AnchorLine $patch.Anchor -LineToAdd $patch.NewLine
        $content = $result.Content
        if ($result.Changed) {
            $changedCount++
        }
    }

    if ($changedCount -eq 0) {
        Write-Output "No changes applied: C# call extraction hotfix is already present."
        exit 0
    }

    [System.IO.File]::WriteAllText($targetPath, $content, [System.Text.UTF8Encoding]::new($false))

    $verify = Get-Content -Raw -Path $targetPath
    foreach ($patch in $patches) {
        if (-not $verify.Contains($patch.NewLine)) {
            throw "Verification failed: missing line -> $($patch.NewLine)"
        }
    }

    Write-Output "Patched successfully: $targetPath"
    Write-Output "Inserted call-query lines: $changedCount"
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
