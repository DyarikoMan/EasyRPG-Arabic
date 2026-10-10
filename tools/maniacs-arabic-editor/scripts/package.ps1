$ErrorActionPreference = 'Stop'

$toolRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $toolRoot '..\..'))
$packagePlugin = Join-Path $toolRoot 'Plugin'
$distDir = Join-Path $toolRoot 'dist'
$stageDir = Join-Path $toolRoot 'build\package-stage'
$zipPath = Join-Path $distDir 'Maniacs-Arabic-Editor-Windows-x86.zip'

function Read-UInt16([byte[]]$Bytes, [int]$Offset) {
	if ($Offset -lt 0 -or $Offset + 2 -gt $Bytes.Length) { throw 'Invalid PE structure while removing debug metadata.' }
	return [BitConverter]::ToUInt16($Bytes, $Offset)
}

function Read-UInt32([byte[]]$Bytes, [int]$Offset) {
	if ($Offset -lt 0 -or $Offset + 4 -gt $Bytes.Length) { throw 'Invalid PE structure while removing debug metadata.' }
	return [BitConverter]::ToUInt32($Bytes, $Offset)
}

function Convert-RvaToFileOffset([byte[]]$Bytes, [uint32]$Rva, [int]$Length, [int]$SectionTable, [int]$SectionCount) {
	for ($index = 0; $index -lt $SectionCount; ++$index) {
		$section = $SectionTable + 40 * $index
		$virtualSize = Read-UInt32 $Bytes ($section + 8)
		$virtualAddress = Read-UInt32 $Bytes ($section + 12)
		$rawSize = Read-UInt32 $Bytes ($section + 16)
		$rawPointer = Read-UInt32 $Bytes ($section + 20)
		$span = [Math]::Max([long]$virtualSize, [long]$rawSize)
		$delta = [long]$Rva - [long]$virtualAddress
		if ($delta -ge 0 -and $delta + $Length -le $rawSize -and $delta -lt $span) {
			$offset = [long]$rawPointer + $delta
			if ($offset + $Length -gt $Bytes.Length) { throw 'Debug metadata points outside the PE file.' }
			return [int]$offset
		}
	}
	throw 'Could not map a PE debug directory into the file.'
}

function Remove-PeDebugMetadata([string]$Path) {
	$bytes = [System.IO.File]::ReadAllBytes($Path)
	$peOffset = [int](Read-UInt32 $bytes 0x3C)
	if ((Read-UInt32 $bytes $peOffset) -ne 0x00004550) { throw "Not a PE file: $Path" }
	$coff = $peOffset + 4
	$sectionCount = Read-UInt16 $bytes ($coff + 2)
	$optionalSize = Read-UInt16 $bytes ($coff + 16)
	$optional = $coff + 20
	$magic = Read-UInt16 $bytes $optional
	if ($magic -eq 0x10B) {
		$directoryBase = $optional + 96
		$directoryCountOffset = $optional + 92
	} elseif ($magic -eq 0x20B) {
		$directoryBase = $optional + 112
		$directoryCountOffset = $optional + 108
	} else {
		throw "Unsupported PE optional header in $Path"
	}
	if ((Read-UInt32 $bytes $directoryCountOffset) -le 6) { return }
	$debugEntry = $directoryBase + (6 * 8)
	$debugRva = Read-UInt32 $bytes $debugEntry
	$debugSize = [int](Read-UInt32 $bytes ($debugEntry + 4))
	if ($debugRva -eq 0 -or $debugSize -eq 0) { return }
	if (($debugSize % 28) -ne 0) { throw "Unexpected PE debug directory size in $Path" }
	$sectionTable = $optional + $optionalSize
	$debugOffset = Convert-RvaToFileOffset $bytes $debugRva $debugSize $sectionTable $sectionCount
	for ($offset = $debugOffset; $offset + 28 -le $debugOffset + $debugSize; $offset += 28) {
		$dataSize = [int](Read-UInt32 $bytes ($offset + 16))
		$dataPointer = [int](Read-UInt32 $bytes ($offset + 24))
		if ($dataSize -gt 0) {
			if ($dataPointer -lt 0 -or $dataPointer + $dataSize -gt $bytes.Length) { throw "Debug payload points outside the PE file: $Path" }
			[Array]::Clear($bytes, $dataPointer, $dataSize)
		}
	}
	[Array]::Clear($bytes, $debugOffset, $debugSize)
	[Array]::Clear($bytes, $debugEntry, 8)
	[System.IO.File]::WriteAllBytes($Path, $bytes)
}

foreach ($name in @('EasyCmdTos.dll', 'EasyRpgCommands.dll', '0Harmony.dll')) {
	if (-not (Test-Path -LiteralPath (Join-Path $packagePlugin $name) -PathType Leaf)) {
		throw "Package runtime is incomplete: missing Plugin\$name"
	}
}

$resolvedStage = [System.IO.Path]::GetFullPath($stageDir)
$resolvedTool = [System.IO.Path]::GetFullPath($toolRoot).TrimEnd('\') + '\'
if (-not $resolvedStage.StartsWith($resolvedTool, [StringComparison]::OrdinalIgnoreCase)) {
	throw "Refusing to clear staging outside the tool directory: $resolvedStage"
}
if (Test-Path -LiteralPath $resolvedStage) { Remove-Item -LiteralPath $resolvedStage -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $resolvedStage 'Plugin') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $resolvedStage 'licenses') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $resolvedStage 'ArabicSupport\tools') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $resolvedStage 'ArabicSupport\terminology') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $resolvedStage 'ArabicSupport\template\Language\ar\Font') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $resolvedStage 'ArabicSupport\licenses') | Out-Null
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$rootFiles = @('install.bat', 'install.ps1', 'uninstall.bat', 'uninstall.ps1', 'README.md', 'THIRD_PARTY.txt')
foreach ($name in $rootFiles) {
	Copy-Item -LiteralPath (Join-Path $toolRoot $name) -Destination $resolvedStage
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'COPYING') -Destination $resolvedStage
foreach ($name in @('EasyCmdTos.dll', 'EasyRpgCommands.dll', '0Harmony.dll')) {
	Copy-Item -LiteralPath (Join-Path $packagePlugin $name) -Destination (Join-Path $resolvedStage "Plugin\$name")
}
Remove-PeDebugMetadata (Join-Path $resolvedStage 'Plugin\EasyCmdTos.dll')
Remove-PeDebugMetadata (Join-Path $resolvedStage 'Plugin\0Harmony.dll')
Copy-Item -LiteralPath (Join-Path $toolRoot 'third_party\easyrpgcommands\COPYING') -Destination (Join-Path $resolvedStage 'licenses\EasyRpgCommands-COPYING')
Copy-Item -LiteralPath (Join-Path $toolRoot 'third_party\harmony\LICENSE') -Destination (Join-Path $resolvedStage 'licenses\Harmony-LICENSE')
Copy-Item -LiteralPath (Join-Path $toolRoot 'third_party\minhook\LICENSE.txt') -Destination (Join-Path $resolvedStage 'licenses\MinHook-LICENSE.txt')

$arabicSupport = Join-Path $resolvedStage 'ArabicSupport'
Copy-Item -LiteralPath (Join-Path $repoRoot 'arabic\tools\update-arabic.ps1') -Destination (Join-Path $arabicSupport 'tools\update-arabic.ps1')
Copy-Item -LiteralPath (Join-Path $repoRoot 'arabic\tools\lcftrans.exe') -Destination (Join-Path $arabicSupport 'tools\lcftrans.exe')
Copy-Item -LiteralPath (Join-Path $repoRoot 'arabic\tools\lcftrans-source.json') -Destination (Join-Path $arabicSupport 'tools\lcftrans-source.json')
Copy-Item -LiteralPath (Join-Path $repoRoot 'arabic\terminology\canonical-terms.json') -Destination (Join-Path $arabicSupport 'terminology\canonical-terms.json')
Copy-Item -LiteralPath (Join-Path $repoRoot 'arabic\template\Language\ar\Meta.ini') -Destination (Join-Path $arabicSupport 'template\Language\ar\Meta.ini')
foreach ($name in @('Font.ttf', 'OFL.txt', 'NOTICE.txt', 'SOURCE.txt')) {
	Copy-Item -LiteralPath (Join-Path $repoRoot "arabic\template\Language\ar\Font\$name") -Destination (Join-Path $arabicSupport "template\Language\ar\Font\$name")
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'arabic\licenses\LcfTrans-COPYING.txt') -Destination (Join-Path $arabicSupport 'licenses\LcfTrans-COPYING.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'arabic\licenses\liblcf-COPYING.txt') -Destination (Join-Path $arabicSupport 'licenses\liblcf-COPYING.txt')

$lcfTransHash = (Get-FileHash -LiteralPath (Join-Path $arabicSupport 'tools\lcftrans.exe') -Algorithm SHA256).Hash
if ($lcfTransHash -ne 'FF8C5C26CF4CA1FF3A837B2C67C1E458370F2340BFB57F4F7D652417AD239379') {
	throw "Bundled LcfTrans hash is not the validated Windows x86 build: $lcfTransHash"
}
$fontHash = (Get-FileHash -LiteralPath (Join-Path $arabicSupport 'template\Language\ar\Font\Font.ttf') -Algorithm SHA256).Hash
if ($fontHash -ne '68B412A4D8443FE677DD37412BB44D117CADE607572B2CC9689177DA3934952F') {
	throw "Bundled Arabic PixelSmall font hash is not the validated historical release: $fontHash"
}
$canonical = Get-Content -LiteralPath (Join-Path $arabicSupport 'terminology\canonical-terms.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$canonicalCount = @($canonical.PSObject.Properties | Where-Object { $_.Name.StartsWith('terms.', [System.StringComparison]::Ordinal) }).Count
if ($canonicalCount -ne 143) { throw "Expected 143 canonical terms in the package, found $canonicalCount." }

if (Get-ChildItem -LiteralPath $resolvedStage -Recurse -File -Filter '*.pdb') {
	throw 'The package staging directory must not contain PDB files.'
}

if (Test-Path -LiteralPath $zipPath -PathType Leaf) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $resolvedStage '*') -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Created $zipPath"
Get-ChildItem -LiteralPath $resolvedStage -Recurse -File | ForEach-Object {
	Write-Host $_.FullName.Substring($resolvedStage.Length + 1)
}
