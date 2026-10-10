[CmdletBinding()]
param(
	[Parameter(Mandatory)] [string] $PackageRoot,
	[Parameter(Mandatory)] [string] $OutputZip,
	[Parameter(Mandatory)] [string] $RecipePath,
	[Parameter(Mandatory)] [string] $LayoutPath,
	[Parameter(Mandatory)] [string] $CompatibilityPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

$package = (Resolve-Path -LiteralPath $PackageRoot).Path
$recipe = Get-Content -LiteralPath $RecipePath -Raw | ConvertFrom-Json
$layout = Get-Content -LiteralPath $LayoutPath -Raw | ConvertFrom-Json
$compatibility = Get-Content -LiteralPath $CompatibilityPath -Raw | ConvertFrom-Json
if ($recipe.id -ne 'maniacs-241029' -or $recipe.buildId -ne '241029') {
	throw 'Only the validated Maniacs 241029 recipe may be embedded.'
}
if ($compatibility.validatedRecipeIds -notcontains $recipe.id) {
	throw 'The Arabic Editor compatibility manifest does not validate this Maniacs recipe.'
}
if (-not (Test-Path -LiteralPath (Join-Path $package 'utility.exe') -PathType Leaf)) {
	throw 'The selected folder is not the root of an extracted Maniacs package.'
}

$vanillaRootFiles = @('rpg2003.exe', 'ultimate_eb.dll', 'rpg_rt.ldb.dat', 'ultimate_rt_eb.dll.dat', 'UNLHA32.DLL')
foreach ($fileName in $vanillaRootFiles) {
	if (Test-Path -LiteralPath (Join-Path $package $fileName)) {
		throw "Refusing to embed a folder containing a vanilla Steam RPG Maker file: $fileName"
	}
}

foreach ($relativeDirectory in $layout.requiredDirectories) {
	if (-not (Test-Path -LiteralPath (Join-Path $package $relativeDirectory) -PathType Container)) {
		throw "The Maniacs package is missing required directory '$relativeDirectory'."
	}
}

foreach ($marker in $recipe.buildMarkers) {
	$markerPath = Join-Path $package $marker.path
	if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) { throw "Missing Maniacs build marker: $($marker.path)" }
	$content = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($markerPath))
	if (-not $content.Contains($marker.containsAscii)) { throw "The selected package is not Maniacs build $($recipe.buildId)." }
}

function Get-PeArchitecture([string] $Path) {
	$stream = [IO.File]::OpenRead($Path)
	try {
		$reader = [IO.BinaryReader]::new($stream)
		if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) { return 'invalid PE' }
		$stream.Position = 0x3C
		$peOffset = $reader.ReadInt32()
		if ($peOffset -lt 64 -or $peOffset -gt ($stream.Length - 6)) { return 'invalid PE' }
		$stream.Position = $peOffset
		if ($reader.ReadUInt32() -ne 0x00004550) { return 'invalid PE' }
		switch ($reader.ReadUInt16()) {
			0x014c { return 'x86' }
			0x8664 { return 'x64' }
			0x01c0 { return 'arm' }
			0x01c4 { return 'arm' }
			0xaa64 { return 'arm64' }
			default { return 'unknown' }
		}
	}
	finally { $stream.Dispose() }
}

$identityFiles = @($recipe.identityFileSha256.PSObject.Properties)
foreach ($file in $identityFiles) {
	$relative = $file.Name.Replace('/', [IO.Path]::DirectorySeparatorChar).Replace('\', [IO.Path]::DirectorySeparatorChar)
	if ([IO.Path]::IsPathRooted($relative) -or $relative.Split([IO.Path]::DirectorySeparatorChar) -contains '..') {
		throw "Unsafe path in the validated Maniacs recipe: $($file.Name)"
	}
	$path = Join-Path $package $relative
	if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing Maniacs identity file: $($file.Name)" }
	$actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
	if ($actual -ne $file.Value) { throw "Fingerprint mismatch in Maniacs 241029 package: $($file.Name)" }
}

foreach ($required in $layout.requiredFiles) {
	if ($null -eq $required.architecture) { continue }
	$path = Join-Path $package ($required.path.Replace('/', [IO.Path]::DirectorySeparatorChar))
	$actualArchitecture = Get-PeArchitecture $path
	if ($actualArchitecture -ne $required.architecture) {
		throw "Architecture mismatch for $($required.path): expected $($required.architecture), found $actualArchitecture."
	}
}

$output = [IO.Path]::GetFullPath($OutputZip)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
if (Test-Path -LiteralPath $output) { throw "Refusing to replace an existing archive: $output" }
$fileStream = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
	$archive = [IO.Compression.ZipArchive]::new($fileStream, [IO.Compression.ZipArchiveMode]::Create, $false)
	try {
		foreach ($relativeDirectory in $layout.requiredDirectories) {
			$entryName = $relativeDirectory.Replace('\', '/').TrimEnd('/') + '/'
			[void] $archive.CreateEntry($entryName)
		}
		foreach ($file in $identityFiles) {
			$entry = $archive.CreateEntry($file.Name.Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal)
			$input = [IO.File]::OpenRead((Join-Path $package ($file.Name.Replace('/', [IO.Path]::DirectorySeparatorChar))))
			try {
				$entryStream = $entry.Open()
				try { $input.CopyTo($entryStream) } finally { $entryStream.Dispose() }
			}
			finally { $input.Dispose() }
		}
	}
	finally { $archive.Dispose() }
}
finally { $fileStream.Dispose() }

Write-Host "Validated and packaged $($identityFiles.Count) Maniacs 241029 fingerprinted files. No vanilla Steam source files were included."
