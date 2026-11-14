#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$GamePath,
    [Alias('RestoreTransaction')][string]$TransactionPath,
    [switch]$CheckOnly,
    [switch]$NonInteractive
)
$ErrorActionPreference = 'Stop'
try {
    if ([string]::IsNullOrWhiteSpace($GamePath)) {
        $installedParent = [IO.Path]::GetDirectoryName($PSScriptRoot)
        if ([IO.File]::Exists((Join-Path $installedParent 'Suikoden I and II HD Remaster.exe'))) {
            $GamePath = $installedParent
        }
    }
    if ([string]::IsNullOrWhiteSpace($TransactionPath)) {
        if ($NonInteractive) { throw 'Supply -TransactionPath with the FullPreview backup folder printed by setup.' }
        $TransactionPath = Read-Host 'Paste the FullPreview backup folder from the installation receipt'
        if ([string]::IsNullOrWhiteSpace($TransactionPath)) { throw 'Choose a transaction backup folder to restore.' }
    }
    & (Join-Path $PSScriptRoot 'Install.ps1') -GamePath $GamePath -RestoreTransaction $TransactionPath -CheckOnly:$CheckOnly -NonInteractive:$NonInteractive
    exit $LASTEXITCODE
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
