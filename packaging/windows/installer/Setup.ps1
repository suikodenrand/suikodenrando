#requires -Version 5.1
[CmdletBinding()]
param([string]$GamePath, [ValidateSet('Install','Rollback')][string]$Mode = 'Install')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'
$Root = Split-Path -Parent $PSScriptRoot
$Package = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package.json') -Raw | ConvertFrom-Json
$Cache = Join-Path $env:LOCALAPPDATA 'SRR\Installer'
$LogRoot = Join-Path $Cache 'logs'
New-Item -ItemType Directory -Path $LogRoot -Force | Out-Null
$RunTag = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$Log = Join-Path $LogRoot ($RunTag + '.log')
$Workspace = $null
$Succeeded = $false
$TranscriptStarted = $false

function Hash-File([string]$Path, [string]$Algorithm = 'SHA256') {
    return (Get-FileHash -LiteralPath $Path -Algorithm $Algorithm).Hash.ToLowerInvariant()
}
function Write-VerifiedFile([string]$Source, [string]$Target) {
    $temporary = $Target + '.srr-new-' + [Guid]::NewGuid().ToString('N')
    $previous = $Target + '.srr-old-' + [Guid]::NewGuid().ToString('N')
    try {
        Copy-Item -LiteralPath $Source -Destination $temporary
        if ((Hash-File $temporary) -ne (Hash-File $Source)) { throw 'Staged file checksum mismatch.' }
        $stream = [IO.File]::Open($temporary, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        try { $stream.Flush($true) } finally { $stream.Dispose() }
        if (Test-Path -LiteralPath $Target -PathType Leaf) {
            [IO.File]::Replace($temporary, $Target, $previous)
        } else {
            [IO.File]::Move($temporary, $Target)
        }
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
        if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Force }
    }
}
function Write-Manifest([string]$Path, [object]$Manifest) {
    $source = $Path + '.srr-manifest-' + [Guid]::NewGuid().ToString('N')
    try {
        [IO.File]::WriteAllText($source, ($Manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
        Write-VerifiedFile $source $Path
    } finally {
        if (Test-Path -LiteralPath $source) { Remove-Item -LiteralPath $source -Force }
    }
}
function Assert-GameClosed {
    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'Suikoden I and II*' })
    if ($running.Count -gt 0) { throw 'Close Suikoden completely, then run this installer again. The installer never closes the game for you.' }
}
function Resolve-GameFolder([string]$Supplied) {
    if ([string]::IsNullOrWhiteSpace($Supplied)) {
        $hint = $null
        foreach ($key in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1932640', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1932640')) {
            $item = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
            if ($null -ne $item -and $null -ne $item.PSObject.Properties['InstallLocation']) { $hint = $item.InstallLocation; break }
        }
        Add-Type -AssemblyName System.Windows.Forms
        $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
        $dialog.Description = 'Select your Suikoden I and II HD Remaster game folder (contains GameAssembly.dll).'
        $dialog.ShowNewFolderButton = $false
        if ($null -ne $hint -and (Test-Path -LiteralPath $hint -PathType Container)) { $dialog.SelectedPath = $hint }
        try {
            if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw 'Installation cancelled. No game files were changed.' }
            $Supplied = $dialog.SelectedPath
        } finally { $dialog.Dispose() }
    }
    $resolved = (Resolve-Path -LiteralPath $Supplied.Trim().Trim('"') -ErrorAction Stop).ProviderPath
    return [IO.Path]::GetFullPath($resolved).TrimEnd('\')
}
function Assert-Game([string]$Folder) {
    if (-not [Environment]::Is64BitProcess) { throw 'Use 64-bit Windows PowerShell. Start Install.cmd rather than opening the script with a 32-bit editor.' }
    $native = Join-Path $Folder 'GameAssembly.dll'
    if (-not (Test-Path -LiteralPath $native -PathType Leaf)) { throw 'GameAssembly.dll was not found in the selected folder.' }
    if ((Hash-File $native) -ne $Package.gameAssemblySha256) { throw 'This native game revision is not supported. No game files were changed.' }
    if ($Mode -eq 'Rollback') { return }
    foreach ($relative in @('core\0Harmony.dll','core\BepInEx.Core.dll','core\BepInEx.Unity.IL2CPP.dll','core\Il2CppInterop.Runtime.dll','interop\GSD1.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Folder ('BepInEx\' + $relative)) -PathType Leaf)) { throw "Required installed reference is missing: BepInEx\$relative. Start the game with your existing BepInEx once before upgrading." }
    }
    $expectedPluginDir = [IO.Path]::GetFullPath((Join-Path $Folder 'BepInEx\plugins\SuikodenRandomizer'))
    $pluginRoot = Join-Path $Folder 'BepInEx\plugins'
    if (-not (Test-Path -LiteralPath $pluginRoot -PathType Container)) { return }
    foreach ($dll in Get-ChildItem -LiteralPath $pluginRoot -Filter '*.dll' -Recurse -File) {
        if ($dll.Name -eq 'SuikodenRandomizer.dll' -and $dll.DirectoryName -ne $expectedPluginDir) { throw "Another standalone randomizer exists at $($dll.FullName). Move that duplicate outside BepInEx\plugins first." }
        if ($dll.Name -ieq 'Suikoden_Fix.dll' -and [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($dll.FullName)).Contains('StartingPartyVirtualizerPatch')) { throw 'An old embedded randomizer was detected in Suikoden Fix. Do not combine it with the standalone randomizer.' }
    }
}
function Invoke-Tool([string]$Exe, [string[]]$Arguments, [string]$WorkingDirectory) {
    # Avoid shell interpretation. Use Win32 argv quoting, including paths with spaces.
    $quoted = @($Arguments | ForEach-Object {
        '"' + ([regex]::Replace([regex]::Replace($_, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1')) + '"'
    })
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $Exe
    $start.Arguments = $quoted -join ' '
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $proc = New-Object Diagnostics.Process
    $proc.StartInfo = $start
    try {
        [void]$proc.Start()
        $stdout = $proc.StandardOutput.ReadToEndAsync()
        $stderr = $proc.StandardError.ReadToEndAsync()
        $proc.WaitForExit()
        $outText = $stdout.GetAwaiter().GetResult()
        $errText = $stderr.GetAwaiter().GetResult()
        if ($outText) { Write-Host $outText.TrimEnd() }
        if ($errText) { Write-Host $errText.TrimEnd() }
        if ($proc.ExitCode -ne 0) { throw "Tool failed with exit code $($proc.ExitCode). No update will be installed. Full output is in $Log" }
    } finally { $proc.Dispose() }
}
function Invoke-ManagedCheck([string]$Dotnet, [string]$WorkingRoot, [string]$Name, [string[]]$CheckArguments = @()) {
    # Build one graph at a time, then execute the exact produced assembly.
    # Avoid dotnet run's implicit build/restore so all gates share this policy.
    $project=Join-Path $WorkingRoot ('tests\'+$Name+'\'+$Name+'.csproj')
    Invoke-Tool $Dotnet @('build',$project,'--configuration','Release','--nologo','-m:1','-p:RestoreBuildInParallel=false','-p:BuildInParallel=false','-p:NuGetAudit=false') $WorkingRoot
    $assembly=Join-Path $WorkingRoot ('tests\'+$Name+'\bin\Release\net10.0\'+$Name+'.dll')
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw "Missing compiled check output: $Name" }
    $arguments=@($assembly)
    if ($null -ne $CheckArguments) { $arguments += $CheckArguments }
    Invoke-Tool $Dotnet $arguments $WorkingRoot
}
function Ensure-Toolchain {
    $sdk = $Package.sdk
    $downloadDir = Join-Path $Cache 'downloads'
    $toolDir = Join-Path $Cache ('dotnet-' + $sdk.version)
    $archive = Join-Path $downloadDir ('dotnet-sdk-' + $sdk.version + '-win-x64.zip')
    New-Item -ItemType Directory -Path $downloadDir -Force | Out-Null
    $verified = (Test-Path -LiteralPath $archive -PathType Leaf) -and ((Hash-File $archive 'SHA512') -eq $sdk.sha512)
    if (-not $verified) {
        Write-Host 'Downloading the portable Microsoft build tools. Nothing is installed system-wide.'
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $part = $archive + '.' + $RunTag + '.partial'
        $client = New-Object Net.WebClient
        try {
            $client.Headers.Add('User-Agent','SuikodenRandomizer-AutoInstaller/0.8.5-FixedRoster-Test')
            $client.DownloadFile([string]$sdk.url, $part)
            if ((Hash-File $part 'SHA512') -ne $sdk.sha512) { throw 'Microsoft SDK checksum mismatch. The downloaded tools will NOT be run.' }
            Move-Item -LiteralPath $part -Destination $archive -Force
        } finally { $client.Dispose(); if (Test-Path -LiteralPath $part) { Remove-Item -LiteralPath $part -Force } }
    }
    $dotnet = Join-Path $toolDir 'dotnet.exe'
    $ready = Join-Path $toolDir 'srr-tools-ready.txt'
    if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf) -or -not (Test-Path -LiteralPath $ready -PathType Leaf)) {
        Write-Host 'Preparing the private compiler cache...'
        if (Test-Path -LiteralPath $toolDir) { Remove-Item -LiteralPath $toolDir -Recurse -Force }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($archive, $toolDir)
        [IO.File]::WriteAllText($ready, [string]$sdk.sha512)
    }
    $env:DOTNET_ROOT = $toolDir
    $env:DOTNET_ROOT_X64 = $toolDir
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_HOME = Join-Path $Cache 'cli-home'
    $env:NUGET_PACKAGES = Join-Path $Cache 'nuget'
    $env:NuGetAudit = 'false'
    return $dotnet
}
function Backup-Targets([string]$Folder, [object[]]$Plan) {
    $backup = Join-Path $Folder ('SRR-Backups\fixed-roster-085-test-' + $RunTag)
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $records=@()
    foreach ($entry in $Plan) {
        $target = Join-Path $Folder $entry.relativePath
        $exists = Test-Path -LiteralPath $target -PathType Leaf
        $oldHash = $null
        if ($exists) { Copy-Item -LiteralPath $target -Destination (Join-Path $backup $entry.name); $oldHash=Hash-File (Join-Path $backup $entry.name) }
        $records += [ordered]@{name=$entry.name; relativePath=$entry.relativePath; existed=$exists; oldHash=$oldHash; newHash=(Hash-File $entry.source)}
    }
    $manifest=[ordered]@{marker='SRR-AutoInstaller-085-FixedRoster-Test';status='pending';gameAssemblySha256=$Package.gameAssemblySha256;createdUtc=[DateTime]::UtcNow.ToString('o');files=$records}
    Write-Manifest (Join-Path $backup 'manifest.json') $manifest
    return $backup
}
function Restore-Backup([string]$Folder, [string]$Backup, [bool]$Recovery) {
    $manifestPath = Join-Path $Backup 'manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.marker -ne 'SRR-AutoInstaller-085-FixedRoster-Test' -or $manifest.gameAssemblySha256 -ne $Package.gameAssemblySha256) { throw 'Backup identity is not valid for this installer.' }
    $allowed=@{
        'SuikodenRandomizer.dll'='BepInEx\plugins\SuikodenRandomizer\SuikodenRandomizer.dll';
        'SuikodenRandomizer.Core.dll'='BepInEx\plugins\SuikodenRandomizer\SuikodenRandomizer.Core.dll'
    }
    # Validate all originals before restoring either DLL.
    foreach ($entry in $manifest.files) {
        if (-not $allowed.ContainsKey([string]$entry.name) -or $allowed[[string]$entry.name] -ne $entry.relativePath) { throw 'Unexpected path in backup manifest.' }
        if ($entry.existed -and ((Hash-File (Join-Path $Backup $entry.name)) -ne $entry.oldHash)) { throw "Backup checksum mismatch: $($entry.name)" }
        $target=Join-Path $Folder $entry.relativePath
        if (-not $Recovery -and $entry.name.EndsWith('.dll') -and (Test-Path -LiteralPath $target)) {
            $current=Hash-File $target
            if ($current -ne $entry.newHash -and $current -ne $entry.oldHash) { throw "Installed $($entry.name) changed since this update. Rollback stopped to avoid overwriting another release." }
        }
    }
    Assert-GameClosed
    foreach ($entry in $manifest.files) {
        $target=Join-Path $Folder $entry.relativePath
        if ($entry.existed) { Write-VerifiedFile (Join-Path $Backup $entry.name) $target }
        elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
    }
    $manifest.status='rolled-back'
    Write-Manifest $manifestPath $manifest
}

try {
    Start-Transcript -LiteralPath $Log -Force | Out-Null
    $TranscriptStarted = $true
    Write-Host 'Suikoden Randomizer 0.8.5 Fixed Roster Test - Automatic setup'
    Write-Host 'This is a build-and-install package, not a precompiled release.'
    Write-Host 'No source editing or manual SDK installation is needed. Internet is required on first installation.'
    Write-Host 'Use a separate New Game for the fixed roster test. See README.txt for setup and save backups.'
    Write-Host ''
    Assert-GameClosed
    $GamePath=Resolve-GameFolder $GamePath
    Assert-Game $GamePath
    if ($Mode -eq 'Rollback') {
        $backupRoot=Join-Path $GamePath 'SRR-Backups'
        $candidates=@()
        if (Test-Path -LiteralPath $backupRoot) {
            $candidates=@(Get-ChildItem -LiteralPath $backupRoot -Directory -Filter 'fixed-roster-085-test-*' | Sort-Object Name -Descending | Where-Object {
                $f=Join-Path $_.FullName 'manifest.json'
                if (-not (Test-Path -LiteralPath $f)) { return $false }
                $m=Get-Content -LiteralPath $f -Raw | ConvertFrom-Json
                return $m.marker -eq 'SRR-AutoInstaller-085-FixedRoster-Test' -and $m.status -ne 'rolled-back'
            })
        }
        if ($candidates.Count -eq 0) { throw 'No unrestored 0.8.5 fixed roster test backup was found in this game folder.' }
        Restore-Backup $GamePath $candidates[0].FullName $false
        Write-Host 'Rollback completed. Any previous plugin DLLs were restored.'
        $Succeeded=$true
    } else {
        # Avatar profiles are isolated; no old seed or save binding is adopted.
        $payload=Join-Path $PSScriptRoot 'payload.zip'
        if ((Hash-File $payload) -ne $Package.payloadSha256) { throw 'Installer payload checksum mismatch. Extract the ZIP again.' }
        $Workspace=Join-Path $Cache ('work\' + $RunTag)
        New-Item -ItemType Directory -Path $Workspace -Force | Out-Null
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($payload, $Workspace)
        $dotnet=Ensure-Toolchain
        Invoke-Tool $dotnet @('--version') $Workspace
        Write-Host 'Checking the canonical-avatar native API contracts and source types...'
        Invoke-ManagedCheck $dotnet $Workspace 'SuikodenRandomizer.Avatar.Contracts' @((Join-Path $GamePath 'BepInEx\interop\GSD1.dll'))
        Write-Host 'Compiling the update against your installed game and BepInEx references...'
        $project=Join-Path $Workspace 'src\SuikodenRandomizer.AvatarPlugin\SuikodenRandomizer.AvatarPlugin.csproj'
        Invoke-Tool $dotnet @('build',$project,'--configuration','Release','--nologo','-m:1','-p:RestoreBuildInParallel=false','-p:BuildInParallel=false',('-p:GamePath='+$GamePath),'-p:NuGetAudit=false') $Workspace
        foreach ($test in @('SuikodenRandomizer.Core.SmokeTests','SuikodenRandomizer.Avatar.Tests','SuikodenRandomizer.AvatarShuffle.Tests','SuikodenRandomizer.AvatarPresentation.Tests','SuikodenRandomizer.AvatarRuntime.Tests','SuikodenRandomizer.AvatarParty.Tests','SuikodenRandomizer.AvatarUnite.Tests','SuikodenRandomizer.FixedRoster.Tests')) {
            Write-Host ('Running checks: ' + $test)
            Invoke-ManagedCheck $dotnet $Workspace $test
        }
        $built=Join-Path $Workspace 'src\SuikodenRandomizer.AvatarPlugin\bin\Release'
        $plan=@()
        foreach ($name in @('SuikodenRandomizer.dll','SuikodenRandomizer.Core.dll')) {
            $file=Join-Path $built $name
            if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing compiled output: $name" }
            $version=[Reflection.AssemblyName]::GetAssemblyName($file).Version
            if ($version.ToString() -ne $Package.assemblyVersion) { throw "Wrong compiled version for $name`: $version" }
            $plan += [pscustomobject]@{name=$name;source=$file;relativePath=('BepInEx\plugins\SuikodenRandomizer\'+$name)}
        }
        Assert-GameClosed
        Write-Host 'Checks passed. Backing up plugin files and installing the fixed roster test...' 
        $backup=Backup-Targets $GamePath $plan
        try {
            foreach ($entry in $plan) {
                $target=Join-Path $GamePath $entry.relativePath
                New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
                Write-VerifiedFile $entry.source $target
                if ((Hash-File $target) -ne (Hash-File $entry.source)) { throw "Installed checksum mismatch: $($entry.name)" }
            }
            $manifestPath=Join-Path $backup 'manifest.json'
            $manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            $manifest.status='installed'
            Write-Manifest $manifestPath $manifest
        } catch {
            $installError=$_
            try { Restore-Backup $GamePath $backup $true; Write-Host 'The update failed; the previous plugin was restored.' }
            catch { Write-Host "Automatic recovery also failed. Do not start the game until Rollback.cmd has restored the backup: $backup" }
            throw $installError
        }
        $Succeeded=$true
        Write-Host ''
        Write-Host 'Installed Suikoden Randomizer 0.8.5 Fixed Roster Test.'
        Write-Host 'Launch once to the title screen, then close the game to set Mode, Seed, and Companions under [Shuffle].'
        Write-Host 'See README.txt for chosen and random presets, test steps, and rollback.'
        Write-Host "Plugin file backup: $backup"
        Write-Host 'If a test fails, close the game and run Collect-Avatar-Diagnostics.cmd.'

    }
} catch {
    Write-Host ''
    Write-Host ('SETUP DID NOT COMPLETE: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host "Diagnostic log: $Log"
    Write-Host 'There are no source-editing steps. Keep this log if setup needs troubleshooting.'
} finally {
    if ($Succeeded -and $null -ne $Workspace -and (Test-Path -LiteralPath $Workspace)) {
        try { Remove-Item -LiteralPath $Workspace -Recurse -Force } catch { Write-Host "Temporary files remain in $Workspace" }
    }
    Write-Host "Installer log: $Log"
    if ($TranscriptStarted) { try { Stop-Transcript | Out-Null } catch { } }
}
if ($Succeeded) { exit 0 } else { exit 1 }
