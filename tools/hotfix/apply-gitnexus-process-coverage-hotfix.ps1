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

function Replace-OrThrow {
    param(
        [string]$Content,
        [string]$OldText,
        [string]$NewText,
        [string]$Label
    )

    if ($Content.Contains($NewText)) {
        return @{ Content = $Content; Changed = $false; Already = $true }
    }

    if (-not $Content.Contains($OldText)) {
        throw "Patch anchor not found for $Label."
    }

    return @{
        Content = $Content.Replace($OldText, $NewText)
        Changed = $true
        Already = $false
    }
}

function Replace-Any {
    param(
        [string]$Content,
        [string[]]$Candidates,
        [string]$NewText,
        [string]$Label
    )

    if ($Content.Contains($NewText)) {
        return @{ Content = $Content; Changed = $false }
    }

    foreach ($candidate in $Candidates) {
        if ($Content.Contains($candidate)) {
            return @{
                Content = $Content.Replace($candidate, $NewText)
                Changed = $true
            }
        }
    }

    throw "Patch anchor not found for $Label."
}

try {
    $gitNexusRoot = Get-GitNexusRoot
    $processorPath = Join-Path $gitNexusRoot "dist\core\ingestion\process-processor.js"
    $pipelinePath = Join-Path $gitNexusRoot "dist\core\ingestion\pipeline.js"

    if (-not (Test-Path $processorPath)) {
        throw "Target file not found: $processorPath"
    }
    if (-not (Test-Path $pipelinePath)) {
        throw "Target file not found: $pipelinePath"
    }

    $processor = Get-Content -Raw -Path $processorPath
    $pipeline = Get-Content -Raw -Path $pipelinePath

    $changes = 0

    $r1 = Replace-OrThrow -Content $processor `
        -OldText "maxTraceDepth: 10," `
        -NewText "maxTraceDepth: 14," `
        -Label "process-processor maxTraceDepth"
    $processor = $r1.Content
    if ($r1.Changed) { $changes++ }

    $r2 = Replace-OrThrow -Content $processor `
        -OldText "maxBranching: 4," `
        -NewText "maxBranching: 6," `
        -Label "process-processor maxBranching"
    $processor = $r2.Content
    if ($r2.Changed) { $changes++ }

    $r3 = Replace-OrThrow -Content $processor `
        -OldText "minSteps: 3, // 3+ steps = genuine multi-hop flow (2-step is just ""A calls B"")" `
        -NewText "minSteps: 2, // 2+ steps = include short but meaningful execution flows" `
        -Label "process-processor minSteps"
    $processor = $r3.Content
    if ($r3.Changed) { $changes++ }

    $r4 = Replace-Any -Content $processor `
        -Candidates @(
            ".slice(0, 200) // Limit to prevent explosion",
            ".slice(0, 500) // Expanded entry-point cap for broader process coverage",
            ".slice(0, 2000) // Expanded entry-point cap for broader process coverage"
        ) `
        -NewText ".slice(0, 5000) // Expanded entry-point cap for broader process coverage" `
        -Label "process-processor entry-point cap"
    $processor = $r4.Content
    if ($r4.Changed) { $changes++ }

    $entryBlockOldPositive = @"
        if (score > 0) {
            entryPointCandidates.push({ id: node.id, score, reasons });
        }
"@
    $entryBlockOldNonNegative = @"
        if (score >= 0) {
            entryPointCandidates.push({ id: node.id, score, reasons });
        }
"@
    $entryBlockNew = "        entryPointCandidates.push({ id: node.id, score, reasons });"
    if ($processor.Contains($entryBlockOldPositive)) {
        $processor = $processor.Replace($entryBlockOldPositive, $entryBlockNew)
        $changes++
    }
    elseif ($processor.Contains($entryBlockOldNonNegative)) {
        $processor = $processor.Replace($entryBlockOldNonNegative, $entryBlockNew)
        $changes++
    }

    $oldPipelineSnippet = @"
    }, { maxProcesses: dynamicMaxProcesses, minSteps: 3 });
"@
    $newPipelineSnippet = @"
    }, { maxProcesses: dynamicMaxProcesses, minSteps: 2 });
"@

    $r5 = Replace-OrThrow -Content $pipeline `
        -OldText $oldPipelineSnippet `
        -NewText $newPipelineSnippet `
        -Label "pipeline process minSteps override"
    $pipeline = $r5.Content
    if ($r5.Changed) { $changes++ }

    $r6 = Replace-Any -Content $pipeline `
        -Candidates @(
            "const dynamicMaxProcesses = Math.max(20, Math.min(300, Math.round(symbolCount / 10)));",
            "const dynamicMaxProcesses = Math.max(20, Math.min(500, Math.round(symbolCount / 10)));",
            "const dynamicMaxProcesses = Math.max(20, Math.min(700, Math.round(symbolCount / 10)));",
            "const dynamicMaxProcesses = Math.max(20, Math.min(1200, Math.round(symbolCount / 10)));"
        ) `
        -NewText "const dynamicMaxProcesses = Math.max(20, Math.min(2000, Math.round(symbolCount / 10)));" `
        -Label "pipeline dynamic maxProcesses cap"
    $pipeline = $r6.Content
    if ($r6.Changed) { $changes++ }

    $r7 = Replace-Any -Content $processor `
        -Candidates @(
            "while (queue.length > 0 && traces.length < config.maxBranching * 3) {",
            "while (queue.length > 0 && traces.length < config.maxBranching * 10) {"
        ) `
        -NewText "while (queue.length > 0 && traces.length < config.maxBranching * 20) {" `
        -Label "process-processor trace budget"
    $processor = $r7.Content
    if ($r7.Changed) { $changes++ }

    $anchor = @"
    processResult.steps.forEach((step) => {
"@
    $canonicalAugment = @'
    // process-neighbor-augmentation:
    // Expand process coverage to direct/nearby CALLS neighbors of stepped nodes.
    {
        const stepKeySet = new Set(processResult.steps.map((s) => `${s.nodeId}::${s.processId}`));
        const nodeProcesses = new Map();
        const nodeMinStep = new Map();
        for (const s of processResult.steps) {
            let procSet = nodeProcesses.get(s.nodeId);
            if (!procSet) {
                procSet = new Set();
                nodeProcesses.set(s.nodeId, procSet);
            }
            procSet.add(s.processId);
            const key = `${s.nodeId}::${s.processId}`;
            const prev = nodeMinStep.get(key);
            if (prev === undefined || s.step < prev) {
                nodeMinStep.set(key, s.step);
            }
        }
        for (let pass = 0; pass < 2; pass++) {
            const staged = [];
            for (const rel of graph.iterRelationships()) {
                if (rel.type !== 'CALLS' || rel.confidence < 0.5) {
                    continue;
                }
                const srcProcs = nodeProcesses.get(rel.sourceId);
                const dstProcs = nodeProcesses.get(rel.targetId);
                if (srcProcs && !dstProcs) {
                    for (const procId of srcProcs) {
                        const key = `${rel.targetId}::${procId}`;
                        if (stepKeySet.has(key)) {
                            continue;
                        }
                        const srcStepKey = `${rel.sourceId}::${procId}`;
                        const srcStep = nodeMinStep.get(srcStepKey) ?? 1;
                        const newStep = Math.min(srcStep + 1, 9999);
                        staged.push({ nodeId: rel.targetId, processId: procId, step: newStep });
                    }
                }
                if (dstProcs && !srcProcs) {
                    for (const procId of dstProcs) {
                        const key = `${rel.sourceId}::${procId}`;
                        if (stepKeySet.has(key)) {
                            continue;
                        }
                        const dstStepKey = `${rel.targetId}::${procId}`;
                        const dstStep = nodeMinStep.get(dstStepKey) ?? 2;
                        const newStep = Math.max(1, dstStep - 1);
                        staged.push({ nodeId: rel.sourceId, processId: procId, step: newStep });
                    }
                }
            }
            if (staged.length === 0) {
                break;
            }
            for (const s of staged) {
                const key = `${s.nodeId}::${s.processId}`;
                if (stepKeySet.has(key)) {
                    continue;
                }
                stepKeySet.add(key);
                processResult.steps.push(s);
                let procSet = nodeProcesses.get(s.nodeId);
                if (!procSet) {
                    procSet = new Set();
                    nodeProcesses.set(s.nodeId, procSet);
                }
                procSet.add(s.processId);
                const prev = nodeMinStep.get(key);
                if (prev === undefined || s.step < prev) {
                    nodeMinStep.set(key, s.step);
                }
            }
        }
    }
    processResult.steps.forEach((step) => {
'@
    if ($pipeline.Contains("process-neighbor-augmentation")) {
        $pattern = [regex]"(?s)\s{4}// process-neighbor-augmentation:.*?\s{4}processResult\.steps\.forEach\(\(step\) => \{"
        if ($pattern.IsMatch($pipeline)) {
            $pipeline = $pattern.Replace($pipeline, [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $canonicalAugment }, 1)
            $changes++
        }
        else {
            throw "Found process-neighbor marker but failed to normalize augmentation block."
        }
    }
    else {
        $insert = $canonicalAugment
        $r8 = Replace-OrThrow -Content $pipeline -OldText $anchor -NewText $insert -Label "pipeline process-neighbor-augmentation"
        $pipeline = $r8.Content
        if ($r8.Changed) { $changes++ }
    }

    if ($changes -gt 0) {
        [System.IO.File]::WriteAllText($processorPath, $processor, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($pipelinePath, $pipeline, [System.Text.UTF8Encoding]::new($false))
    }

    $verifyProcessor = Get-Content -Raw -Path $processorPath
    $verifyPipeline = Get-Content -Raw -Path $pipelinePath

    if (-not $verifyProcessor.Contains("maxTraceDepth: 14,")) { throw "Verification failed: maxTraceDepth not patched." }
    if (-not $verifyProcessor.Contains("maxBranching: 6,")) { throw "Verification failed: maxBranching not patched." }
    if (-not $verifyProcessor.Contains("minSteps: 2")) { throw "Verification failed: process default minSteps not patched." }
    if (-not $verifyProcessor.Contains(".slice(0, 5000)")) { throw "Verification failed: entry-point cap not patched." }
    if (-not $verifyProcessor.Contains("entryPointCandidates.push({ id: node.id, score, reasons });")) { throw "Verification failed: entry-point score gate not patched." }
    if (-not $verifyPipeline.Contains("maxProcesses: dynamicMaxProcesses, minSteps: 2")) { throw "Verification failed: pipeline minSteps override not patched." }
    if (-not $verifyPipeline.Contains("Math.min(2000, Math.round(symbolCount / 10))")) { throw "Verification failed: pipeline maxProcesses cap not patched." }
    if (-not $verifyPipeline.Contains("process-neighbor-augmentation")) { throw "Verification failed: pipeline process-neighbor augmentation not patched." }
    if (-not $verifyProcessor.Contains("traces.length < config.maxBranching * 20")) { throw "Verification failed: trace budget not patched." }

    if ($changes -gt 0) {
        Write-Output "Patched successfully:"
        Write-Output "- $processorPath"
        Write-Output "- $pipelinePath"
        Write-Output "Mode: process coverage boost (minSteps=2, depth=14, branching=6, entry cap=5000, dynamic maxProcesses=2000, traceBudget=branching*20, scoreGate=all, +process-neighbor-augmentation)"
    }
    else {
        Write-Output "No changes applied: process coverage hotfix is already present."
    }

    exit 0
}
catch {
    Write-Error $_
    exit 1
}
