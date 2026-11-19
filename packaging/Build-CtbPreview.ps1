#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ReleaseBuildDirectory,
    [string]$OutputDirectory,
    [string]$Dotnet = 'dotnet',
    [string]$Python = 'python'
)
$ErrorActionPreference = 'Stop'
$build = (Resolve-Path -LiteralPath $ReleaseBuildDirectory).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $build 'packages' }
& $Python (Join-Path $PSScriptRoot 'release/package_ctb.py') --build $build --output $OutputDirectory --dotnet $Dotnet
if ($LASTEXITCODE -ne 0) { throw "CTB preview packaging failed ($LASTEXITCODE)." }
