param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../outputs/system-monitor-widget'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler not found.' }
$destination = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $destination | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$icon = Join-Path $PSScriptRoot 'assets/PulseWidget.ico'
if (-not (Test-Path -LiteralPath $icon)) { & (Join-Path $PSScriptRoot 'create-icon.ps1') }
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output','/warn:4',('/out:' + (Join-Path $destination 'PulseWidget.exe')),('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Management.dll','/reference:System.Web.Extensions.dll') + $sources
$arguments += @(('/win32icon:' + $icon), ('/resource:' + $icon + ',PulseWidget.AppIcon'))
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination (Join-Path $destination 'PulseWidget.exe.config') -Force
Get-Item -LiteralPath (Join-Path $destination 'PulseWidget.exe') | Select-Object FullName,Length,LastWriteTime
