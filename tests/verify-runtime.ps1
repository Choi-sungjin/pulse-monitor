param(
    [Parameter(Mandatory=$true)][int]$WidgetProcessId,
    [int]$SampleCount=61,
    [string]$OutputPath=(Join-Path $PSScriptRoot '../../outputs/system-monitor-widget-qa/performance.json')
)
$ErrorActionPreference='Stop'
$samples = @()
for ($index=0; $index -lt $SampleCount; $index++) {
    $process=Get-Process -Id $WidgetProcessId -ErrorAction Stop
    $samples += [pscustomobject]@{ Time=[DateTime]::UtcNow; CpuSeconds=$process.TotalProcessorTime.TotalSeconds; WorkingSetMB=$process.WorkingSet64/1MB; PrivateMB=$process.PrivateMemorySize64/1MB; Handles=$process.HandleCount }
    if ($index -lt $SampleCount-1) { Start-Sleep -Seconds 2 }
}
$first=$samples[0]
$last=$samples[-1]
$elapsed=($last.Time-$first.Time).TotalSeconds
$summary=[ordered]@{
    ProcessId=$WidgetProcessId; SampleCount=$samples.Count; ElapsedSeconds=$elapsed
    MeanCpuPercentOfWholeMachine=100*($last.CpuSeconds-$first.CpuSeconds)/$elapsed/[Environment]::ProcessorCount
    MeanCpuPercentOfOneCore=100*($last.CpuSeconds-$first.CpuSeconds)/$elapsed
    MinWorkingSetMB=($samples.WorkingSetMB|Measure-Object -Minimum).Minimum
    MaxWorkingSetMB=($samples.WorkingSetMB|Measure-Object -Maximum).Maximum
    FirstPrivateMB=$first.PrivateMB; LastPrivateMB=$last.PrivateMB
    FirstHandles=$first.Handles;LastHandles=$last.Handles
    MinHandles=($samples.Handles|Measure-Object -Minimum).Minimum
    MaxHandles=($samples.Handles|Measure-Object -Maximum).Maximum
    Note='Short observation only; not a long-duration leak guarantee.'
    Samples=$samples
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
[pscustomobject]$summary | Select-Object * -ExcludeProperty Samples | Format-List
