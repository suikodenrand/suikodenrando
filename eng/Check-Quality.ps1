#requires -Version 7.0

[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$BaseRef = 'HEAD',
    [switch]$FormatOnly
)

$ErrorActionPreference = 'Stop'

function Invoke-Checked {
    param([string]$Command, [string[]]$Arguments)

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command $($Arguments -join ' ') exited with code $LASTEXITCODE."
    }
}

Push-Location (Join-Path $PSScriptRoot '..')
try {
    $base = Invoke-Checked git @('rev-parse', '--verify', '--end-of-options', "$BaseRef^{commit}")
    $changedFiles = @(
        Invoke-Checked git @('-c', 'core.quotePath=false', 'diff', '--name-only', '--diff-filter=ACMRT', $base, '--', '*.cs')
        Invoke-Checked git @('-c', 'core.quotePath=false', 'ls-files', '--others', '--exclude-standard', '--', '*.cs')
    )
    $csharpFiles = @($changedFiles | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Sort-Object -Unique)

    if ($csharpFiles.Count -gt 0) {
        Write-Host "Checking C# whitespace in $($csharpFiles.Count) changed files."
        # Keep each command line below the Windows process argument limit.
        for ($start = 0; $start -lt $csharpFiles.Count; $start += 50) {
            $batch = $csharpFiles | Select-Object -Skip $start -First 50
            Invoke-Checked dotnet (@('format', 'whitespace', '.', '--folder', '--verify-no-changes', '--include') + $batch)
        }
    }
    else {
        Write-Host 'No changed C# files to format-check.'
    }

    if ($FormatOnly) { return }

    foreach ($project in @(
        'src/StarsOfDestiny.Launcher/StarsOfDestiny.Launcher.csproj'
        'src/StarsOfDestiny.CtbEditor/StarsOfDestiny.CtbEditor.csproj'
    )) {
        Invoke-Checked dotnet @('build', $project, '-c', 'Release', '--nologo')
    }

    # These executables need native interop, extracted game data, or built release DLLs.
    $specialistTests = @(
        'StarsOfDestiny.Launcher.AtbBuild.Tests'
        'SuikodenRandomizer.Dialogue.Bridge.Tests'
        'SuikodenRandomizer.PluginInventory.Tests'
        'SuikodenRandomizer.Unified.Metadata.Tests'
    )
    $testProjects = Get-ChildItem -Path 'tests/*/*.csproj' -File |
        Where-Object { $_.BaseName.EndsWith('.Tests') -and $_.BaseName -notin $specialistTests } |
        Sort-Object BaseName

    foreach ($project in $testProjects) {
        Write-Host "Running $($project.BaseName)"
        Invoke-Checked dotnet @('run', '--project', $project.FullName, '-c', 'Release')
    }

    Invoke-Checked python @('-m', 'unittest', 'discover', '-s', 'packaging/release/tests', '-v')
}
finally {
    Pop-Location
}
