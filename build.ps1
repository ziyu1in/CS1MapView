[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$NoRestore,
    [switch]$SmokeTest,
    [string]$MapFile
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
if (!$env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME = Join-Path $repoRoot '.tools/dotnet-home' }
if (!$env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $repoRoot '.tools/nuget-packages' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($dotnetCommand) { $dotnetCommand.Source } else { $null }
if (!$dotnet) {
    foreach ($candidate in @('.tools/dotnet/dotnet.exe', '.tools/dotnet8/dotnet.exe', '.tools/dotnet-sdk/dotnet.exe')) {
        $candidatePath = Join-Path $repoRoot $candidate
        if (Test-Path -LiteralPath $candidatePath) { $dotnet = $candidatePath; break }
    }
}
if (!$dotnet) { throw 'Install .NET SDK 8 or newer with Windows desktop support.' }

$referenceMap = $null
if ($SmokeTest) {
    $referencePath = if ($MapFile) { $MapFile } else { Join-Path $repoRoot 'samples/reference.cslmap.gz' }
    if (!(Test-Path -LiteralPath $referencePath -PathType Leaf)) {
        throw 'Full checks require a local integration-test export. Supply -MapFile <path>.'
    }
    $referenceMap = (Resolve-Path -LiteralPath $referencePath).Path
}

Push-Location $repoRoot
try {
    $buildArgs = @('build', 'CS1MapView.sln', '-c', $Configuration, '-v', 'minimal', '--nologo')
    if ($NoRestore) { $buildArgs += '--no-restore' }
    & $dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    if ($SmokeTest) {
        $checkExe = Join-Path $repoRoot "tests/CS1MapView.Checks/bin/$Configuration/net8.0-windows/CS1MapView.Checks.exe"
        & $checkExe $repoRoot $referenceMap
        if ($LASTEXITCODE -ne 0) { throw 'Acceptance checks failed.' }
    }

    Write-Host "Viewer: $(Join-Path $repoRoot "src/CS2MapView.Form/bin/$Configuration/net8.0-windows/CS1MapView.exe")"
}
finally { Pop-Location }
