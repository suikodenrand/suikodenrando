function Get-PlainTreeItems([string]$Path) {
    if ($null -eq (Assert-PlainPath $Path $true)) { return }
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($Path)
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Setup cannot use a linked path. $($item.FullName)"
            }
            $item
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
        }
    }
}

function Test-BundledLoaderLayout([object[]]$Files, [string]$Root) {
    $loader = @{}
    foreach ($file in $Files) { $loader[$file.Target] = $file.Hash }
    $rootPrefix = $Root + '\'
    foreach ($name in @('BepInEx\core', 'dotnet')) {
        $path = Join-Path $Root $name
        if ($null -eq (Assert-PlainPath $path $true)) { continue }
        foreach ($item in Get-ChildItem -LiteralPath $path -Force) {
            if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
                -not $loader.ContainsKey($item.FullName.Substring($rootPrefix.Length))) { return $false }
        }
    }
    # A version-specific DLL can identify our loader even if another file is damaged.
    $corePresent = $false
    foreach ($name in @('BepInEx.Core.dll', 'BepInEx.Preloader.Core.dll', 'BepInEx.Unity.IL2CPP.dll')) {
        $relative = "BepInEx\core\$name"
        $path = Join-Path $Root $relative
        if ($null -eq (Assert-PlainPath $path $false)) { continue }
        $corePresent = $true
        if ((Get-Hash $path) -eq $loader[$relative]) { return $true }
    }
    if ($corePresent) { return $false }
    $proxy = Join-Path $Root 'winhttp.dll'
    ($null -eq (Assert-PlainPath $proxy $false)) -or (Get-Hash $proxy) -eq $loader['winhttp.dll']
}

function Get-UninstallPlan([object[]]$Payload, [string]$Root) {
    $markers = @("$pluginFolder\SuikodenRandomizer.dll", "$pluginFolder\$legacyDll",
        "$launcherFolder\$launcherExe", "$supportFolder\Install.ps1", $activeAtb, $dormantAtb)
    $installed = $false
    foreach ($name in $markers) {
        if ($null -ne (Assert-PlainPath (Join-Path $Root $name) $false)) { $installed = $true; break }
    }
    if (-not $installed) {
        return [pscustomobject]@{ Files = @(); Folders = @(); KeepLoader = $true }
    }

    $knownPlugins = @("$pluginFolder\$legacyDll", $activeAtb, "BepInEx\plugins\$fixDll")
    $knownPlugins += $pluginNames | ForEach-Object { "$pluginFolder\$_" }
    $rootPrefix = $Root + '\'
    $keepLoader = -not (Test-BundledLoaderLayout $Payload $Root)
    foreach ($name in @('BepInEx\plugins', 'BepInEx\patchers')) {
        $items = Get-PlainTreeItems (Join-Path $Root $name)
        foreach ($item in $items) {
            if (-not $item.PSIsContainer -and $item.Extension -ieq '.dll' -and
                $item.FullName.Substring($rootPrefix.Length) -notin $knownPlugins) {
                $keepLoader = $true
            }
        }
    }

    $targets = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $folders = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $Payload) {
        $name = $file.Target
        $loaderFile = $name -in @('winhttp.dll', '.doorstop_version', 'doorstop_config.ini', 'changelog.txt') -or
            $name -match '^(?:BepInEx\\core|dotnet)\\'
        if (($loaderFile -and $keepLoader) -or (-not $RemoveConfig -and $name -like 'BepInEx\config\*')) { continue }
        $path = Join-Path $Root $name
        if ($name -ieq 'changelog.txt' -and $null -ne (Assert-PlainPath $path $false) -and
            (Get-Hash $path) -ine $file.Hash) { continue }
        $null = $targets.Add($path)
    }

    $additionalFiles = @($dormantAtb, "$pluginFolder\$legacyDll") + $retiredSupportFiles
    $generatedFolders = @()
    if ($RemoveConfig) {
        $additionalFiles += @("$launcherFolder\launcher-preferences.json",
            'BepInEx\config\s1-ctb-tuning.json', 'BepInEx\config\s1-ctb-last-party.json')
        $generatedFolders += @('BepInEx\config\SuikodenRandomizer', 'BepInEx\config\StarsOfDestiny.Launcher')
    }
    if (-not $keepLoader) {
        if ($RemoveConfig) { $additionalFiles += 'BepInEx\config\BepInEx.cfg' }
        $additionalFiles += @('BepInEx\LogOutput.log', 'BepInEx\ErrorLog.log')
        $generatedFolders += @('BepInEx\cache', 'BepInEx\interop', 'BepInEx\unity-libs', 'BepInEx\dummy')
        $null = $folders.Add((Join-Path $Root 'BepInEx\patchers'))
        foreach ($item in Get-ChildItem -LiteralPath $Root -Force -File) {
            if ($item.Name -match '^preloader_[0-9]{8}_[0-9]{6}_[0-9]{3}\.log$') { $null = $targets.Add($item.FullName) }
        }
    }
    foreach ($relative in $additionalFiles) { $null = $targets.Add((Join-Path $Root $relative)) }
    foreach ($relative in $generatedFolders) {
        $path = Join-Path $Root $relative
        $null = $folders.Add($path)
        foreach ($item in Get-PlainTreeItems $path) {
            if ($item.PSIsContainer) { $null = $folders.Add($item.FullName) }
            else { $null = $targets.Add($item.FullName) }
        }
    }

    $existing = foreach ($path in $targets) {
        if (-not $path.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Uninstall target is outside the game folder. $path"
        }
        for ($directory = [IO.Path]::GetDirectoryName($path); $directory -ine $Root; $directory = [IO.Path]::GetDirectoryName($directory)) {
            $null = $folders.Add($directory)
        }
        if ($null -ne (Assert-PlainPath $path $false)) { $path }
    }
    # Disable the loader first when it belongs to this installation. Leave a
    # setup marker until the other files are removed so interrupted removal can be retried.
    $proxy = Join-Path $Root 'winhttp.dll'
    $marker = Join-Path $Root "$supportFolder\Install.ps1"
    $ordered = @($existing | Sort-Object { if ($_ -eq $proxy) { 0 } elseif ($_ -eq $marker) { 2 } else { 1 } })
    [pscustomobject]@{ Files = $ordered; Folders = @($folders | Sort-Object Length -Descending); KeepLoader = $keepLoader }
}

function Remove-SetupFiles($Plan, [string]$Root) {
    foreach ($path in $Plan.Files) {
        if ($null -ne (Assert-PlainPath $path $false)) { [IO.File]::Delete($path) }
    }
    $rootPrefix = $Root + '\'
    foreach ($path in $Plan.Folders) {
        if (-not $path.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Uninstall target is outside the game folder. $path"
        }
        if ($null -ne (Assert-PlainPath $path $true) -and -not [IO.Directory]::EnumerateFileSystemEntries($path).GetEnumerator().MoveNext()) {
            [IO.Directory]::Delete($path)
        }
    }
}

function Remove-SetupShortcuts([string]$Root) {
    $programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    if (-not $programs) { return }
    $directory = Join-Path $programs 'Stars of Destiny'
    if ($null -eq (Assert-PlainPath $directory $true)) { return }
    $targets = @{
        'Stars of Destiny Launcher.lnk' = Join-Path $Root "$launcherFolder\$launcherExe"
        'Setup and recovery.lnk' = Join-Path $Root $supportFolder
    }
    $shell = New-Object -ComObject WScript.Shell
    foreach ($name in $targets.Keys) {
        $path = Join-Path $directory $name
        if ($null -ne (Assert-PlainPath $path $false) -and $shell.CreateShortcut($path).TargetPath -ieq $targets[$name]) {
            [IO.File]::Delete($path)
        }
    }
    if (-not [IO.Directory]::EnumerateFileSystemEntries($directory).GetEnumerator().MoveNext()) {
        [IO.Directory]::Delete($directory)
    }
}
