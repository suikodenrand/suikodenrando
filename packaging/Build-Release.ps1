#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GamePath,
    [string]$Dotnet = 'dotnet',
    [string]$Python = 'python',
    [string]$OutputDirectory,
    [ValidateSet('Binaries', 'Manual', 'All')][string]$PackageMode = 'Binaries',
    [string]$MakeNsis,
    [string]$DependenciesDirectory,
    [switch]$DownloadDependencies,
    [switch]$StageOnly
)
$ErrorActionPreference = 'Stop'
function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    } finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}
$repo = Split-Path -Parent $PSScriptRoot
$callerLocation = $ExecutionContext.SessionState.Path
$game = $callerLocation.GetUnresolvedProviderPathFromPSPath($GamePath)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'artifacts/0.14.4' }
$output = $callerLocation.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if ($output -eq $game -or $output.StartsWith($game.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build output must be outside the game installation.'
}
if (-not $DependenciesDirectory) { $DependenciesDirectory = Join-Path $output 'dependencies' }
$DependenciesDirectory = $callerLocation.GetUnresolvedProviderPathFromPSPath($DependenciesDirectory)
if ($MakeNsis) { $MakeNsis = $callerLocation.GetUnresolvedProviderPathFromPSPath($MakeNsis) }
if ($PackageMode -eq 'All' -and -not $StageOnly -and -not $MakeNsis) { throw 'All mode needs -MakeNsis, or -StageOnly for staging qualification.' }
$expectedGame = 'e093f175eaf3734d293ca0a236c652dd7fcc72ea5fa10d7b21103edf12f11c3a'
$assembly = Join-Path $game 'GameAssembly.dll'
if (-not (Test-Path -LiteralPath $assembly -PathType Leaf) -or (Get-Sha256 $assembly) -ne $expectedGame) {
    throw 'The supported game build is required; see BUILDING.md.'
}
foreach ($name in @('GSD1.dll', 'GSD2.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $game "BepInEx/interop/$name") -PathType Leaf)) {
        throw "First run BepInEx to generate local interop references: $name is missing."
    }
}
function Invoke-Dotnet([string[]]$Arguments) {
    & $Dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet command failed with exit code $LASTEXITCODE." }
}
$runtime = Join-Path $output 'runtime'
$atb = Join-Path $output 'atb'
$launcher = Join-Path $output 'launcher'
New-Item -ItemType Directory -Path $runtime,$atb,$launcher -Force | Out-Null
Push-Location $repo
try {
    if ((& $Dotnet --version).Trim() -ne '10.0.403') { throw 'Use the .NET SDK version pinned in global.json: 10.0.403.' }
    $common = @('-c', 'Release', '--disable-build-servers', '-m:1', '-p:BuildInParallel=false', '-p:RestoreDisableParallel=true', '-p:NuGetAudit=false')
    Invoke-Dotnet (@('build', 'src/SuikodenRandomizer.Unified/SuikodenRandomizer.Unified.csproj',
        '-p:Version=0.14.4', "-p:GamePath=$game") + $common)
    foreach ($name in @('SuikodenRandomizer.dll', 'SuikodenRandomizer.Core.dll', 'SuikodenRandomizer.Suikoden2.Core.dll', 'SuikodenRandomizer.Shared.dll')) {
        Copy-Item -LiteralPath (Join-Path $repo "src/SuikodenRandomizer.Unified/bin/Release/$name") -Destination (Join-Path $runtime $name)
    }
    Invoke-Dotnet (@('build', 'src/SuikodenRandomizer.S1BattlePreview/SuikodenRandomizer.S1BattlePreview.csproj', "-p:GamePath=$game") + $common)
    $atbDll = Join-Path $atb 'SuikodenRandomizer.S1BattlePreview.dll'
    Copy-Item -LiteralPath (Join-Path $repo 'src/SuikodenRandomizer.S1BattlePreview/bin/Release/net6.0/SuikodenRandomizer.S1BattlePreview.dll') -Destination $atbDll
    $reviewedAtbHash = Get-Sha256 $atbDll
    # The launcher retains its integrity guard. A custom source build explicitly
    # records the ATB binary produced by this same build instead of the historical
    # release hash, which varies when deterministic compilation paths differ.
    Invoke-Dotnet (@('publish', 'src/StarsOfDestiny.Launcher/StarsOfDestiny.Launcher.csproj',
        '-r', 'win-x64', '--self-contained', 'true', '-o', $launcher,
        '-p:RuntimeFrameworkVersion=10.0.14', "-p:ReviewedAtbSha256=$reviewedAtbHash") + $common)
    foreach ($name in @('dotnet10-Core-LICENSE.txt', 'dotnet10-Core-THIRD-PARTY-NOTICES.txt', 'dotnet10-WindowsDesktop-LICENSE.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $launcher $name) -PathType Leaf)) { throw "Missing published runtime notice: $name" }
    }
    if ($PackageMode -ne 'Binaries') {
        $mode = if ($PackageMode -eq 'All') { 'all' } else { 'manual' }
        $arguments = @((Join-Path $PSScriptRoot 'release/build.py'), '--mode', $mode,
            '--runtime-dir', $runtime, '--atb-dll', $atbDll, '--launcher-dir', $launcher,
            '--reviewed-atb-sha256', $reviewedAtbHash, '--dependencies', $DependenciesDirectory,
            '--output', (Join-Path $output 'packages'))
        if ($MakeNsis) { $arguments += @('--makensis', $MakeNsis) }
        if ($DownloadDependencies) { $arguments += '--download-dependencies' }
        if ($StageOnly) { $arguments += '--stage-only' }
        & $Python @arguments
        if ($LASTEXITCODE -ne 0) { throw "Release packaging failed with exit code $LASTEXITCODE." }
    }
    $report = [ordered]@{version='0.14.4'; packageMode=$PackageMode; runtime=$runtime; atb=$atbDll;
        launcher=$launcher; reviewedAtbSha256=$reviewedAtbHash; gameModified=$false; published=$false}
    [IO.File]::WriteAllText((Join-Path $output 'release-build.json'), ($report | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    Write-Output "Built Stars of Destiny 0.14.4 in $output"
} finally { Pop-Location }
