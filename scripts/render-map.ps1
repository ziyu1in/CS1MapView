[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputFile,
    [Parameter(Mandatory)][string]$OutputFile,
    [ValidateRange(32,32768)][int]$CanvasSize = 8192,
    [string]$Crop,
    [double]$Rotate = 0,
    [string]$Style = 'apple-light',
    [string]$Config,
    [Nullable[int]]$HighlightStationNode,
    [switch]$Transit,
    [switch]$Heightmap,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (!$SkipBuild) { & (Join-Path $repoRoot 'build.ps1') }
$exe = Join-Path $repoRoot 'src\CS2MapView.Form\bin\Release\net8.0-windows\CS1MapView.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Build the CS1MapView viewer first.' }
$source = (Resolve-Path -LiteralPath $InputFile).Path
$destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputFile)
$parent = Split-Path -Parent $destination
New-Item -ItemType Directory -Force -Path $parent | Out-Null
$argsList = @($(if ($Heightmap) { '--heightmap' } else { '--render' }), $source, '--output', $destination, '--size', $CanvasSize, '--style', $Style, '--rotate', $Rotate.ToString([Globalization.CultureInfo]::InvariantCulture))
if ($Crop) { $argsList += @('--crop', $Crop) }
if ($Config) { $argsList += @('--config', (Resolve-Path -LiteralPath $Config).Path) }
if ($null -ne $HighlightStationNode) { $argsList += @('--highlight', $HighlightStationNode) }
if ($Transit) { $argsList += '--transit' }
$logDirectory = Join-Path $repoRoot ('artifacts\map-renders\run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$quoted = $argsList | ForEach-Object { '"' + $_.ToString().Replace('"', '\"') + '"' }
$process = Start-Process -FilePath $exe -ArgumentList $quoted -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDirectory 'stdout.log') -RedirectStandardError (Join-Path $logDirectory 'stderr.log')
$timer = [Diagnostics.Stopwatch]::StartNew()
while (!$process.WaitForExit(1000)) {
    if ($timer.Elapsed.TotalMinutes -gt 10) { Stop-Process -Id $process.Id; throw "CS1MapView rendering timed out. See $logDirectory" }
}
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $destination)) { throw "CS1MapView rendering failed. See $logDirectory" }
Write-Host "Rendered: $destination"
Write-Host "Logs: $logDirectory"
