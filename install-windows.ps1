param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs/PulseMonitor'),
    [switch]$NoAutostart,
    [switch]$NoLaunch
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Run this installer on Windows.' }
$destination = [IO.Path]::GetFullPath($InstallDirectory)
$exe = Join-Path $destination 'PulseWidget.exe'
# Do not terminate an existing app or overwrite its loaded files.
$running = @(Get-Process -Name PulseWidget -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) { throw 'Exit Pulse from its tray menu before installing or updating.' }
$stage = Join-Path ([IO.Path]::GetTempPath()) ('PulseSetup-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $stage | Out-Null
    $prebuilt = Join-Path $PSScriptRoot 'dist/PulseWidget.exe'
    if (Test-Path -LiteralPath $prebuilt) {
        Copy-Item -LiteralPath $prebuilt -Destination $stage
        Copy-Item -LiteralPath ($prebuilt + '.config') -Destination $stage
    } else {
        & (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $stage
    }
    $testPath = Join-Path $stage 'self-test.json'
    $test = Start-Process -FilePath (Join-Path $stage 'PulseWidget.exe') -ArgumentList @('--self-test', ('"' + $testPath + '"')) -PassThru -Wait
    if ($test.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $testPath)) { throw 'Pulse self-test failed.' }
    $result = Get-Content -LiteralPath $testPath -Raw | ConvertFrom-Json
    if (-not $result.Passed) { throw 'Pulse self-test reported failure.' }
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -LiteralPath (Join-Path $stage 'PulseWidget.exe') -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $stage 'PulseWidget.exe.config') -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $destination -Force
    $programs = [Environment]::GetFolderPath('Programs')
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path $programs 'Pulse Monitor.lnk'))
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $destination
    $shortcut.IconLocation = $exe
    $shortcut.Save()
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if ($NoAutostart) {
        Remove-ItemProperty -LiteralPath $runKey -Name PulseWidget -ErrorAction SilentlyContinue
    } else {
        if (-not (Test-Path -LiteralPath $runKey)) { New-Item -Path $runKey | Out-Null }
        New-ItemProperty -Path $runKey -Name PulseWidget -Value ('"' + $exe + '"') -PropertyType String -Force | Out-Null
    }
    Write-Output ('Installed: ' + $exe)
    Write-Output ('Self-test checks passed: ' + $result.Checks.Count)
    if (-not $NoLaunch) { Start-Process -FilePath $exe }
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
