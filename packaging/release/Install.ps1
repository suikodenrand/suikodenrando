#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$GamePath,
    [switch]$CheckOnly,
    [string]$RestoreTransaction,
    [switch]$IncludeAtb,
    [switch]$NonInteractive
)

$ErrorActionPreference = 'Stop'
$gameExe = 'Suikoden I and II HD Remaster.exe'
$pluginFolder = 'BepInEx\plugins\SuikodenRandomizer'
$atbDll = 'SuikodenRandomizer.S1BattlePreview.dll'
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
    foreach ($name in @([IO.Path]::GetFileNameWithoutExtension($gameExe), [IO.Path]::GetFileNameWithoutExtension($launcherExe))) {
        $running = [Diagnostics.Process]::GetProcessesByName($name)
        try {
            if ($running.Length -gt 0) { throw 'Close Suikoden I and II HD Remaster and Stars of Destiny Launcher before running setup.' }
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
        "BepInEx\plugins\$atbDll", "BepInEx\plugins\$fixDll") -or
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

function Get-InstallPlan($Manifest, [string]$Root) {
    $plan = New-Object 'System.Collections.Generic.List[object]'
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
        $source = Join-Path $PSScriptRoot $relative
        if ($null -eq (Assert-PlainPath $source $false) -or (Get-Hash $source) -ine $file.sha256) {
            throw "A preview file is missing or damaged. Download and run the complete installer again. $relative"
        }
        # Verify every bundled input, including the optional component, before
        # filtering. Omitting ATB never removes or edits an existing installation.
        if (-not $IncludeAtb -and $target -in @("BepInEx\plugins\$atbDll", $atbConfig)) { continue }
        $destination = Join-Path $Root $target
        $existing = Assert-PlainPath $destination $false
        $originalHash = $null
        if ($null -ne $existing) {
            if ($target -eq 'changelog.txt' -or $target -like 'BepInEx\config\*.cfg') {
                continue
            }
            $originalHash = Get-Hash $destination
            if ($originalHash -ieq $file.sha256) { continue }
            if ($target -eq 'doorstop_config.ini' -and (Test-DoorstopConfig $destination)) { continue }
            if ($target -ine "BepInEx\plugins\$atbDll" -and $target -ine "BepInEx\plugins\$fixDll" -and
                -not $target.StartsWith("$launcherFolder\", [StringComparison]::OrdinalIgnoreCase) -and
                -not $target.StartsWith("$supportFolder\", [StringComparison]::OrdinalIgnoreCase) -and
                ($pluginNames | ForEach-Object { "$pluginFolder\$_" }) -notcontains $target) {
                throw "An existing loader file differs from the bundled BepInEx build 697. See 'The installer finds a different BepInEx runtime' in Troubleshooting.txt for the backup and reinstall steps. $destination"
            }
        }
        $plan.Add([pscustomobject]@{
            Target = $target; Source = $source; Hash = [string]$file.sha256; OriginalHash = $originalHash
        })
    }
    $required = @('winhttp.dll', 'doorstop_config.ini', 'dotnet\coreclr.dll', 'BepInEx\core\BepInEx.Unity.IL2CPP.dll')
    $required += $pluginNames | ForEach-Object { "$pluginFolder\$_" }
    $required += @("BepInEx\plugins\$atbDll", "BepInEx\plugins\$fixDll", "$launcherFolder\$launcherExe")
    $required += $configNames | ForEach-Object { "BepInEx\config\$_" }
    $required += @("$supportFolder\Install.ps1", "$supportFolder\Restore.ps1", "$supportFolder\Restore.cmd")
    foreach ($name in $required) {
        if (-not $seen.ContainsKey($name)) { throw "The package manifest is missing a required file. $name" }
    }
    $legacy = "$pluginFolder\$legacyDll"
    $legacyPath = Join-Path $Root $legacy
    if ($null -ne (Assert-PlainPath $legacyPath $false)) {
        $plan.Add([pscustomobject]@{
            Target = $legacy; Source = $null; Hash = $null; OriginalHash = Get-Hash $legacyPath
        })
    }
    # Retire only the three former packaged source archives. The original
    # bytes are journaled outside the active support tree for exact restore.
    foreach ($archive in @('BepInEx-source-5362580.zip', 'Il2CppInterop-source-56bf1da.zip', 'UnityDoorstop-source-v4.3.0.zip')) {
        $oldSource = "$supportFolder\Licenses\Source\$archive"
        $oldSourcePath = Join-Path $Root $oldSource
        if ($null -ne (Assert-PlainPath $oldSourcePath $false)) {
            $plan.Add([pscustomobject]@{ Target=$oldSource; Source=$null; Hash=$null; OriginalHash=Get-Hash $oldSourcePath })
        }
    }
    # The former manual-install wrapper is not used by NSIS or restore.
    # Retire only this owned path, with its original bytes kept for undo.
    $oldWrapper = "$supportFolder\Install.cmd"
    $oldWrapperPath = Join-Path $Root $oldWrapper
    if ($null -ne (Assert-PlainPath $oldWrapperPath $false)) {
        $plan.Add([pscustomobject]@{ Target=$oldWrapper; Source=$null; Hash=$null; OriginalHash=Get-Hash $oldWrapperPath })
    }
    # Keep entry binaries last so their dependencies and fresh settings are ready first.
    $plan | Sort-Object { $_.Target -in @("$pluginFolder\SuikodenRandomizer.dll", "BepInEx\plugins\$atbDll", "$launcherFolder\$launcherExe") }
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
        $record = [ordered]@{
            schema = 'stars-of-destiny-full-install-v1'; gamePath = $Root
            gameAssemblySha256 = $ExpectedBuildHash; status = 'applying'
            createdUtc = [DateTime]::UtcNow.ToString('o'); operations = @(
                foreach ($entry in $Plan) {
                    [ordered]@{ target = $entry.Target; originalHash = $entry.OriginalHash; newHash = $entry.Hash }
                }
            )
        }
        [IO.File]::WriteAllText($recordPath, ($record | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
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
        [IO.File]::WriteAllText($recordPath, ($record | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
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
            try { [IO.File]::WriteAllText($recordPath, ($record | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false)) }
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

function Get-RestorePlan([string]$Transaction, [string]$Root) {
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
    if ($record.schema -ne 'stars-of-destiny-full-install-v1' -or $record.status -ne 'complete' -or
        $record.gamePath -ine $Root -or $record.gameAssemblySha256 -notmatch '^[0-9a-fA-F]{64}$' -or -not $record.operations) {
        throw 'This transaction is incomplete or belongs to a different game folder.'
    }
    Assert-GameBuild (Join-Path $Root 'GameAssembly.dll') $record.gameAssemblySha256
    $plan = New-Object 'System.Collections.Generic.List[object]'
    $seen = @{}
    foreach ($entry in $record.operations) {
        $target = [string]$entry.target
        if ($target -ne "$pluginFolder\$legacyDll") { Assert-TargetName $target }
        if ($seen.ContainsKey($target) -or ($null -ne $entry.originalHash -and $entry.originalHash -notmatch '^[0-9a-fA-F]{64}$') -or
            ($null -ne $entry.newHash -and $entry.newHash -notmatch '^[0-9a-fA-F]{64}$')) { throw 'The transaction contains an invalid operation.' }
        $seen[$target] = $true
        $current = Join-Path $Root $target
        $item = Assert-PlainPath $current $false
        if (($null -eq $item) -ne ($null -eq $entry.newHash) -or
            ($null -ne $item -and (Get-Hash $current) -ine $entry.newHash)) {
            throw "A file changed after installation. Restore stopped without changing files. $current"
        }
        $source = $null
        if ($null -ne $entry.originalHash) {
            $source = Join-Path (Join-Path $transaction 'Original') $target
            if ($null -eq (Assert-PlainPath $source $false) -or (Get-Hash $source) -ine $entry.originalHash) {
                throw "A previous file is missing or changed in the backup. $target"
            }
        }
        $plan.Add([pscustomobject]@{ Target = $target; Source = $source; Hash = $entry.originalHash; OriginalHash = $entry.newHash })
    }
    $plan
}

$lock = $null
$installerMutex = $null
$ownsMutex = $false
try {
    if (-not [Environment]::Is64BitOperatingSystem) { throw 'This package requires 64-bit Windows.' }
    $folder = Get-GameFolder
    $exe = Join-Path $folder $gameExe
    $assembly = Join-Path $folder 'GameAssembly.dll'
    if ($null -eq (Assert-PlainPath $exe $false) -or $null -eq (Assert-PlainPath $assembly $false)) {
        throw "Choose the folder containing $gameExe and GameAssembly.dll."
    }
    Assert-GameClosed
    Write-Host "Game folder $folder"
    Assert-NoDuplicatePlugins $folder
    if ($RestoreTransaction) {
        $plan = @(Get-RestorePlan $RestoreTransaction $folder)
        $expectedGameHash = Get-Hash $assembly
        Write-Host 'Restore previous installation files from the selected verified transaction.'
    } else {
        $manifestPath = Join-Path $PSScriptRoot 'payload-manifest.json'
        if ($null -eq (Assert-PlainPath $manifestPath $false) -or (Get-Item -LiteralPath $manifestPath).Length -gt 4194304) {
            throw 'The package manifest is missing or invalid. Extract the complete ZIP again.'
        }
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.schema -ne 'stars-full-package-v1' -or $manifest.packageVersion -ne '0.14.4' -or
            $manifest.gameAssemblySha256 -notmatch '^[0-9a-fA-F]{64}$' -or -not $manifest.files) {
            throw 'The package manifest is missing or invalid. Extract the complete ZIP again.'
        }
        $expectedGameHash = [string]$manifest.gameAssemblySha256
        Assert-GameBuild $assembly $expectedGameHash
        $plan = @(Get-InstallPlan $manifest $folder)
        Write-Host 'Required components: BepInEx, combined randomizer 0.14.4, launcher, and Suikoden Fix.'
        if ($IncludeAtb) { Write-Host 'Optional S1 ATB selected. Its fresh configuration is disabled.' }
        else { Write-Host 'Optional S1 ATB not selected. Any existing ATB files are preserved.' }
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
        Write-Host 'The complete preview package is already installed.'
    } else {
        if (-not $NonInteractive -and (Read-Host 'Install into this game folder? [y/N]').Trim() -notmatch '^(y|yes)$') {
            Write-Host 'Setup cancelled.'
            exit 0
        }
        $installerMutex = [Threading.Mutex]::new($true, 'Local\StarsOfDestinyInstaller', [ref]$ownsMutex)
        if (-not $ownsMutex) { throw 'Close the other Stars of Destiny installer before continuing.' }
        Assert-GameClosed
        $null = Assert-PlainPath $exe $false
        $lock = [IO.File]::Open($exe, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        Assert-GameClosed
        Assert-GameBuild $assembly $expectedGameHash
        Assert-NoDuplicatePlugins $folder
        $transaction = Install-Plan $plan $folder $expectedGameHash
        if ($RestoreTransaction) { Write-Host 'Previous installation files restored.' }
        else { Write-Host 'Stars of Destiny Full Preview 0.14.4 installed.' }
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
