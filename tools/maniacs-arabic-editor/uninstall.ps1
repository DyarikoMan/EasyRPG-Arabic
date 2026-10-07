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
	if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return $null }
	return $dialog.SelectedPath
}

function Test-IsAdministrator {
	$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
	$principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
	return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-FileHashOrEmpty([string]$Path) {
	if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '' }
	return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

if ([string]::IsNullOrWhiteSpace($ManiacsDir)) {
	$ManiacsDir = Select-ManiacsDirectory
	if ([string]::IsNullOrWhiteSpace($ManiacsDir)) { Write-Host 'Uninstall cancelled.'; exit 0 }
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
$stateDir = Join-Path $root '.ManiacsArabicEditor'
$statePath = Join-Path $stateDir 'install-state.json'
if (-not (Test-Path -LiteralPath (Join-Path $root 'rpg2003.exe') -PathType Leaf)) {
	throw 'The selected folder does not contain rpg2003.exe.'
}
if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
	throw "No EasyRPG Arabic Editor installation state was found at $statePath."
}

$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
if ($state.Version -ne 1) { throw 'The installation state has an unsupported version; no files were changed.' }
$allowed = @('Plugin\EasyCmdTos.dll', 'Plugin\EasyRpgCommands.dll', 'Plugin\0Harmony.dll')
$pluginDir = Join-Path $root 'Plugin'
$unresolved = $false

foreach ($entry in @($state.Files)) {
	$relative = [string]$entry.Path
	if ($allowed -notcontains $relative) {
		Write-Warning "Skipped unexpected state entry: $relative"
		$unresolved = $true
		continue
	}
	$name = [System.IO.Path]::GetFileName($relative)
	$target = Join-Path $pluginDir $name
	$currentHash = Get-FileHashOrEmpty $target
	$installedHash = [string]$entry.InstalledHash
	$originalHash = [string]$entry.OriginalHash
	$originalExisted = [bool]$entry.OriginalExisted
	$originalBackup = Join-Path $stateDir (Join-Path 'backups\original' $relative)

	if ($currentHash -and $installedHash -and $currentHash -ne $installedHash -and
		-not ($originalExisted -and $currentHash -eq $originalHash)) {
		Write-Warning "Preserved a file changed since installation: $target"
		$unresolved = $true
		continue
	}

	if ($originalExisted) {
		if (-not (Test-Path -LiteralPath $originalBackup -PathType Leaf)) {
			Write-Warning "Original backup is missing; preserved the current file: $target"
			$unresolved = $true
			continue
		}
		Copy-Item -LiteralPath $originalBackup -Destination $target -Force
		Write-Host "Restored Plugin\$name"
	} elseif ($currentHash -and $installedHash -and $currentHash -eq $installedHash) {
		Remove-Item -LiteralPath $target -Force
		Write-Host "Removed Plugin\$name"
	} elseif ($currentHash) {
		Write-Warning "Preserved an unrecognized file: $target"
		$unresolved = $true
	}
}

if ($unresolved) {
	Write-Warning "Some files need review. Install state and backups remain at $stateDir"
	Write-Host 'Resolve those files manually, then run uninstall.ps1 again.'
	exit 1
}

Remove-Item -LiteralPath $statePath -Force
Write-Host 'EasyRPG Arabic Editor patch uninstalled.'
Write-Host "Original backups are retained at $(Join-Path $stateDir 'backups')"
