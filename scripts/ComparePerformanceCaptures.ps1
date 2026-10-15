param(
    [Parameter(Mandatory = $true)][string[]]$BaselineLog,
    [Parameter(Mandatory = $true)][string[]]$CandidateLog,
    [int[]]$BaselineCapture = @(),
    [int[]]$CandidateCapture = @(),
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$culture = [Globalization.CultureInfo]::InvariantCulture
$issues = New-Object 'System.Collections.Generic.List[string]'

function Number([string]$value) { [double]::Parse($value, $culture) }

function Read-Captures([string[]]$paths, [string]$group) {
    $runs = New-Object 'System.Collections.Generic.List[object]'
    foreach ($path in $paths) {
        $resolved = (Resolve-Path -LiteralPath $path).ProviderPath
        $current = $null
        $stream = [IO.File]::Open($resolved, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        $reader = New-Object IO.StreamReader($stream)
        try {
        while ($null -ne ($raw = $reader.ReadLine())) {
            $line = $raw -replace '^(?:\[[^\]]+\]\s*)+', ''
            if ($line -match '^Stellar Performance (?<version>\S+) diagnostic capture: (?<label>.*)$') {
                if ($null -ne $current) { $issues.Add("$group contains an incomplete capture; it was excluded.") }
                $current = @{ SourceLog = $resolved; Index = 0; Version = $Matches.version; Label = $Matches.label; Metrics = @{}; AllocationStatus = 'unvalidated' }
                continue
            }
            if ($null -eq $current) { continue }
            if ($line -match '^(?<name>Frame intervals|[A-Za-z]+): calls=(?<calls>\d+), mean=(?<mean>[\d.]+) ms, max=(?<max>[\d.]+) ms, p95=(?<p95>[\d.]+) ms, p99=(?<p99>[\d.]+) ms, errors=(?<errors>\d+), stored=(?<stored>\d+)/\d+') {
                $current.Metrics[$Matches.name] = [pscustomobject]@{ Calls = [long]$Matches.calls; MeanMs = (Number $Matches.mean); MaxMs = (Number $Matches.max); P95Ms = (Number $Matches.p95); P99Ms = (Number $Matches.p99); Errors = [long]$Matches.errors; Stored = [long]$Matches.stored }
            } elseif ($line -match '^(?<name>[A-Za-z]+): no samples$') {
                $current.Metrics[$Matches.name] = [pscustomobject]@{ Calls = 0L; MeanMs = 0.0; MaxMs = 0.0; P95Ms = 0.0; P99Ms = 0.0; Errors = 0L; Stored = 0L }
            } elseif ($line -match '^Average FPS=(?<fps>[\d.]+), median FPS=(?<median>[\d.]+), 1% low=(?<low>[\d.]+)') {
                $current.AverageFps = Number $Matches.fps
                $current.MedianFps = Number $Matches.median
                $current.OnePercentLow = Number $Matches.low
            } elseif ($line -match '^World-object snapshots=\d+, entries copied=\d+; warnings=(?<warnings>\d+), errors=(?<errors>\d+), space messages=(?<space>\d+), send-limit messages=(?<send>\d+), bounds messages=(?<bounds>\d+)') {
                $current.Warnings = [long]$Matches.warnings
                $current.Errors = [long]$Matches.errors
                $current.SpaceMessages = [long]$Matches.space
                $current.SendLimitMessages = [long]$Matches.send
                $current.BoundsMessages = [long]$Matches.bounds
            } elseif ($line -match '^Observed maxima: ships=(?<ships>\d+), parts=(?<parts>\d+) \(host, sampled at most once/second\), world objects=(?<objects>\d+), loaded cables=(?<cables>\d+)') {
                $current.Workload = "ships=$($Matches.ships); parts=$($Matches.parts); objects=$($Matches.objects); cables=$($Matches.cables)"
            } elseif ($line.StartsWith('Main-thread allocat')) {
                if ($line -match 'allocated bytes=(?<bytes>\d+) \(validated with 4096-byte positive control\)') {
                    $current.AllocationStatus = 'validated'
                    $current.AllocatedBytes = [long]$Matches.bytes
                } elseif ($line.Contains('unavailable')) { $current.AllocationStatus = 'unavailable' }
                if ($line -match 'GC collections delta=(?<gc>\d+/\d+/\d+)') { $current.GcGenerations = $Matches.gc }
                $valid = $current.ContainsKey('AverageFps') -and $current.ContainsKey('Workload') -and $current.ContainsKey('Errors') -and $current.ContainsKey('GcGenerations')
                foreach ($required in @('Frame intervals', 'FluidCables', 'ServerTick', 'NativeSave')) { $valid = $valid -and $current.Metrics.ContainsKey($required) }
                if ($valid -and $current.Metrics['Frame intervals'].Calls -gt 0) {
                    $current.Index = $runs.Count + 1
                    $runs.Add([pscustomobject]$current)
                } else { $issues.Add("$group contains an incomplete or empty capture; it was excluded.") }
                $current = $null
            }
        }
        } finally { $reader.Dispose() }
        if ($null -ne $current) { $issues.Add("$group ends with an incomplete capture; it was excluded.") }
    }
    if ($runs.Count -eq 0) { throw "$group has no complete nonempty diagnostic captures." }
    $runs.ToArray()
}

function Select-Captures([object[]]$runs, [int[]]$indices, [string]$group) {
    if ($indices.Count -eq 0) { return $runs }
    foreach ($index in ($indices | Sort-Object -Unique)) {
        if ($index -lt 1 -or $index -gt $runs.Count) { throw "$group capture index $index is outside 1..$($runs.Count)." }
        $runs[$index - 1]
    }
}

function Rows([object[]]$runs, [string]$group) {
    foreach ($run in $runs) {
        $frame = $run.Metrics['Frame intervals']
        $fluid = $run.Metrics.FluidCables
        $graph = $null
        if ($run.Metrics.ContainsKey('FluidGraphBuild') -and $run.Metrics.FluidGraphBuild.Calls -gt 0) { $graph = $run.Metrics.FluidGraphBuild.Calls * $run.Metrics.FluidGraphBuild.MeanMs / $frame.Calls }
        $frameOnly = $run.Label -match '(?:^|;\s*)diagnostic-mode=Frame(?:;|$)'
        [pscustomobject]@{
            Group = $group; Capture = $run.Index; SourceLog = $run.SourceLog; Version = $run.Version; Modules = $run.Label
            AverageFps = $run.AverageFps; OnePercentLow = $run.OnePercentLow
            FrameMeanMs = $frame.MeanMs; FrameP99Ms = $frame.P99Ms; FrameMaxMs = $frame.MaxMs
            ApproxSeconds = $frame.Calls * $frame.MeanMs / 1000
            FluidMsPerCall = if ($fluid.Calls -gt 0) { $fluid.MeanMs } else { $null }; FluidMsPerFrame = if ($fluid.Calls -gt 0) { $fluid.Calls * $fluid.MeanMs / $frame.Calls } else { $null }
            FluidGraphMsPerFrame = $graph; ServerTickMs = if ($run.Metrics.ServerTick.Calls -gt 0) { $run.Metrics.ServerTick.MeanMs } else { $null }
            SignalMsPerTick = if ($run.Metrics.ContainsKey('SignalEvaluation') -and $run.Metrics.SignalEvaluation.Calls -gt 0) { $run.Metrics.SignalEvaluation.MeanMs } else { $null }
            SignalMsPerFrame = if ($run.Metrics.ContainsKey('SignalEvaluation') -and $run.Metrics.SignalEvaluation.Calls -gt 0) { $run.Metrics.SignalEvaluation.Calls * $run.Metrics.SignalEvaluation.MeanMs / $frame.Calls } else { $null }
            ShipPartsMsPerTick = if ($run.Metrics.ContainsKey('ShipParts') -and $run.Metrics.ShipParts.Calls -gt 0) { $run.Metrics.ShipParts.MeanMs } else { $null }
            ClientVisualsMsPerFrame = if ($run.Metrics.ContainsKey('ClientVisuals') -and $run.Metrics.ClientVisuals.Calls -gt 0) { $run.Metrics.ClientVisuals.MeanMs } else { $null }
            ScriptUpdateMsPerFrame = if ($run.Metrics.ContainsKey('ScriptUpdateLoop') -and $run.Metrics.ScriptUpdateLoop.Calls -gt 0) { $run.Metrics.ScriptUpdateLoop.MeanMs } else { $null }
            HintsTipsMsPerFrame = if ($run.Metrics.ContainsKey('HintsTips') -and $run.Metrics.HintsTips.Calls -gt 0) { $run.Metrics.HintsTips.MeanMs } else { $null }
            SaveCalls = if (!$frameOnly) { $run.Metrics.NativeSave.Calls } else { $null }; SaveMaxMs = if (!$frameOnly) { $run.Metrics.NativeSave.MaxMs } else { $null }
            Workload = $run.Workload; Warnings = $run.Warnings; Errors = $run.Errors
            AllocationStatus = $run.AllocationStatus; AllocatedBytes = if ($run.AllocationStatus -eq 'validated') { $run.AllocatedBytes } else { $null }; GcGenerations = $run.GcGenerations
        }
    }
}

function Mean([object[]]$rows, [string]$property) {
    $available = @($rows | Where-Object { $null -ne $_.$property })
    if ($available.Count -eq 0) { return $null }
    ($available | Measure-Object -Property $property -Average).Average
}
function Summary([object[]]$rows) {
    [pscustomobject]@{
        Captures = $rows.Count
        AverageFps = (Mean $rows 'AverageFps'); OnePercentLow = (Mean $rows 'OnePercentLow')
        FrameP99Ms = (Mean $rows 'FrameP99Ms'); FluidMsPerFrame = (Mean $rows 'FluidMsPerFrame'); ServerTickMs = (Mean $rows 'ServerTickMs')
        SignalMsPerTick = (Mean $rows 'SignalMsPerTick'); SignalMsPerFrame = (Mean $rows 'SignalMsPerFrame'); ShipPartsMsPerTick = (Mean $rows 'ShipPartsMsPerTick')
        ClientVisualsMsPerFrame = (Mean $rows 'ClientVisualsMsPerFrame'); ScriptUpdateMsPerFrame = (Mean $rows 'ScriptUpdateMsPerFrame'); HintsTipsMsPerFrame = (Mean $rows 'HintsTipsMsPerFrame')
        FpsMin = ($rows | Measure-Object AverageFps -Minimum).Minimum; FpsMax = ($rows | Measure-Object AverageFps -Maximum).Maximum
        FluidMin = ($rows | Measure-Object FluidMsPerFrame -Minimum).Minimum; FluidMax = ($rows | Measure-Object FluidMsPerFrame -Maximum).Maximum
    }
}

$baseline = @(Select-Captures @(Read-Captures $BaselineLog 'Baseline') $BaselineCapture 'Baseline')
$candidate = @(Select-Captures @(Read-Captures $CandidateLog 'Candidate') $CandidateCapture 'Candidate')
$baselineRows = @(Rows $baseline 'Baseline')
$candidateRows = @(Rows $candidate 'Candidate')
$allRuns = @($baseline) + @($candidate)
$allRows = @($baselineRows) + @($candidateRows)
if (@($allRuns.Workload | Sort-Object -Unique).Count -ne 1) { $issues.Add('Workload maxima differ; the selected captures are not workload-matched.') }
if (@($allRuns.Version | Sort-Object -Unique).Count -ne 1) { $issues.Add('Mod versions differ; instrumentation may differ. Use the same diagnostic build for a controlled comparison.') }
$probeSets = @($allRuns | ForEach-Object { ((($_.Label -replace '^modules=', '') -split ',' | Where-Object { $_ -like 'profile-*' } | Sort-Object) -join ',') } | Sort-Object -Unique)
if ($probeSets.Count -ne 1) { $issues.Add('Installed diagnostic probe groups differ.') }
$diagnosticModes = @($allRuns | ForEach-Object { if ($_.Label -match '(?:^|;\s*)diagnostic-mode=(Frame|Lean|Full)(?:;|$)') { $Matches[1] } else { 'LegacyFull' } } | Sort-Object -Unique)
if ($diagnosticModes.Count -ne 1) { $issues.Add('Diagnostic modes differ; instrumentation overhead differs. Do not attribute this comparison solely to optimization gains.') }
if ($diagnosticModes -contains 'Frame') { $issues.Add('Frame mode does not observe workload, saves or sends; verify the controlled world and workload independently.') }
if (@($allRows | Where-Object { $_.SaveCalls -gt 0 }).Count -gt 0) { $issues.Add('Save work occurred; frame-time tails are confounded. Do not silently exclude the affected capture or attribute hitches to the candidate.') }
if (@($allRuns | Where-Object { $_.AllocationStatus -ne 'validated' }).Count -gt 0) { $issues.Add('Unvalidated/unavailable allocation counters are excluded from allocation conclusions; old zero-byte readings are not evidence of zero allocation.') }
if (@($allRuns | Where-Object { $_.Errors -gt 0 -or $_.Warnings -gt 0 -or @($_.Metrics.Values | Where-Object { $_.Errors -gt 0 }).Count -gt 0 }).Count -gt 0) { $issues.Add('Warnings or errors occurred in at least one selected capture.') }
if (@($allRuns | Where-Object { $_.Metrics['Frame intervals'].Stored -lt $_.Metrics['Frame intervals'].Calls }).Count -gt 0) { $issues.Add('Some frame quantiles use bounded reservoir samples, not every recorded interval.') }
$shortest = ($allRows | Measure-Object ApproxSeconds -Minimum).Minimum
$longest = ($allRows | Measure-Object ApproxSeconds -Maximum).Maximum
if ($longest -gt $shortest * 1.1) { $issues.Add('Capture durations differ by more than 10%; workload exposure and tail comparisons may differ.') }
$baselineSummary = Summary $baselineRows
$candidateSummary = Summary $candidateRows
$deltas = [ordered]@{}
foreach ($property in @('AverageFps', 'OnePercentLow', 'FrameP99Ms', 'ScriptUpdateMsPerFrame', 'ClientVisualsMsPerFrame', 'FluidMsPerFrame', 'HintsTipsMsPerFrame', 'ServerTickMs', 'SignalMsPerTick', 'SignalMsPerFrame', 'ShipPartsMsPerTick')) {
    $before = $baselineSummary.$property
    $after = $candidateSummary.$property
    $percent = $null
    if ($null -ne $before -and $null -ne $after -and $before -gt 0) { $percent = 100 * ($after / $before - 1) }
    $deltas[$property] = [pscustomobject]@{ Baseline = $before; Candidate = $after; Difference = if ($null -eq $before -or $null -eq $after) { $null } else { $after - $before }; Percent = $percent }
}
$result = [pscustomobject]@{
    Baseline = $baselineRows; Candidate = $candidateRows
    BaselineSummary = $baselineSummary; CandidateSummary = $candidateSummary
    Deltas = $deltas; Flags = @($issues.ToArray())
    Notes = @('Summary means are equally weighted per-capture summaries; percentiles and 1% lows are not pooled or averaged frame samples.', 'Counts are loaded/sampled maxima, not proof of identical view or simulation activity.', 'Wall-clock timings overlap and include hook overhead; do not add them or infer a CPU/GPU bottleneck.', 'GC generations are listed separately and never summed. Single or uncontrolled captures do not establish causality.')
}
if ($AsJson) { $result | ConvertTo-Json -Depth 8; return }
$allRows | Format-Table Group, Capture, AverageFps, OnePercentLow, FrameP99Ms, FluidMsPerFrame, ServerTickMs, SaveCalls -AutoSize
'Mean-of-capture comparison (tail summaries are not pooled):'
$deltas.GetEnumerator() | ForEach-Object { [pscustomobject]@{ Metric = $_.Key; Baseline = if ($null -eq $_.Value.Baseline) { $null } else { [Math]::Round($_.Value.Baseline, 3) }; Candidate = if ($null -eq $_.Value.Candidate) { $null } else { [Math]::Round($_.Value.Candidate, 3) }; ChangePercent = if ($null -eq $_.Value.Percent) { $null } else { [Math]::Round($_.Value.Percent, 2) } } } | Format-Table -AutoSize
"Baseline FPS range: $($baselineSummary.FpsMin)..$($baselineSummary.FpsMax); candidate: $($candidateSummary.FpsMin)..$($candidateSummary.FpsMax)"
foreach ($flag in $result.Flags) { "Caution: $flag" }
foreach ($note in $result.Notes) { $note }
