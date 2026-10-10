[CmdletBinding()]
param(
	[ValidateSet('Public', 'PrivateInternal')]
	[string] $Mode = 'Public',
	[ValidateSet('EmbeddedValidated241029', 'UserSuppliedValidatedPackage')]
	[string] $ManiacsPackageSource,
	[string] $ValidatedManiacsPackageDirectory,
	[switch] $ConfirmManiacsRedistributionPermission,
	[string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$setupRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $setupRoot 'src\Setup.App\Setup.App.csproj'
$packageSource = $ManiacsPackageSource
if (-not $packageSource) {
	$packageSource = if ($Mode -eq 'PrivateInternal') { 'EmbeddedValidated241029' } else { 'UserSuppliedValidatedPackage' }
}
if ($Mode -eq 'Public' -and $packageSource -eq 'EmbeddedValidated241029' -and -not $ConfirmManiacsRedistributionPermission) {
	throw 'A public build may embed Maniacs only after redistribution permission is explicitly confirmed with -ConfirmManiacsRedistributionPermission.'
}
if ($packageSource -eq 'EmbeddedValidated241029' -and [string]::IsNullOrWhiteSpace($ValidatedManiacsPackageDirectory)) {
	throw 'Embedding the validated Maniacs package requires -ValidatedManiacsPackageDirectory.'
}

if (-not $OutputDirectory) {
	$output = if ($Mode -eq 'PrivateInternal') { Join-Path $setupRoot 'builds\private-internal' } else { Join-Path $setupRoot 'dist' }
}
else {
	$output = [IO.Path]::GetFullPath($OutputDirectory)
}
[IO.Directory]::CreateDirectory($output) | Out-Null

$dotnetArguments = @(
	'publish', $project,
	'-c', 'Release',
	'-r', 'win-x64',
	'--self-contained', 'true',
	'-p:PublishSingleFile=true',
	'-p:IncludeNativeLibrariesForSelfExtract=true',
	'-p:EnableCompressionInSingleFile=true',
	'-p:DebugType=None',
	'-p:DebugSymbols=false',
	"-p:SetupBuildFlavor=$Mode",
	"-p:ManiacsPackageSource=$packageSource",
	"-p:ManiacsRedistributionPermissionConfirmed=$(([bool]$ConfirmManiacsRedistributionPermission).ToString().ToLowerInvariant())",
	'-o', $output
)

$archivePath = $null
if ($packageSource -eq 'EmbeddedValidated241029') {
	$archiveDirectory = Join-Path ([IO.Path]::GetTempPath()) 'EasyRpgArabic-Setup-build'
	[IO.Directory]::CreateDirectory($archiveDirectory) | Out-Null
	$archivePath = Join-Path $archiveDirectory ('Maniacs241029-' + [Guid]::NewGuid().ToString('N') + '.zip')
	$archiveBuilder = Join-Path $PSScriptRoot 'create-validated-maniacs-archive.ps1'
	$recipe = Join-Path $setupRoot 'data\maniacs\recipes\maniacs-241029.json'
	$layout = Join-Path $setupRoot 'data\maniacs\layouts\common-english-v1.json'
	$compatibility = Join-Path $setupRoot 'data\maniacs\compatibility\arabic-editor.json'

	& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $archiveBuilder `
		-PackageRoot $ValidatedManiacsPackageDirectory -OutputZip $archivePath `
		-RecipePath $recipe -LayoutPath $layout -CompatibilityPath $compatibility
	if ($LASTEXITCODE -ne 0) { throw "Validated Maniacs archive creation failed with exit code $LASTEXITCODE." }
	$dotnetArguments += "-p:ManiacsEmbeddedArchivePath=$archivePath"
}

try {
	& dotnet @dotnetArguments
	if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
}
finally {
	if ($archivePath -and (Test-Path -LiteralPath $archivePath)) { Remove-Item -LiteralPath $archivePath -Force }
}

$executable = Join-Path $output 'EasyRPG-Arabic-Setup.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Published executable is missing: $executable" }
$otherFiles = @(Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Name -ne 'EasyRPG-Arabic-Setup.exe' })
if ($otherFiles.Count -ne 0) {
	$names = ($otherFiles | ForEach-Object Name) -join ', '
	throw "Publish output contains files outside the single-executable bundle: $names"
}
Write-Host "Created self-contained Windows x64 setup ($Mode, $packageSource): $executable"
