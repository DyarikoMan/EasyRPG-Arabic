[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$setupRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $setupRoot 'src\Setup.App\Setup.App.csproj'
$output = Join-Path $setupRoot 'dist'
New-Item -ItemType Directory -Force -Path $output | Out-Null

& dotnet publish $project -c Release -r win-x64 --self-contained true `
	-p:PublishSingleFile=true `
	-p:IncludeNativeLibrariesForSelfExtract=true `
	-p:EnableCompressionInSingleFile=true `
	-p:DebugType=None `
	-p:DebugSymbols=false `
	-o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$executable = Join-Path $output 'EasyRPG-Arabic-Setup.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Published executable is missing: $executable" }
$otherFiles = @(Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Name -ne 'EasyRPG-Arabic-Setup.exe' })
if ($otherFiles.Count -ne 0) {
	$names = ($otherFiles | ForEach-Object Name) -join ', '
	throw "Publish output contains files outside the single-executable bundle: $names"
}
Write-Host "Created self-contained Windows x64 setup: $executable"
