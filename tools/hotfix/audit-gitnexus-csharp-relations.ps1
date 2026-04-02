param(
    [string]$Repo = "RimChat",
    [string]$OutputPath = "",
    [double]$MaxEdgePerUsing = 3.0,
    [int]$MaxProcessZeroSamples = 2,
    [int]$MinGlobalProcessEdges = 2201
)

$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

function Convert-FromCliJson {
    param([string]$Text)
    $trimmed = $Text
    if ($null -eq $trimmed) {
        $trimmed = ""
    }
    $trimmed = $trimmed.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        throw "GitNexus CLI returned empty output."
    }

    $objectStart = $trimmed.IndexOf("{")
    $arrayStart = $trimmed.IndexOf("[")
    $start = -1
    if ($objectStart -ge 0 -and $arrayStart -ge 0) {
        $start = [Math]::Min($objectStart, $arrayStart)
    } elseif ($objectStart -ge 0) {
        $start = $objectStart
    } elseif ($arrayStart -ge 0) {
        $start = $arrayStart
    }

    if ($start -lt 0) {
        throw "GitNexus CLI output does not contain JSON payload: $trimmed"
    }

    $json = $trimmed.Substring($start)
    return $json | ConvertFrom-Json
}

function Invoke-GitNexusCypher {
    param(
        [string]$RepoName,
        [string]$Query
    )

    $safeQuery = $Query
    if ($null -eq $safeQuery) {
        $safeQuery = ""
    }
    $singleLineQuery = ($safeQuery -replace "`r?`n", " ").Trim()
    $raw = (& npx gitnexus cypher -r $RepoName $singleLineQuery 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0) {
        throw "GitNexus cypher failed: $raw"
    }

    $payload = Convert-FromCliJson -Text $raw
    if ($null -ne $payload.error -and -not [string]::IsNullOrWhiteSpace($payload.error)) {
        throw "GitNexus cypher error: $($payload.error)"
    }

    return $payload
}

function Get-CountValue {
    param(
        [string]$RepoName,
        [string]$Query,
        [string]$FieldName
    )

    $payload = Invoke-GitNexusCypher -RepoName $RepoName -Query $Query
    $markdown = $payload.markdown
    if ($null -eq $markdown) {
        $markdown = ""
    }
    if ([string]::IsNullOrWhiteSpace($markdown)) {
        return 0
    }

    $lines = @($markdown -split "`r?`n")
    if ($lines.Count -lt 3) {
        return 0
    }

    $headers = @($lines[0].Trim("|").Split("|") | ForEach-Object { $_.Trim() })
    $values = @($lines[2].Trim("|").Split("|") | ForEach-Object { $_.Trim() })
    $index = [Array]::IndexOf($headers, $FieldName)
    if ($index -lt 0 -or $index -ge $values.Count) {
        return 0
    }

    $number = 0
    if ([int]::TryParse($values[$index], [ref]$number)) {
        return $number
    }

    return 0
}

function Get-NamespaceFromFile {
    param([string]$FilePath)
    $line = Select-String -Path $FilePath -Pattern "^\s*namespace\s+([A-Za-z0-9_.]+)" -Encoding UTF8 | Select-Object -First 1
    if ($null -eq $line) {
        return ""
    }

    return $line.Matches[0].Groups[1].Value
}

function Get-EvidenceHitCount {
    param(
        [string]$FilePath,
        [string]$Pattern
    )

    $hits = Select-String -Path $FilePath -Pattern $Pattern -SimpleMatch -Encoding UTF8
    return @($hits).Count
}

function Escape-CypherString {
    param([string]$Value)
    $safe = $Value
    if ($null -eq $safe) {
        $safe = ""
    }

    return $safe.Replace("\", "\\").Replace("'", "\'")
}

function New-Sample {
    param(
        [string]$Id,
        [string]$CallerFile,
        [string]$CallerSymbol,
        [string]$CalleeFile,
        [string]$CalleeSymbol,
        [string]$EvidencePattern,
        [bool]$ExpectImportEdge
    )

    return [PSCustomObject]@{
        Id = $Id
        CallerFile = $CallerFile
        CallerSymbol = $CallerSymbol
        CalleeFile = $CalleeFile
        CalleeSymbol = $CalleeSymbol
        EvidencePattern = $EvidencePattern
        ExpectImportEdge = $ExpectImportEdge
    }
}

function Evaluate-Sample {
    param(
        [string]$RepoName,
        [string]$RepoRoot,
        [pscustomobject]$Sample
    )

    $callerPath = Join-Path $RepoRoot $Sample.CallerFile
    $calleePath = Join-Path $RepoRoot $Sample.CalleeFile
    $callerNs = Get-NamespaceFromFile -FilePath $callerPath
    $calleeNs = Get-NamespaceFromFile -FilePath $calleePath
    $sameNamespace = $callerNs -ne "" -and $callerNs -eq $calleeNs
    $evidenceHits = Get-EvidenceHitCount -FilePath $callerPath -Pattern $Sample.EvidencePattern

    $callerFileEscaped = Escape-CypherString -Value $Sample.CallerFile
    $calleeFileEscaped = Escape-CypherString -Value $Sample.CalleeFile
    $callerSymbolEscaped = Escape-CypherString -Value $Sample.CallerSymbol
    $calleeSymbolEscaped = Escape-CypherString -Value $Sample.CalleeSymbol

    $callsCount = Get-CountValue -RepoName $RepoName -FieldName "edgeCount" -Query @"
MATCH (c)-[r:CodeRelation {type:'CALLS'}]->(t)
WHERE c.filePath CONTAINS '$callerFileEscaped'
  AND c.name = '$callerSymbolEscaped'
  AND t.filePath CONTAINS '$calleeFileEscaped'
  AND t.name = '$calleeSymbolEscaped'
RETURN count(r) AS edgeCount
"@

    $importsCount = Get-CountValue -RepoName $RepoName -FieldName "edgeCount" -Query @"
MATCH (f:File)-[r:CodeRelation {type:'IMPORTS'}]->(t:File)
WHERE f.filePath CONTAINS '$callerFileEscaped'
  AND t.filePath CONTAINS '$calleeFileEscaped'
RETURN count(r) AS edgeCount
"@

    $processEdges = Get-CountValue -RepoName $RepoName -FieldName "stepEdges" -Query @"
MATCH (s)-[r:CodeRelation {type:'STEP_IN_PROCESS'}]->(p:Process)
WHERE s.filePath CONTAINS '$callerFileEscaped'
  AND s.name = '$callerSymbolEscaped'
RETURN count(r) AS stepEdges
"@

    $verdict = "OK"
    if ($evidenceHits -eq 0) {
        $verdict = "SOURCE_EVIDENCE_MISSING"
    } elseif ($callsCount -eq 0) {
        $verdict = "CALL_EDGE_MISSING"
    } elseif ($Sample.ExpectImportEdge -and $importsCount -eq 0) {
        $verdict = "IMPORT_EDGE_MISSING"
    } elseif (-not $Sample.ExpectImportEdge -and $sameNamespace -and $importsCount -eq 0) {
        $verdict = "SAME_NAMESPACE_NO_IMPORT_EDGE"
    }

    return [PSCustomObject]@{
        Id = $Sample.Id
        CallerFile = $Sample.CallerFile
        CallerSymbol = $Sample.CallerSymbol
        CalleeFile = $Sample.CalleeFile
        CalleeSymbol = $Sample.CalleeSymbol
        CallerNamespace = $callerNs
        CalleeNamespace = $calleeNs
        SameNamespace = $sameNamespace
        EvidenceHits = $evidenceHits
        CallsEdges = $callsCount
        ImportsEdges = $importsCount
        ProcessEdges = $processEdges
        Verdict = $verdict
    }
}

function Get-ImportOverlinkSummary {
    param(
        [string]$RepoName,
        [string]$RepoRoot,
        [string]$CallerFile
    )

    $callerPath = Join-Path $RepoRoot $CallerFile
    $usingCount = @(Select-String -Path $callerPath -Pattern "^\s*using\s+RimChat\." -Encoding UTF8).Count
    $fileEscaped = Escape-CypherString -Value $CallerFile
    $importEdges = Get-CountValue -RepoName $RepoName -FieldName "edgeCount" -Query @"
MATCH (f:File)-[r:CodeRelation {type:'IMPORTS'}]->(:File)
WHERE f.filePath CONTAINS '$fileEscaped'
RETURN count(r) AS edgeCount
"@

    $ratio = 0.0
    if ($usingCount -gt 0) {
        $ratio = [Math]::Round($importEdges / [double]$usingCount, 2)
    }

    return [PSCustomObject]@{
        CallerFile = $CallerFile
        RimChatUsingCount = $usingCount
        ImportEdges = $importEdges
        EdgePerUsingRatio = $ratio
        SuspectOverlink = ($usingCount -gt 0 -and $importEdges -ge ($usingCount * 8))
    }
}

function Get-StrictGateResult {
    param(
        [array]$Rows,
        [array]$OverlinkRows,
        [int]$GlobalProcessEdges,
        [double]$MaxEdgePerUsingAllowed,
        [int]$MaxProcessZeroAllowed,
        [int]$MinGlobalProcessEdgesRequired
    )

    $callsMissing = @($Rows | Where-Object { $_.CallsEdges -eq 0 }).Count
    $processZero = @($Rows | Where-Object { $_.ProcessEdges -eq 0 }).Count
    $socialRow = @($OverlinkRows | Where-Object { $_.CallerFile -eq "RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs" } | Select-Object -First 1)
    $socialRatio = if ($socialRow) { [double]$socialRow.EdgePerUsingRatio } else { 0.0 }

    $checks = @(
        [PSCustomObject]@{
            Name = "CALLS sample coverage"
            Passed = ($callsMissing -eq 0)
            Detail = "missing=$callsMissing (expected 0)"
        },
        [PSCustomObject]@{
            Name = "SocialNewsPromptBuilder Edge/using"
            Passed = ($socialRatio -le $MaxEdgePerUsingAllowed)
            Detail = "value=$socialRatio (threshold<=$MaxEdgePerUsingAllowed)"
        },
        [PSCustomObject]@{
            Name = "Process zero sample count"
            Passed = ($processZero -le $MaxProcessZeroAllowed)
            Detail = "value=$processZero (threshold<=$MaxProcessZeroAllowed)"
        },
        [PSCustomObject]@{
            Name = "Global STEP_IN_PROCESS edges"
            Passed = ($GlobalProcessEdges -ge $MinGlobalProcessEdgesRequired)
            Detail = "value=$GlobalProcessEdges (threshold>=$MinGlobalProcessEdgesRequired)"
        }
    )

    $failed = @($checks | Where-Object { -not $_.Passed })
    return [PSCustomObject]@{
        Passed = ($failed.Count -eq 0)
        Checks = $checks
    }
}

function Write-MarkdownReport {
    param(
        [string]$OutputFile,
        [string]$RepoName,
        [datetime]$RunAt,
        [array]$Rows,
        [array]$OverlinkRows,
        [int]$GlobalProcessEdges,
        [pscustomobject]$StrictGate
    )

    $okCalls = @($Rows | Where-Object { $_.CallsEdges -gt 0 }).Count
    $missingCalls = @($Rows | Where-Object { $_.Verdict -eq "CALL_EDGE_MISSING" }).Count
    $missingImports = @($Rows | Where-Object { $_.Verdict -eq "IMPORT_EDGE_MISSING" }).Count
    $sameNsNoImport = @($Rows | Where-Object { $_.Verdict -eq "SAME_NAMESPACE_NO_IMPORT_EDGE" }).Count
    $processZero = @($Rows | Where-Object { $_.ProcessEdges -eq 0 }).Count
    $sourceMissing = @($Rows | Where-Object { $_.Verdict -eq "SOURCE_EVIDENCE_MISSING" }).Count

    $content = @()
    $content += "# GitNexus C# Relation Audit"
    $content += ""
    $content += "- Repository: $RepoName"
    $content += "- RunAt: $($RunAt.ToString("yyyy-MM-dd HH:mm:ss"))"
    $content += "- Samples: $($Rows.Count)"
    $content += ""
    $content += "## Summary"
    $content += ""
    $content += "- CALLS edges found: $okCalls / $($Rows.Count)"
    $content += "- CALLS missing: $missingCalls"
    $content += "- Expected IMPORTS missing: $missingImports"
    $content += "- Same-namespace no-import edges: $sameNsNoImport"
    $content += "- Process zero coverage: $processZero"
    $content += "- Global STEP_IN_PROCESS edges: $GlobalProcessEdges"
    $content += "- Source evidence missing: $sourceMissing"
    $content += "- Strict gate: $(if ($StrictGate.Passed) { 'PASS' } else { 'FAIL' })"
    $content += ""
    $content += "## Strict Gate Checks"
    $content += ""
    foreach ($check in $StrictGate.Checks) {
        $content += "- $(if ($check.Passed) { '[PASS]' } else { '[FAIL]' }) $($check.Name): $($check.Detail)"
    }
    $content += ""
    $content += "## Sample Matrix"
    $content += ""
    $content += "| Id | Caller | Callee | EvidenceHits | CALLS | IMPORTS | PROCESS | Verdict |"
    $content += "| --- | --- | --- | --- | --- | --- | --- | --- |"
    foreach ($row in $Rows) {
        $caller = "$($row.CallerFile)::$($row.CallerSymbol)"
        $callee = "$($row.CalleeFile)::$($row.CalleeSymbol)"
        $content += "| $($row.Id) | $caller | $callee | $($row.EvidenceHits) | $($row.CallsEdges) | $($row.ImportsEdges) | $($row.ProcessEdges) | $($row.Verdict) |"
    }

    $content += ""
    $content += "## Import Overlink Check"
    $content += ""
    $content += "| File | RimChat using count | IMPORT edges | Edge/using | Suspect overlink |"
    $content += "| --- | --- | --- | --- | --- |"
    foreach ($row in $OverlinkRows) {
        $content += "| $($row.CallerFile) | $($row.RimChatUsingCount) | $($row.ImportEdges) | $($row.EdgePerUsingRatio) | $($row.SuspectOverlink) |"
    }

    $content += ""
    $content += "## Rule Of Thumb"
    $content += ""
    $content += "- Treat `CALLS` as primary truth for C# relation checks."
    $content += "- Treat `IMPORTS` as auxiliary only; same-namespace direct references often have no import edge."
    $content += "- If `IMPORTS` is high relative to `using RimChat.*`, namespace-wide overlink is likely."
    $content += "- If a symbol has `PROCESS=0`, do not infer that the call chain is absent."

    [System.IO.File]::WriteAllLines($OutputFile, $content, [System.Text.UTF8Encoding]::new($false))
}

try {
    $repoRoot = Get-RepoRoot
    $reportDir = Join-Path $repoRoot "doc\reports"
    if (-not (Test-Path $reportDir)) {
        New-Item -ItemType Directory -Path $reportDir | Out-Null
    }

    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $name = "gitnexus-csharp-relation-audit-" + (Get-Date).ToString("yyyyMMdd-HHmmss") + ".md"
        $OutputPath = Join-Path $reportDir $name
    }

    $samples = @(
        (New-Sample -Id "S1" -CallerFile "RimChat/DiplomacySystem/ItemAirdropPaymentResolver.cs" -CallerSymbol "Resolve" -CalleeFile "RimChat/DiplomacySystem/ThingDefResolver.cs" -CalleeSymbol "BuildMatchRequest" -EvidencePattern "ThingDefResolver.BuildMatchRequest(" -ExpectImportEdge $false),
        (New-Sample -Id "S2" -CallerFile "RimChat/DiplomacySystem/ItemAirdropPaymentResolver.cs" -CallerSymbol "Resolve" -CalleeFile "RimChat/DiplomacySystem/ThingDefMatchEngine.cs" -CalleeSymbol "ResolveSingle" -EvidencePattern "ThingDefMatchEngine.ResolveSingle(" -ExpectImportEdge $false),
        (New-Sample -Id "S3" -CallerFile "RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs" -CallerSymbol "BuildVariables" -CalleeFile "RimChat/DiplomacySystem/Social/SocialCircleService.cs" -CalleeSymbol "GetCategoryLabel" -EvidencePattern "SocialCircleService.GetCategoryLabel(" -ExpectImportEdge $false),
        (New-Sample -Id "S4" -CallerFile "RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs" -CallerSymbol "BuildPromptInputPayload" -CalleeFile "RimChat/DiplomacySystem/Social/SocialCircleService.cs" -CalleeSymbol "ResolveDisplayLabel" -EvidencePattern "SocialCircleService.ResolveDisplayLabel(" -ExpectImportEdge $false),
        (New-Sample -Id "S5" -CallerFile "RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs" -CallerSymbol "BuildMessages" -CalleeFile "RimChat/Persistence/DialogueScenarioContext.cs" -CalleeSymbol "CreateDiplomacy" -EvidencePattern "DialogueScenarioContext.CreateDiplomacy(" -ExpectImportEdge $true),
        (New-Sample -Id "S6" -CallerFile "RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs" -CallerSymbol "BuildMessages" -CalleeFile "RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs" -CalleeSymbol "BuildUnifiedChannelSystemPrompt" -EvidencePattern "BuildUnifiedChannelSystemPrompt(" -ExpectImportEdge $true),
        (New-Sample -Id "S7" -CallerFile "RimChat/Prompting/ScribanPromptEngine.cs" -CallerSymbol "RenderOrThrow" -CalleeFile "RimChat/Prompting/PromptTemplateBlockRegistry.cs" -CalleeSymbol "TryGetReason" -EvidencePattern "PromptTemplateBlockRegistry.TryGetReason(" -ExpectImportEdge $false),
        (New-Sample -Id "S8" -CallerFile "RimChat/DiplomacySystem/ThingDefResolver.cs" -CallerSymbol "BuildMatchRequest" -CalleeFile "RimChat/DiplomacySystem/ThingDefMatchEngine.cs" -CalleeSymbol "ExtractSemanticTokens" -EvidencePattern "ThingDefMatchEngine.ExtractSemanticTokens(" -ExpectImportEdge $false)
    )

    $rows = @()
    foreach ($sample in $samples) {
        $rows += Evaluate-Sample -RepoName $Repo -RepoRoot $repoRoot -Sample $sample
    }

    $overlinkRows = @(
        (Get-ImportOverlinkSummary -RepoName $Repo -RepoRoot $repoRoot -CallerFile "RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs"),
        (Get-ImportOverlinkSummary -RepoName $Repo -RepoRoot $repoRoot -CallerFile "RimChat/DiplomacySystem/ItemAirdropPaymentResolver.cs")
    )

    $globalProcessEdges = Get-CountValue -RepoName $Repo -FieldName "stepEdges" -Query @"
MATCH (s)-[r:CodeRelation {type:'STEP_IN_PROCESS'}]->(p:Process)
RETURN count(r) AS stepEdges
"@

    $strictGate = Get-StrictGateResult `
        -Rows $rows `
        -OverlinkRows $overlinkRows `
        -GlobalProcessEdges $globalProcessEdges `
        -MaxEdgePerUsingAllowed $MaxEdgePerUsing `
        -MaxProcessZeroAllowed $MaxProcessZeroSamples `
        -MinGlobalProcessEdgesRequired $MinGlobalProcessEdges

    Write-MarkdownReport `
        -OutputFile $OutputPath `
        -RepoName $Repo `
        -RunAt (Get-Date) `
        -Rows $rows `
        -OverlinkRows $overlinkRows `
        -GlobalProcessEdges $globalProcessEdges `
        -StrictGate $strictGate

    Write-Output "Audit completed: $OutputPath"
    if ($strictGate.Passed) {
        Write-Output "Strict gate: PASS"
        exit 0
    }

    Write-Error "Strict gate: FAIL"
    foreach ($check in $strictGate.Checks) {
        if (-not $check.Passed) {
            Write-Error "Failed check: $($check.Name) -> $($check.Detail)"
        }
    }
    exit 2
}
catch {
    Write-Error $_
    exit 1
}
