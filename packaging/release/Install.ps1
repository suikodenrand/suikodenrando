#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$GamePath,
    [ValidateSet('Install', 'Repair', 'Uninstall')][string]$Mode = 'Install',
    [switch]$CheckOnly,
    [string]$RestoreTransaction,
    [switch]$IncludeAtb,
    [switch]$RemoveConfig,
    [switch]$RemoveShortcuts,
    [switch]$NonInteractive
)

$ErrorActionPreference = 'Stop'
$gameExe = 'Suikoden I and II HD Remaster.exe'
$pluginFolder = 'BepInEx\plugins\SuikodenRandomizer'
$atbDll = 'SuikodenRandomizer.S1BattlePreview.dll'
$activeAtb = "BepInEx\plugins\$atbDll"
$dormantAtb = "BepInEx\$atbDll"
$atbConfig = 'BepInEx\config\agilen.suikoden1.battle-preview.cfg'
$legacyDll = 'SuikodenRandomizer.Suikoden2.dll'
$launcherFolder = 'StarsOfDestiny Launcher'
$launcherExe = 'StarsOfDestiny.Launcher.exe'
$supportFolder = 'StarsOfDestiny Setup'
$fixDll = 'Suikoden_Fix.dll'
$configNames = @(
    'agilen.suikoden.recruitment_randomizer.cfg'
    'agilen.suikoden2.recruitment_randomizer.cfg'
    'agilen.starsofdestiny.cfg'
    'agilen.suikoden1.battle-preview.cfg'
    'd3xMachina.suikoden_fix.cfg'
)
$pluginNames = @(
    'SuikodenRandomizer.dll'
    'SuikodenRandomizer.Core.dll'
    'SuikodenRandomizer.Suikoden2.Core.dll'
    'SuikodenRandomizer.Shared.dll'
)
$retiredSupportFiles = @(
    "$supportFolder\Install.cmd"
    "$supportFolder\Licenses\Source\BepInEx-source-5362580.zip"
    "$supportFolder\Licenses\Source\Il2CppInterop-source-56bf1da.zip"
    "$supportFolder\Licenses\Source\UnityDoorstop-source-v4.3.0.zip"
)

function Get-FileSystemItem([string]$Path) {
    try { Get-Item -LiteralPath $Path -Force -ErrorAction Stop }
    catch [System.Management.Automation.ItemNotFoundException] { $null }
}

function Assert-PlainPath([string]$Path, [bool]$Directory) {
    $current = $Path
    $item = Get-FileSystemItem $current
    $first = $item
    while ($true) {
        if ($null -ne $item) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Setup cannot use a linked path. Move this item or choose an unlinked game folder. $current"
            }
            if ($item.PSIsContainer -ne $Directory) {
                throw "A file or folder occupies an incompatible path. $current"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrEmpty($parent)) { break }
        $current = $parent
        $Directory = $true
        $item = Get-FileSystemItem $current
    }
    $first
}

function Find-SteamGame {
    $registryPaths = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1932640'
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1932640'
    )
    foreach ($key in $registryPaths) {
        $entry = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
        if ($entry.InstallLocation) { $entry.InstallLocation }
    }

    $steamRoots = @(
        (Get-ItemProperty -LiteralPath 'HKCU:\SOFTWARE\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
        (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -ErrorAction SilentlyContinue).InstallPath
    )
    if (${env:ProgramFiles(x86)}) { $steamRoots += Join-Path ${env:ProgramFiles(x86)} 'Steam' }
    $libraries = foreach ($steamRoot in ($steamRoots | Where-Object { $_ } | Select-Object -Unique)) {
        $steamRoot
        $vdfPath = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
        if ([IO.File]::Exists($vdfPath)) {
            $vdf = [IO.File]::ReadAllText($vdfPath)
            foreach ($match in [regex]::Matches($vdf, '(?im)^\s*"(?:path|[0-9]+)"\s*"([^"]+)"')) {
                $library = $match.Groups[1].Value.Replace('\\', '\')
                if ([IO.Path]::IsPathRooted($library)) { $library }
            }
        }
    }
    foreach ($library in ($libraries | Select-Object -Unique)) {
        Join-Path $library 'steamapps\common\Suikoden I and II HD Remaster'
    }
}

function Get-GameFolder {
    if ($GamePath) { return [IO.Path]::GetFullPath($GamePath.Trim().Trim('"')) }
    $found = @(Find-SteamGame | Where-Object {
        [IO.File]::Exists((Join-Path $_ $gameExe))
    } | ForEach-Object { [IO.Path]::GetFullPath($_) } | Sort-Object -Unique)
    if ($found.Count -eq 1) { return $found[0] }
    if ($NonInteractive) { throw 'Supply -GamePath with the full game folder path.' }
    if ($found.Count -gt 1) {
        Write-Host 'Steam reported multiple game folders.'
        $found | ForEach-Object { Write-Host "  $_" }
    }
    $answer = Read-Host 'Paste the game folder from Steam > Manage > Browse local files'
    if ([string]::IsNullOrWhiteSpace($answer)) { throw 'Choose a game folder to continue.' }
    [IO.Path]::GetFullPath($answer.Trim().Trim('"'))
}

function Assert-GameClosed {
    foreach ($name in @([IO.Path]::GetFileNameWithoutExtension($gameExe), [IO.Path]::GetFileNameWithoutExtension($launcherExe), 'StarsOfDestiny.CtbEditor')) {
        $running = [Diagnostics.Process]::GetProcessesByName($name)
        try {
            if ($running.Length -gt 0) { throw 'Close Suikoden I and II HD Remaster and the Stars of Destiny launcher and CTB editor before running setup.' }
        } finally { foreach ($process in $running) { $process.Dispose() } }
    }
}

function Get-Hash([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $inputFile = [IO.File]::OpenRead($Path)
        try { [BitConverter]::ToString($sha.ComputeHash($inputFile)).Replace('-', '') }
        finally { $inputFile.Dispose() }
    } finally { $sha.Dispose() }
}

function Assert-GameBuild([string]$AssemblyPath, [string]$ExpectedHash) {
    $actualHash = Get-Hash $AssemblyPath
    if ($actualHash -ine $ExpectedHash) {
        throw ("This game build differs from the version supported by the preview.`n" +
            "Expected SHA-256  $ExpectedHash`n" +
            "Actual SHA-256    $actualHash`nSee Troubleshooting.txt.")
    }
}

function Test-DoorstopConfig([string]$Path) {
    $section = ''
    $values = @{}
    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        if ($line -match '^\s*\[([^\]]+)\]\s*$') {
            $section = $Matches[1].Trim()
        } elseif ($line -match '^\s*([a-zA-Z_]+)\s*=\s*(.*?)\s*$') {
            $key = "$section.$($Matches[1])"
            if ($values.ContainsKey($key)) { return $false }
            $values[$key] = $Matches[2].Replace('/', '\') -replace '^\.\\', ''
        }
    }
    $values['General.enabled'] -eq 'true' -and
    $values['General.target_assembly'] -eq 'BepInEx\core\BepInEx.Unity.IL2CPP.dll' -and
    $values['Il2Cpp.coreclr_path'] -eq 'dotnet\coreclr.dll' -and
    ([string]$values['Il2Cpp.corlib_dir']).TrimEnd('\') -eq 'dotnet'
}

function Assert-NoDuplicatePlugins([string]$Root) {
    $base = Join-Path $Root 'BepInEx\plugins'
    if ($null -eq (Assert-PlainPath $base $true)) { return }
    $canonical = Join-Path $Root $pluginFolder
    $known = $pluginNames + $legacyDll
    $pluginMarkers = @{
        'agilen.starsofdestiny' = "$canonical\SuikodenRandomizer.dll"
        'agilen.suikoden.recruitment_randomizer' = "$canonical\SuikodenRandomizer.dll"
        'agilen.suikoden2.recruitment_randomizer' = "$canonical\$legacyDll"
        'agilen.suikoden1.battle-preview' = "$base\$atbDll"
        'd3xMachina.suikoden_fix' = "$base\$fixDll"
    }
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($base)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($child in (Get-ChildItem -LiteralPath $directory -Force)) {
            $linked = ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
            if ($known -contains $child.Name) {
                if ($directory -ine $canonical -or $child.PSIsContainer -or $linked) {
                    throw "Move this duplicate or linked randomizer DLL outside BepInEx\plugins. $($child.FullName)"
                }
            }
            if ($child.Name -ieq $atbDll) {
                if ($directory -ine $base -or $child.PSIsContainer -or $linked) {
                    throw "Move this duplicate or linked ATB DLL outside BepInEx\plugins. $($child.FullName)"
                }
            }
            if ($child.Name -ieq $fixDll -and ($directory -ine $base -or $child.PSIsContainer -or $linked)) {
                throw "Move this duplicate or linked Suikoden Fix DLL outside BepInEx\plugins. $($child.FullName)"
            }
            if ($child.PSIsContainer) {
                if ($linked) { throw "Setup cannot inspect a linked plugins folder. $($child.FullName)" }
                $pending.Push($child.FullName)
            } elseif ($child.Extension -ieq '.dll') {
                if ($linked) { throw "Setup cannot inspect a linked plugin. $($child.FullName)" }
                # Managed type names and serialized BepInPlugin GUIDs are UTF-8 in PE metadata.
                # Read bytes only. Candidate plugins are never loaded or executed.
                $metadataText = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($child.FullName))
                if ($metadataText.Contains("StartingPartyVirtualizerPatch`0")) {
                    throw "The old embedded Suikoden Fix randomizer is present. Move it outside BepInEx\plugins before setup. $($child.FullName)"
                }
                foreach ($marker in $pluginMarkers.Keys) {
                    $serializedGuid = [string][char]([Text.Encoding]::UTF8.GetByteCount($marker)) + $marker
                    if ($metadataText.Contains($serializedGuid) -and $child.FullName -ine $pluginMarkers[$marker]) {
                        throw "Move this duplicate or renamed randomizer, ATB, or Fix plugin outside BepInEx\plugins. $($child.FullName)"
                    }
                }
            }
        }
    }
}

function Assert-TargetName([string]$Target) {
    $allowed = $Target -in @('.doorstop_version', 'changelog.txt', 'doorstop_config.ini', 'winhttp.dll',
        $activeAtb, $dormantAtb, "BepInEx\plugins\$fixDll") -or
        $Target -match '^(?:dotnet|BepInEx\\core)\\[^\\]+$' -or
        ($pluginNames | ForEach-Object { "$pluginFolder\$_" }) -contains $Target -or
        ($configNames | ForEach-Object { "BepInEx\config\$_" }) -contains $Target -or
        $Target.StartsWith("$supportFolder\", [StringComparison]::OrdinalIgnoreCase) -or
        $Target.StartsWith("$launcherFolder\", [StringComparison]::OrdinalIgnoreCase)
    if (-not $allowed -or $Target -notmatch '^[A-Za-z0-9_.+ \\-]+$' -or
        ($Target -split '\\' | Where-Object { $_ -in @('', '.', '..') -or $_.EndsWith('.') -or $_.EndsWith(' ') -or
            $_ -match '^(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)' }) -or
        $Target -ieq "$launcherFolder\launcher-preferences.json") {
        throw "The package lists an unexpected game file. $Target"
    }
}

function Read-PackageManifest {
    $path = Join-Path $PSScriptRoot 'payload-manifest.json'
    $item = Assert-PlainPath $path $false
    if ($null -eq $item -or $item.Length -gt 4194304) {
        throw 'The package manifest is missing or invalid. Run the complete setup executable again.'
    }
    $manifest = [IO.File]::ReadAllText($path) | ConvertFrom-Json
    if ($manifest.schema -ne 'stars-full-package-v1' -or $manifest.packageVersion -ne '0.14.6' -or
        $manifest.gameAssemblySha256 -notmatch '^[0-9a-fA-F]{64}$' -or -not $manifest.files) {
        throw 'The package manifest is missing or invalid. Run the complete setup executable again.'
    }
    $manifest
}

function Get-PayloadFiles($Manifest) {
    $seen = @{}
    foreach ($file in $Manifest.files) {
        $relative = [string]$file.path
        if ($relative.StartsWith('Game Files/', [StringComparison]::Ordinal) -and -not $relative.Contains('\')) {
            $target = $relative.Substring('Game Files/'.Length).Replace('/', '\')
        } else {
            throw "The package lists an unexpected file. $relative"
        }
        Assert-TargetName $target
        if ($seen.ContainsKey($target) -or $file.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
            throw "The package manifest contains an invalid entry. $relative"
        }
        $seen[$target] = $true
        [pscustomobject]@{
            Target = $target; Source = Join-Path $PSScriptRoot $relative; Hash = [string]$file.sha256
        }
    }
    $required = @('winhttp.dll', 'doorstop_config.ini', 'dotnet\coreclr.dll', 'BepInEx\core\BepInEx.Unity.IL2CPP.dll')
    $required += $pluginNames | ForEach-Object { "$pluginFolder\$_" }
    $required += @($activeAtb, "BepInEx\plugins\$fixDll", "$launcherFolder\$launcherExe")
    $required += $configNames | ForEach-Object { "BepInEx\config\$_" }
    $required += @("$supportFolder\Install.ps1", "$supportFolder\Restore.ps1", "$supportFolder\Restore.cmd")
    foreach ($name in $required) {
        if (-not $seen.ContainsKey($name)) { throw "The package manifest is missing a required file. $name" }
    }
}

function Get-InstallPlan([object[]]$Files, [string]$Root) {
    $plan = New-Object 'System.Collections.Generic.List[object]'
    $includeBattle = $IncludeAtb
    $parkBattle = $false
    $repairLoader = $false
    if ($Mode -eq 'Repair') {
        $repairLoader = Test-BundledLoaderLayout $Files $Root
        $activeBattle = $null -ne (Assert-PlainPath (Join-Path $Root $activeAtb) $false)
        $parkBattle = -not $activeBattle -and -not $IncludeAtb -and
            $null -ne (Assert-PlainPath (Join-Path $Root $dormantAtb) $false)
        $includeBattle = $includeBattle -or $activeBattle -or $parkBattle -or
            $null -ne (Assert-PlainPath (Join-Path $Root $atbConfig) $false)
    }
    foreach ($file in $Files) {
        $target = $file.Target
        $source = $file.Source
        if ($null -eq (Assert-PlainPath $source $false) -or (Get-Hash $source) -ine $file.Hash) {
            throw "A packaged file is missing or damaged. Download and run the complete installer again. $target"
        }
        if (-not $includeBattle -and $target -in @($activeAtb, $atbConfig)) { continue }
        if ($parkBattle -and $target -eq $activeAtb) { $target = $dormantAtb }
        $destination = Join-Path $Root $target
        $existing = Assert-PlainPath $destination $false
        $originalHash = $null
        if ($null -ne $existing) {
            if ($target -eq 'changelog.txt' -or $target -like 'BepInEx\config\*.cfg') {
                continue
            }
            $originalHash = Get-Hash $destination
            if ($originalHash -ieq $file.Hash) { continue }
            if ($target -eq 'doorstop_config.ini' -and (Test-DoorstopConfig $destination)) { continue }
            if (-not $repairLoader -and $target -notin @($activeAtb, $dormantAtb, "BepInEx\plugins\$fixDll") -and
                -not $target.StartsWith("$launcherFolder\", [StringComparison]::OrdinalIgnoreCase) -and
                -not $target.StartsWith("$supportFolder\", [StringComparison]::OrdinalIgnoreCase) -and
                ($pluginNames | ForEach-Object { "$pluginFolder\$_" }) -notcontains $target) {
                throw "An existing loader file differs from the bundled BepInEx build 697. See 'The installer finds a different BepInEx runtime' in Troubleshooting.txt for the backup and reinstall steps. $destination"
            }
        }
        $plan.Add([pscustomobject]@{
            Target = $target; Source = $source; Hash = $file.Hash; OriginalHash = $originalHash
        })
    }
    foreach ($legacy in @("$pluginFolder\$legacyDll") + $retiredSupportFiles) {
        $existing = Join-Path $Root $legacy
        if ($null -ne (Assert-PlainPath $existing $false)) {
            $plan.Add([pscustomobject]@{ Target=$legacy; Source=$null; Hash=$null; OriginalHash=Get-Hash $existing })
        }
    }
    # Keep entry binaries last so their dependencies and fresh settings are ready first.
    $plan | Sort-Object { $_.Target -in @("$pluginFolder\SuikodenRandomizer.dll", $activeAtb, $dormantAtb, "$launcherFolder\$launcherExe") }
}

function Write-TransactionRecord([string]$Path, $Record) {
    # Readers see the previous intact receipt or the new one, even across an interruption.
    $pending = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $bytes = [Text.Encoding]::UTF8.GetBytes(($Record | ConvertTo-Json -Depth 6))
    $file = [IO.File]::Open($pending, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $file.Write($bytes, 0, $bytes.Length); $file.Flush($true) } finally { $file.Dispose() }
    if ([IO.File]::Exists($Path)) { [IO.File]::Replace($pending, $Path, [NullString]::Value) }
    else { [IO.File]::Move($pending, $Path) }
}

function Install-Plan([object[]]$Plan, [string]$Root, [string]$ExpectedBuildHash) {
    $backupRoot = Join-Path $Root 'SRR-Backups'
    $null = Assert-PlainPath $backupRoot $true
    $name = 'FullPreview-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $transaction = Join-Path $backupRoot $name
    $staged = Join-Path $transaction 'New'
    $original = Join-Path $transaction 'Original'
    $applied = New-Object 'System.Collections.Generic.List[object]'
    $stagedFiles = New-Object 'System.Collections.Generic.List[string]'
    $stagedFolders = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $changesFile = Join-Path $transaction 'Plugin changes.txt'
    $recordPath = Join-Path $transaction 'transaction.json'
    $record = $null
    try {
        foreach ($entry in $Plan) {
            if ($null -eq $entry.Source) { continue }
            $copy = Join-Path $staged $entry.Target
            $folder = [IO.Path]::GetDirectoryName($copy)
            $null = Assert-PlainPath $folder $true
            $null = [IO.Directory]::CreateDirectory($folder)
            for ($dir = $folder; $dir.Length -ge $staged.Length; $dir = [IO.Path]::GetDirectoryName($dir)) {
                $null = $stagedFolders.Add($dir)
            }
            $stagedFiles.Add($copy)
            [IO.File]::Copy($entry.Source, $copy)
            if ((Get-Hash $copy) -ine $entry.Hash) { throw "A staged file failed verification. $($entry.Target)" }
        }

        Assert-GameClosed
        Assert-NoDuplicatePlugins $Root
        $changes = @(
            foreach ($entry in ($Plan | Where-Object { $_.Target -like 'BepInEx\plugins\*' })) {
                $action = if ($null -eq $entry.Source) { 'Retired' }
                          elseif ($null -ne $entry.OriginalHash) { 'Replaced' }
                          else { 'Added' }
                "$action  $($entry.Target)"
            }
        )
        $stagedChanges = Join-Path $staged 'Plugin changes.txt'
        if ($changes.Count -gt 0) {
            $null = Assert-PlainPath $staged $true
            $null = [IO.Directory]::CreateDirectory($staged)
            $null = $stagedFolders.Add($staged)
            $stagedFiles.Add($stagedChanges)
            [IO.File]::WriteAllLines($stagedChanges, [string[]]$changes)
        }
        $null = [IO.Directory]::CreateDirectory($transaction)
        # The installed restore scripts may themselves be part of an interrupted move.
        $recoveryScript = Join-Path $transaction 'Recover.ps1'
        [IO.File]::Copy($PSCommandPath, $recoveryScript)
        $record = [ordered]@{
            schema = 'stars-of-destiny-full-install-v1'; gamePath = $Root
            gameAssemblySha256 = $ExpectedBuildHash; status = 'applying'
            createdUtc = [DateTime]::UtcNow.ToString('o'); operations = @(
                foreach ($entry in $Plan) {
                    [ordered]@{ target = $entry.Target; originalHash = $entry.OriginalHash; newHash = $entry.Hash }
                }
            )
        }
        Write-TransactionRecord $recordPath $record
        Write-Host "If this operation is interrupted, run Restore.cmd with -TransactionPath `"$transaction`"."
        Write-Host "Standalone recovery: powershell -ExecutionPolicy Bypass -File `"$recoveryScript`" -GamePath `"$Root`" -RestoreTransaction `"$transaction`""
        foreach ($entry in $Plan) {
            $target = Join-Path $Root $entry.Target
            $backup = Join-Path $original $entry.Target
            $existing = Assert-PlainPath $target $false
            if (($null -eq $existing) -ne ($null -eq $entry.OriginalHash) -or
                ($null -ne $existing -and (Get-Hash $target) -ine $entry.OriginalHash)) {
                throw "This file changed during setup. Close other installers and retry. $target"
            }
            $operation = [pscustomobject]@{ Target = $target; Backup = $backup; Moved = $false; Installed = $false }
            $applied.Add($operation)
            if ($null -ne $existing) {
                $folder = [IO.Path]::GetDirectoryName($backup)
                $null = Assert-PlainPath $folder $true
                $null = [IO.Directory]::CreateDirectory($folder)
                [IO.File]::Move($target, $backup)
                $operation.Moved = $true
            }
            if ($null -ne $entry.Source) {
                $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
                [IO.File]::Move((Join-Path $staged $entry.Target), $target)
                $operation.Installed = $true
            }
        }
        if ($changes.Count -gt 0) { [IO.File]::Move($stagedChanges, $changesFile) }
        $record.status = 'complete'
        Write-TransactionRecord $recordPath $record
    } catch {
        $failure = $_
        $restoreFailed = $false
        for ($i = $applied.Count - 1; $i -ge 0; $i--) {
            $operation = $applied[$i]
            try {
                $null = Assert-PlainPath $operation.Target $false
                if ($operation.Installed) { [IO.File]::Delete($operation.Target) }
                if ($operation.Moved) {
                    $null = Assert-PlainPath $operation.Backup $false
                    [IO.File]::Move($operation.Backup, $operation.Target)
                }
            } catch {
                $restoreFailed = $true
                Write-Warning $_.Exception.Message
            }
        }
        if ($restoreFailed) { throw "Setup stopped and some recovery files still need to be restored from $original. $failure" }
        if ($null -ne $record) {
            $record.status = 'rolled-back'
            try { Write-TransactionRecord $recordPath $record }
            catch { Write-Warning 'Could not update the rollback receipt. The previous files were restored.' }
        }
        throw $failure
    } finally {
        foreach ($file in $stagedFiles) {
            try {
                if ($null -ne (Assert-PlainPath $file $false)) { [IO.File]::Delete($file) }
            } catch { Write-Warning "Could not remove staged file $file." }
        }
        foreach ($dir in ($stagedFolders | Sort-Object Length -Descending)) {
            try {
                if ($null -ne (Assert-PlainPath $dir $true) -and -not [IO.Directory]::EnumerateFileSystemEntries($dir).GetEnumerator().MoveNext()) {
                    [IO.Directory]::Delete($dir)
                }
            } catch { Write-Warning "Could not remove staging folder $dir." }
        }
    }
    if ([IO.Directory]::Exists($original)) {
        Write-Host "Previous plugin files are in $original"
    }
    if ([IO.File]::Exists($changesFile)) {
        Write-Host "Plugin change list is in $changesFile"
    } elseif ([IO.Directory]::Exists($transaction) -and -not [IO.Directory]::EnumerateFileSystemEntries($transaction).GetEnumerator().MoveNext()) {
        [IO.Directory]::Delete($transaction)
    }
    Write-Host "Verified transaction record $recordPath"
    return $transaction
}

function Get-RestorePlan([string]$Transaction, [string]$Root, [string]$CurrentGameHash) {
    $transaction = [IO.Path]::GetFullPath($Transaction.Trim().Trim('"'))
    $backupRoot = [IO.Path]::GetFullPath((Join-Path $Root 'SRR-Backups'))
    if ([IO.Path]::GetDirectoryName($transaction) -ine $backupRoot -or
        [IO.Path]::GetFileName($transaction) -notmatch '^FullPreview-[A-Za-z0-9-]+$') {
        throw 'Choose one FullPreview transaction folder directly inside this game\SRR-Backups.'
    }
    $null = Assert-PlainPath $transaction $true
    $path = Join-Path $transaction 'transaction.json'
    if ($null -eq (Assert-PlainPath $path $false)) { throw 'The selected backup has no transaction record.' }
    if ((Get-Item -LiteralPath $path).Length -gt 4194304) { throw 'The transaction record exceeds its supported size.' }
    $record = [IO.File]::ReadAllText($path) | ConvertFrom-Json
    if ($record.schema -ne 'stars-of-destiny-full-install-v1' -or $record.status -notin @('complete', 'applying') -or
        $record.gamePath -ine $Root -or $record.gameAssemblySha256 -notmatch '^[0-9a-fA-F]{64}$' -or -not $record.operations) {
        throw 'This transaction is invalid, already rolled back, or belongs to a different game folder.'
    }
    $changedBuild = $CurrentGameHash -ine $record.gameAssemblySha256
    $plan = New-Object 'System.Collections.Generic.List[object]'
    $seen = @{}
    foreach ($entry in $record.operations) {
        $target = [string]$entry.target
        if ($target -ne "$pluginFolder\$legacyDll") { Assert-TargetName $target }
        if ($seen.ContainsKey($target) -or $entry.originalHash -eq $entry.newHash -or
            ($null -ne $entry.originalHash -and $entry.originalHash -notmatch '^[0-9a-fA-F]{64}$') -or
            ($null -ne $entry.newHash -and $entry.newHash -notmatch '^[0-9a-fA-F]{64}$')) { throw 'The transaction contains an invalid operation.' }
        $seen[$target] = $true
        $current = Join-Path $Root $target
        $item = Assert-PlainPath $current $false
        $currentHash = if ($null -ne $item) { Get-Hash $current }
        # An interrupted operation may still be untouched, already rolled back, or
        # between the rename into Original and the rename out of New.
        if ($currentHash -eq $entry.originalHash) { continue }
        $keepBackup = $changedBuild -and $null -ne $entry.originalHash -and
            ($target -match '^(?:BepInEx\\(?:plugins|core)|dotnet)\\' -or
             $target -in @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version'))
        $betweenMoves = $record.status -eq 'applying' -and $null -eq $item -and $null -ne $entry.originalHash
        if ($currentHash -ne $entry.newHash -and -not $betweenMoves -and -not ($keepBackup -and $null -eq $item)) {
            throw "A file changed after installation. Restore stopped without changing files. $current"
        }
        $source = $null
        if ($null -ne $entry.originalHash) {
            $source = Join-Path (Join-Path $transaction 'Original') $target
            if ($null -eq (Assert-PlainPath $source $false) -or (Get-Hash $source) -ine $entry.originalHash) {
                throw "A previous file is missing or changed in the backup. $target"
            }
        }
        $restoreHash = $entry.originalHash
        if ($keepBackup) {
            Write-Warning "Game build changed. Keeping the previous loader or plugin in its backup: $source"
            if ($null -eq $item) { continue }
            $source = $null
            $restoreHash = $null
        }
        $plan.Add([pscustomobject]@{ Target = $target; Source = $source; Hash = $restoreHash; OriginalHash = $currentHash })
    }
    [pscustomobject]@{ Plan = $plan.ToArray(); GameAssemblySha256 = $record.gameAssemblySha256 }
}

function Enter-MaintenanceLock([string]$Exe) {
    Assert-GameClosed
    $script:installerMutex = [Threading.Mutex]::new($false, 'Local\StarsOfDestinyInstaller')
    try { $script:ownsMutex = $script:installerMutex.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $script:ownsMutex = $true }
    if (-not $script:ownsMutex) { throw 'Close the other Stars of Destiny installer before continuing.' }
    $null = Assert-PlainPath $Exe $false
    $script:lock = [IO.File]::Open($Exe, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    Assert-GameClosed
}

$lock = $null
$installerMutex = $null
$ownsMutex = $false
try {
    if (-not [Environment]::Is64BitOperatingSystem) { throw 'This package requires 64-bit Windows.' }
    $folder = (Get-GameFolder).TrimEnd('\', '/')
    $exe = Join-Path $folder $gameExe
    $assembly = Join-Path $folder 'GameAssembly.dll'
    if ($null -eq (Assert-PlainPath $exe $false) -or $null -eq (Assert-PlainPath $assembly $false)) {
        throw "Choose the folder containing $gameExe and GameAssembly.dll."
    }
    Assert-GameClosed
    Write-Host "Game folder $folder"
    if ($Mode -ne 'Install') {
        if ($RestoreTransaction) { throw 'Choose Repair, Uninstall, or transaction restore separately.' }
        $maintenance = Join-Path $PSScriptRoot 'Maintenance.ps1'
        if ($null -eq (Assert-PlainPath $maintenance $false)) { throw 'Run the complete setup executable for maintenance.' }
        . $maintenance
    }
    if ($Mode -eq 'Uninstall') {
        $payload = @(Get-PayloadFiles (Read-PackageManifest))
        if (-not $NonInteractive -and -not $CheckOnly -and -not $RemoveConfig) {
            $RemoveConfig = (Read-Host 'Keep configuration and campaign bindings? [Y/n]').Trim() -match '^(n|no)$'
        }
        $plan = Get-UninstallPlan $payload $folder
        Write-Host "Files to uninstall  $($plan.Files.Count)"
        if ($RemoveConfig) { Write-Host 'Remove Stars of Destiny settings and campaign bindings.' }
        else { Write-Host 'Keep settings and campaign bindings.' }
        if ($plan.KeepLoader) { Write-Host 'Keep any shared or separately installed BepInEx loader.' }
        if ($CheckOnly) {
            $plan.Files | ForEach-Object { Write-Host "  $_" }
            Write-Host 'Check complete.'
            exit 0
        }
        if (-not $plan.Files.Count) { Write-Host 'No installed Stars of Destiny files were found.'; exit 0 }
        if (-not $NonInteractive -and (Read-Host 'Uninstall from this game folder? [y/N]').Trim() -notmatch '^(y|yes)$') {
            Write-Host 'Uninstall cancelled.'
            exit 0
        }
        Enter-MaintenanceLock $exe
        $plan = Get-UninstallPlan $payload $folder
        Remove-SetupFiles $plan $folder
        if ($RemoveShortcuts) {
            try { Remove-SetupShortcuts $folder }
            catch { Write-Warning "Could not remove a Start menu shortcut. $($_.Exception.Message)" }
        }
        Write-Host 'Stars of Destiny uninstalled.'
        exit 0
    }
    Assert-NoDuplicatePlugins $folder
    if ($RestoreTransaction) {
        $expectedGameHash = Get-Hash $assembly
        $restore = Get-RestorePlan $RestoreTransaction $folder $expectedGameHash
        $plan = $restore.Plan
        $receiptGameHash = $restore.GameAssemblySha256
        Write-Host 'Restore previous installation files from the selected verified transaction.'
    } else {
        $manifest = Read-PackageManifest
        $expectedGameHash = [string]$manifest.gameAssemblySha256
        $receiptGameHash = $expectedGameHash
        Assert-GameBuild $assembly $expectedGameHash
        $payload = @(Get-PayloadFiles $manifest)
        $plan = @(Get-InstallPlan $payload $folder)
        Write-Host 'Required components: BepInEx, combined randomizer 0.14.6, launcher, and Suikoden Fix.'
        if ($Mode -eq 'Repair') { Write-Host 'Repairing packaged files and any installed S1 battle preview.' }
        elseif ($IncludeAtb) { Write-Host 'Optional S1 ATB / CTB selected. Its fresh configuration is disabled.' }
        else { Write-Host 'Optional S1 ATB / CTB not selected. Any existing ATB files are preserved.' }
        Write-Host 'Existing settings are preserved. Fresh settings start with chest shuffle and ATB disabled.'
    }
    $loaderCount = @($plan | Where-Object { $_.Target -match '^(?:dotnet|BepInEx\\core)\\' -or $_.Target -in @('winhttp.dll','doorstop_config.ini','.doorstop_version','changelog.txt') }).Count
    $launcherCount = @($plan | Where-Object { $_.Target.StartsWith("$launcherFolder\", [StringComparison]::OrdinalIgnoreCase) }).Count
    Write-Host "BepInEx loader files to change  $loaderCount"
    Write-Host "Launcher files to change  $launcherCount"
    foreach ($entry in ($plan | Where-Object { $_.Target -like 'BepInEx\plugins\*' -or $_.Target -like 'BepInEx\config\*' })) {
        $action = if ($null -eq $entry.Source) { 'Retire to backup' }
                  elseif ($null -ne $entry.OriginalHash) { 'Back up and update' }
                  else { 'Add' }
        Write-Host "  $action $($entry.Target)"
    }
    if ($CheckOnly) {
        $plan | ForEach-Object { Write-Host "  $($_.Target)" }
        Write-Host 'Check complete.'
        exit 0
    }
    if ($plan.Count -eq 0) {
        Write-Host 'No file changes are required.'
    } else {
        if (-not $NonInteractive -and (Read-Host "Apply changes to this game folder? [y/N]").Trim() -notmatch '^(y|yes)$') {
            Write-Host 'Setup cancelled.'
            exit 0
        }
        Enter-MaintenanceLock $exe
        Assert-GameBuild $assembly $expectedGameHash
        Assert-NoDuplicatePlugins $folder
        $transaction = Install-Plan $plan $folder $receiptGameHash
        if ($RestoreTransaction -and $expectedGameHash -ine $receiptGameHash) {
            Write-Host 'Recovery complete. Previous loader and plugin binaries for the old game build remain in their backups.'
        } elseif ($RestoreTransaction) { Write-Host 'Previous installation files restored.' }
        elseif ($Mode -eq 'Repair') { Write-Host 'Stars of Destiny repaired.' }
        else { Write-Host 'Stars of Destiny Full Preview 0.14.6 installed.' }
        Write-Host "Undo this transaction using StarsOfDestiny Setup\Restore.cmd -TransactionPath `"$transaction`""
    }
    if (-not [IO.File]::Exists((Join-Path $folder 'BepInEx\interop\GSD1.dll')) -or
        -not [IO.File]::Exists((Join-Path $folder 'BepInEx\interop\GSD2.dll'))) {
        Write-Host 'Launch from Steam with an internet connection so BepInEx can generate its interop files.'
    } else {
        Write-Host 'Launch the game normally from Steam.'
    }
    exit 0
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
} finally {
    if ($null -ne $lock) { $lock.Dispose() }
    if ($ownsMutex) { $installerMutex.ReleaseMutex() }
    if ($null -ne $installerMutex) { $installerMutex.Dispose() }
}
