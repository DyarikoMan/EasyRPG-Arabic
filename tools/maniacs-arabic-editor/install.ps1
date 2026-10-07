[CmdletBinding()]
param(
	[Parameter(Position = 0)]
	[string]$ManiacsDir,
	[switch]$Elevated
)

$ErrorActionPreference = 'Stop'

function Select-ManiacsDirectory {
	Add-Type -AssemblyName System.Windows.Forms
	$dialog = New-Object System.Windows.Forms.FolderBrowserDialog
	$dialog.Description = 'Select the Maniacs Patch installation folder (the folder containing rpg2003.exe).'
	$dialog.ShowNewFolderButton = $false
	if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
		return $null
	}
	return $dialog.SelectedPath
}

function Test-IsAdministrator {
	$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
	$principal = New-Object Security.Principal.WindowsPrincipal($identity)
	return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-PeMachine([string]$Path) {
	$stream = [System.IO.File]::OpenRead($Path)
	try {
		$reader = New-Object System.IO.BinaryReader($stream)
		$stream.Position = 0x3C
		$peOffset = $reader.ReadInt32()
		if ($peOffset -lt 0 -or $peOffset + 6 -gt $stream.Length) { return 0 }
		$stream.Position = $peOffset
		if ($reader.ReadUInt32() -ne 0x00004550) { return 0 }
		return $reader.ReadUInt16()
	} finally {
		$stream.Dispose()
	}
}

function Save-State($State, [string]$Path) {
	$tempPath = "$Path.tmp"
	$json = ConvertTo-Json -InputObject $State -Depth 8
	[System.IO.File]::WriteAllText($tempPath, $json, (New-Object System.Text.UTF8Encoding($false)))
	Move-Item -LiteralPath $tempPath -Destination $Path -Force
}

if ([string]::IsNullOrWhiteSpace($ManiacsDir)) {
	$ManiacsDir = Select-ManiacsDirectory
	if ([string]::IsNullOrWhiteSpace($ManiacsDir)) { Write-Host 'Installation cancelled.'; exit 0 }
}

$ManiacsDir = [System.IO.Path]::GetFullPath($ManiacsDir)
if (-not $Elevated -and -not (Test-IsAdministrator)) {
	$quotedScript = '"' + $PSCommandPath + '"'
	$quotedDirectory = '"' + $ManiacsDir + '"'
	$arguments = "-NoProfile -ExecutionPolicy Bypass -File $quotedScript -ManiacsDir $quotedDirectory -Elevated"
	$hostExecutable = (Get-Process -Id $PID).Path
	$child = Start-Process -FilePath $hostExecutable -Verb RunAs -ArgumentList $arguments -Wait -PassThru
	exit $child.ExitCode
}

$root = $ManiacsDir.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$requiredFiles = @('rpg2003.exe', 'cmdcs.dll', 'cmdTos.dll')
foreach ($name in $requiredFiles) {
	$path = Join-Path $root $name
	if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
		throw "This folder does not look like a Maniacs installation: missing $name"
	}
	if ((Get-PeMachine $path) -ne 0x014C) {
		throw "This folder does not look like a 32-bit Maniacs installation: $name is not x86."
	}
}

$pluginDir = Join-Path $root 'Plugin'
if (-not (Test-Path -LiteralPath $pluginDir -PathType Container)) {
	throw 'This folder does not look like a Maniacs installation: missing Plugin folder.'
}

$payloadDir = Join-Path $PSScriptRoot 'Plugin'
$fileNames = @('EasyCmdTos.dll', 'EasyRpgCommands.dll', '0Harmony.dll')
$stateDir = Join-Path $root '.ManiacsArabicEditor'
$backupDir = Join-Path $stateDir 'backups'
$statePath = Join-Path $stateDir 'install-state.json'
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

$oldState = $null
if (Test-Path -LiteralPath $statePath -PathType Leaf) {
	$oldState = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
	if ($oldState.Version -ne 1) { throw 'The existing installation state has an unsupported version; no files were changed.' }
}

$oldFiles = @{}
if ($null -ne $oldState) {
	foreach ($entry in @($oldState.Files)) {
		$oldFiles[[string]$entry.Path] = $entry
	}
}

$newFiles = @()
$snapshotTag = 'reinstall-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N')
foreach ($name in $fileNames) {
	$source = Join-Path $payloadDir $name
	if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Package is incomplete: missing Plugin\$name" }
	$relative = "Plugin\$name"
	$destination = Join-Path $pluginDir $name
	$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash

	if ($oldFiles.ContainsKey($relative)) {
		$previous = $oldFiles[$relative]
		$record = @{
			Path = $relative
			OriginalExisted = [bool]$previous.OriginalExisted
			OriginalHash = [string]$previous.OriginalHash
			InstalledHash = $sourceHash
		}
		if (Test-Path -LiteralPath $destination -PathType Leaf) {
			$currentHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
			if ($previous.InstalledHash -and $currentHash -ne [string]$previous.InstalledHash) {
				$snapshot = Join-Path $backupDir (Join-Path $snapshotTag $relative)
				New-Item -ItemType Directory -Force -Path (Split-Path -Parent $snapshot) | Out-Null
				Copy-Item -LiteralPath $destination -Destination $snapshot
				Write-Host "Saved a changed installed file before replacing it: $snapshot"
			}
		}
	} else {
		$existed = Test-Path -LiteralPath $destination -PathType Leaf
		$originalHash = ''
		if ($existed) {
			$originalHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
			$originalBackup = Join-Path $backupDir (Join-Path 'original' $relative)
			New-Item -ItemType Directory -Force -Path (Split-Path -Parent $originalBackup) | Out-Null
			Copy-Item -LiteralPath $destination -Destination $originalBackup -Force
			Write-Host "Backed up existing Plugin\$name to $originalBackup"
		}
		$record = @{
			Path = $relative
			OriginalExisted = $existed
			OriginalHash = $originalHash
			InstalledHash = $sourceHash
		}
	}
	$newFiles += $record
}

$state = @{ Version = 1; Files = $newFiles }
Save-State $state $statePath
foreach ($name in $fileNames) {
	Copy-Item -LiteralPath (Join-Path $payloadDir $name) -Destination (Join-Path $pluginDir $name) -Force
	Write-Host "Installed Plugin\$name"
}

Write-Host ''
Write-Host 'EasyRPG Arabic Editor patch installed.'
Write-Host "Maniacs folder: $root"
Write-Host "Runtime files: $pluginDir"
Write-Host "Backups and install state: $stateDir"
Write-Host 'Restart Maniacs to use the Arabic editor rendering hook.'
