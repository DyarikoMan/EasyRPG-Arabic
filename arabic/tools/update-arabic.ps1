[CmdletBinding()]
param (
	[Parameter(Mandatory = $true, Position = 0)]
	[ValidateNotNullOrEmpty()]
	[string]$GamePath,

	[Parameter()]
	[string]$LcfTransPath,

	[Parameter()]
	[string]$RuntimeLogPath,

	[Parameter()]
	[switch]$InitializeNewProject,

	[Parameter()]
	[switch]$ForceCanonical
)

$ErrorActionPreference = 'Stop'
$runtimeTranscriptStarted = $false
if (-not [string]::IsNullOrWhiteSpace($RuntimeLogPath)) {
	$runtimeLogFullPath = [System.IO.Path]::GetFullPath($RuntimeLogPath)
	$runtimeLogDirectory = [System.IO.Path]::GetDirectoryName($runtimeLogFullPath)
	if (-not [string]::IsNullOrWhiteSpace($runtimeLogDirectory)) {
		New-Item -ItemType Directory -Force -Path $runtimeLogDirectory | Out-Null
	}
	Start-Transcript -Path $runtimeLogFullPath -Append | Out-Null
	$runtimeTranscriptStarted = $true
}

function Resolve-LcfTransExecutable {
	param (
		[string]$ExplicitPath,
		[string]$ToolsDirectory
	)

	if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
		$candidate = $ExplicitPath
		if (Test-Path -LiteralPath $candidate -PathType Container) {
			$candidate = Join-Path $candidate 'lcftrans.exe'
		}
		if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
			throw "LcfTrans was not found at '$candidate'. Pass -LcfTransPath as the executable path or its containing folder."
		}
		return (Resolve-Path -LiteralPath $candidate).ProviderPath
	}

	$localCandidate = Join-Path $ToolsDirectory 'lcftrans.exe'
	if (Test-Path -LiteralPath $localCandidate -PathType Leaf) {
		return (Resolve-Path -LiteralPath $localCandidate).ProviderPath
	}

	$command = Get-Command 'lcftrans.exe' -CommandType Application -ErrorAction SilentlyContinue
	if ($command) {
		return $command.Source
	}

	return $null
}

function ConvertTo-PoQuotedString {
	param ([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)

	$escaped = [System.Text.StringBuilder]::new()
	foreach ($character in $Value.ToCharArray()) {
		switch ($character) {
			'"' { $null = $escaped.Append('\"'); continue }
			'\' { $null = $escaped.Append('\\'); continue }
			"`a" { $null = $escaped.Append('\a'); continue }
			"`b" { $null = $escaped.Append('\b'); continue }
			"`f" { $null = $escaped.Append('\f'); continue }
			"`n" { $null = $escaped.Append('\n'); continue }
			"`r" { $null = $escaped.Append('\r'); continue }
			"`t" { $null = $escaped.Append('\t'); continue }
			"`v" { $null = $escaped.Append('\v'); continue }
			default { $null = $escaped.Append($character) }
		}
	}
	return '"' + $escaped.ToString() + '"'
}

function ConvertFrom-PoQuotedString {
	param ([Parameter(Mandatory = $true)][string]$QuotedString)

	if ($QuotedString.Length -lt 2 -or $QuotedString[0] -ne '"' -or $QuotedString[$QuotedString.Length - 1] -ne '"') {
		throw 'Invalid PO quoted string.'
	}

	$value = [System.Text.StringBuilder]::new()
	for ($index = 1; $index -lt $QuotedString.Length - 1; $index++) {
		$character = $QuotedString[$index]
		if ($character -ne '\') {
			$null = $value.Append($character)
			continue
		}

		$index++
		if ($index -ge $QuotedString.Length - 1) {
			throw 'PO quoted string ends with an incomplete escape sequence.'
		}
		$escape = $QuotedString[$index]
		switch ($escape) {
			'"' { $null = $value.Append('"'); continue }
			'\' { $null = $value.Append('\'); continue }
			'a' { $null = $value.Append([char]7); continue }
			'b' { $null = $value.Append([char]8); continue }
			'f' { $null = $value.Append([char]12); continue }
			'n' { $null = $value.Append("`n"); continue }
			'r' { $null = $value.Append("`r"); continue }
			't' { $null = $value.Append("`t"); continue }
			'v' { $null = $value.Append([char]11); continue }
			'x' {
				$hexStart = $index + 1
				$hexEnd = $hexStart
				while ($hexEnd -lt $QuotedString.Length - 1 -and [Uri]::IsHexDigit($QuotedString[$hexEnd])) {
					$hexEnd++
				}
				if ($hexEnd -eq $hexStart) {
					throw 'PO hexadecimal escape has no digits.'
				}
				$code = [Convert]::ToInt32($QuotedString.Substring($hexStart, $hexEnd - $hexStart), 16)
				if ($code -gt 0xFFFF) {
					throw 'PO hexadecimal escape is outside the supported UTF-16 character range.'
				}
				$null = $value.Append([char]$code)
				$index = $hexEnd - 1
				continue
			}
			default {
				$code = [int][char]$escape
				if ($code -lt 48 -or $code -gt 55) {
					throw "Unsupported PO escape sequence: \$escape"
				}
				$octalStart = $index
				$octalEnd = $octalStart
				while ($octalEnd -lt $QuotedString.Length - 1 -and $octalEnd -lt $octalStart + 3) {
					$octalCode = [int][char]$QuotedString[$octalEnd]
					if ($octalCode -lt 48 -or $octalCode -gt 55) { break }
					$octalEnd++
				}
				$octal = [Convert]::ToInt32($QuotedString.Substring($octalStart, $octalEnd - $octalStart), 8)
				$null = $value.Append([char]$octal)
				$index = $octalEnd - 1
				continue
			}
		}
	}

	return $value.ToString()
}

function Get-PoStringField {
	param (
		[Parameter(Mandatory = $true)][AllowEmptyString()][string]$Block,
		[Parameter(Mandatory = $true)][ValidateSet('msgctxt', 'msgstr')][string]$FieldName
	)

	$fieldPattern = '(?m)^(?<indent>[ \t]*)' + [regex]::Escape($FieldName) + '[ \t]+"(?:\\.|[^"\\])*"(?:(?:\r\n|\n|\r)[ \t]*"(?:\\.|[^"\\])*")*'
	$fieldMatch = [regex]::Match($Block, $fieldPattern)
	if (-not $fieldMatch.Success) {
		return $null
	}

	$segments = [regex]::Matches($fieldMatch.Value, '"(?:\\.|[^"\\])*"')
	$decoded = [System.Text.StringBuilder]::new()
	foreach ($segment in $segments) {
		$null = $decoded.Append((ConvertFrom-PoQuotedString -QuotedString $segment.Value))
	}

	return [pscustomobject]@{
		Match = $fieldMatch
		Indent = $fieldMatch.Groups['indent'].Value
		Value = $decoded.ToString()
	}
}

function Split-PoContent {
	param ([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Content)

	# Keep the blank-line separators as captured parts so untouched PO text remains byte-for-byte stable.
	# Atomic line-break alternatives prevent a CRLF from backtracking into separate CR and LF breaks.
	return [regex]::Split($Content, '((?>\r\n|\n|\r)[ \t]*(?:(?>\r\n|\n|\r)[ \t]*)+)')
}

function Add-UpdatedFile {
	param (
		[Parameter(Mandatory = $true)][string]$Path,
		[Parameter(Mandatory = $true)][string]$LanguageDirectory,
		[Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Files
	)

	$prefix = $LanguageDirectory.TrimEnd('\', '/')
	if ($Path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
		$relative = $Path.Substring($prefix.Length).TrimStart([char[]]@('\', '/'))
		$displayPath = Join-Path 'Language\ar' $relative
	} else {
		$displayPath = $Path
	}

	if (-not $Files.Contains($displayPath)) {
		$Files.Add($displayPath)
	}
}

function Get-PoFileHashes {
	param ([Parameter(Mandatory = $true)][string]$Directory)

	$hashes = @{}
	if (Test-Path -LiteralPath $Directory -PathType Container) {
		$poFiles = Get-ChildItem -LiteralPath $Directory -Filter '*.po' -File -Recurse -ErrorAction SilentlyContinue
		foreach ($poFile in $poFiles) {
			$stream = [System.IO.File]::OpenRead($poFile.FullName)
			try {
				$sha256 = [System.Security.Cryptography.SHA256]::Create()
				try {
					$hashes[$poFile.FullName] = [System.BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '')
				} finally {
					$sha256.Dispose()
				}
			} finally {
				$stream.Dispose()
			}
		}
	}
	return $hashes
}

function Update-CanonicalPoTerms {
	param (
		[Parameter(Mandatory = $true)][string]$Content,
		[Parameter(Mandatory = $true)][System.Collections.Generic.Dictionary[string, string]]$Terms,
		[Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Warnings,
		[switch]$ForceCanonical
	)

	$parts = Split-PoContent -Content $Content
	$output = [System.Text.StringBuilder]::new()
	$contextsFound = 0
	$translationsInserted = 0
	$existingTranslationsPreserved = 0
	$translationsOverwritten = 0
	$foundTerms = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

	for ($index = 0; $index -lt $parts.Length; $index += 2) {
		$block = $parts[$index]

		$contextField = $null
		try {
			$contextField = Get-PoStringField -Block $block -FieldName 'msgctxt'
		} catch {
			if ($block -match '(?m)^[ \t]*msgctxt[ \t]+"terms\.') {
				$Warnings.Add("A terms.* msgctxt contains invalid PO escaping and was skipped: $($_.Exception.Message)")
			}
		}

		if ($null -ne $contextField -and $contextField.Value.StartsWith('terms.', [System.StringComparison]::Ordinal) -and $Terms.ContainsKey($contextField.Value)) {
			$context = $contextField.Value
			$contextsFound++
			$null = $foundTerms.Add($context)

			$msgstrField = $null
		$msgstrParseFailed = $false
			try {
				$msgstrField = Get-PoStringField -Block $block -FieldName 'msgstr'
			} catch {
				$msgstrParseFailed = $true
				$Warnings.Add("Canonical context '$context' has invalid PO msgstr escaping and was skipped: $($_.Exception.Message)")
			}

			if ($null -eq $msgstrField -and -not $msgstrParseFailed) {
				$Warnings.Add("Canonical context '$context' has no msgstr field and was skipped.")
			} elseif ($null -ne $msgstrField) {
				$canonicalValue = $Terms[$context]
				$shouldReplace = $false
				if ($msgstrField.Value.Length -eq 0) {
					if ($canonicalValue.Length -gt 0) {
						$shouldReplace = $true
						$translationsInserted++
					}
				} elseif ($ForceCanonical) {
					if (-not [string]::Equals($msgstrField.Value, $canonicalValue, [System.StringComparison]::Ordinal)) {
						$shouldReplace = $true
						$translationsOverwritten++
					} else {
						$existingTranslationsPreserved++
					}
				} else {
					$existingTranslationsPreserved++
				}

				if ($shouldReplace) {
					$replacement = $msgstrField.Indent + 'msgstr ' + (ConvertTo-PoQuotedString -Value $canonicalValue)
					$block = $block.Remove($msgstrField.Match.Index, $msgstrField.Match.Length).Insert($msgstrField.Match.Index, $replacement)
			}
			}
		}

		$null = $output.Append($block)
		if ($index + 1 -lt $parts.Length) {
			$null = $output.Append($parts[$index + 1])
		}
	}

	$missingTerms = 0
	foreach ($term in $Terms.Keys) {
		if (-not $foundTerms.Contains($term)) {
			$missingTerms++
		}
	}

	return [pscustomobject]@{
		Content = $output.ToString()
		ContextsFound = $contextsFound
		TranslationsInserted = $translationsInserted
		ExistingTranslationsPreserved = $existingTranslationsPreserved
		TranslationsOverwritten = $translationsOverwritten
		CanonicalEntriesMissing = $missingTerms
	}
}

function Get-UntranslatedPoEntryCount {
	param ([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Content)

	$emptyCount = 0
	$parts = Split-PoContent -Content $Content

	for ($index = 0; $index -lt $parts.Length; $index += 2) {
		$block = $parts[$index]
		try {
			$contextField = Get-PoStringField -Block $block -FieldName 'msgctxt'
		} catch {
			continue
		}
		if ($null -eq $contextField) {
			continue
		}

		try {
			$msgstrField = Get-PoStringField -Block $block -FieldName 'msgstr'
		} catch {
			$emptyCount++
			continue
		}
		if ($null -eq $msgstrField -or $msgstrField.Value.Length -eq 0) {
			$emptyCount++
		}
	}

	return $emptyCount
}

function Set-NewProjectResolution {
	param ([Parameter(Mandatory = $true)][string]$Directory)

	$iniPath = Join-Path $Directory 'RPG_RT.ini'
	if (-not (Test-Path -LiteralPath $iniPath -PathType Leaf)) {
		[System.IO.File]::WriteAllText($iniPath, "[RPG_RT]`r`nWinW=426`r`nWinH=240`r`n", [System.Text.Encoding]::ASCII)
		return
	}

	$bytes = [System.IO.File]::ReadAllBytes($iniPath)
	$prefixLength = 0
	$encoding = [System.Text.Encoding]::GetEncoding(28591)
	if ($bytes.Length -ge 4 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE -and $bytes[2] -eq 0x00 -and $bytes[3] -eq 0x00) {
		$prefixLength = 4
		$encoding = [System.Text.UTF32Encoding]::new($false, $false)
	} elseif ($bytes.Length -ge 4 -and $bytes[0] -eq 0x00 -and $bytes[1] -eq 0x00 -and $bytes[2] -eq 0xFE -and $bytes[3] -eq 0xFF) {
		$prefixLength = 4
		$encoding = [System.Text.UTF32Encoding]::new($true, $false)
	} elseif ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
		$prefixLength = 3
		$encoding = [System.Text.UTF8Encoding]::new($false)
	} elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
		$prefixLength = 2
		$encoding = [System.Text.UnicodeEncoding]::new($false, $false)
	} elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
		$prefixLength = 2
		$encoding = [System.Text.UnicodeEncoding]::new($true, $false)
	}

	$content = $encoding.GetString($bytes, $prefixLength, $bytes.Length - $prefixLength)
	$newlineMatch = [regex]::Match($content, "`r`n|`n|`r")
	$newline = if ($newlineMatch.Success) { $newlineMatch.Value } else { "`r`n" }
	$hadFinalNewline = [regex]::IsMatch($content, '(?:\r\n|\n|\r)$')
	$lines = [System.Collections.Generic.List[string]]::new()
	foreach ($line in [regex]::Split($content, "`r`n|`n|`r")) { $lines.Add($line) }
	if ($hadFinalNewline -and $lines.Count -gt 0 -and $lines[$lines.Count - 1].Length -eq 0) {
		$lines.RemoveAt($lines.Count - 1)
	}

	$headerPattern = '^\s*\[[^\]]+\]\s*$'
	$targetSection = -1
	for ($index = 0; $index -lt $lines.Count; $index++) {
		if ($lines[$index] -match '^\s*\[RPG_RT\]\s*$') {
			$targetSection = $index
			break
		}
	}

	if ($targetSection -lt 0) {
		if ($lines.Count -gt 0 -and $lines[$lines.Count - 1].Length -gt 0) { $lines.Add('') }
		$lines.Add('[RPG_RT]')
		$lines.Add('WinW=426')
		$lines.Add('WinH=240')
		$hadFinalNewline = $true
	} else {
		$sectionEnd = $targetSection + 1
		while ($sectionEnd -lt $lines.Count -and $lines[$sectionEnd] -notmatch $headerPattern) { $sectionEnd++ }
		$hasWidth = $false
		$hasHeight = $false
		for ($index = $targetSection + 1; $index -lt $sectionEnd; $index++) {
			if ($lines[$index] -match '^\s*WinW\s*=') { $lines[$index] = 'WinW=426'; $hasWidth = $true }
			elseif ($lines[$index] -match '^\s*WinH\s*=') { $lines[$index] = 'WinH=240'; $hasHeight = $true }
		}
		$insertAt = $sectionEnd
		if (-not $hasWidth) { $lines.Insert($insertAt, 'WinW=426'); $insertAt++ }
		if (-not $hasHeight) { $lines.Insert($insertAt, 'WinH=240') }
	}

	$updatedContent = [string]::Join($newline, $lines)
	if ($hadFinalNewline) { $updatedContent += $newline }
	$encoded = $encoding.GetBytes($updatedContent)
	$output = [byte[]]::new($prefixLength + $encoded.Length)
	if ($prefixLength -gt 0) { [Array]::Copy($bytes, 0, $output, 0, $prefixLength) }
	[Array]::Copy($encoded, 0, $output, $prefixLength, $encoded.Length)
	[System.IO.File]::WriteAllBytes($iniPath, $output)
}

$toolsDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$arabicDirectory = Split-Path -Parent $toolsDirectory
$templateLanguageDirectory = Join-Path $arabicDirectory 'template\Language\ar'
$canonicalTermsPath = Join-Path $arabicDirectory 'terminology\canonical-terms.json'

if (-not (Test-Path -LiteralPath $GamePath -PathType Container)) {
	throw "Game directory does not exist: $GamePath"
}
$gameDirectory = (Resolve-Path -LiteralPath $GamePath).ProviderPath
$languageDirectory = Join-Path $gameDirectory 'Language\ar'
$databasePoPath = Join-Path $languageDirectory 'RPG_RT.ldb.po'

if (-not (Test-Path -LiteralPath $templateLanguageDirectory -PathType Container)) {
	throw "Arabic template directory was not found: $templateLanguageDirectory"
}
if (-not (Test-Path -LiteralPath $canonicalTermsPath -PathType Leaf)) {
	throw "Canonical terminology file was not found: $canonicalTermsPath"
}

$null = New-Item -ItemType Directory -Force -Path (Join-Path $languageDirectory 'Font')
$filesUpdated = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

if ($InitializeNewProject) {
	Set-NewProjectResolution -Directory $gameDirectory
	Write-Host 'Initialized new project resolution to 426x240.'
}

Get-ChildItem -LiteralPath $templateLanguageDirectory -File -Recurse | ForEach-Object {
	$relativePath = $_.FullName.Substring($templateLanguageDirectory.TrimEnd('\', '/').Length).TrimStart([char[]]@('\', '/'))
	$destination = Join-Path $languageDirectory $relativePath
	$destinationDirectory = Split-Path -Parent $destination
	$null = New-Item -ItemType Directory -Force -Path $destinationDirectory
	if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
		Copy-Item -LiteralPath $_.FullName -Destination $destination
		Add-UpdatedFile -Path $destination -LanguageDirectory $languageDirectory -Files $filesUpdated
	}
}

$canonicalJson = Get-Content -LiteralPath $canonicalTermsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$canonicalTerms = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
foreach ($property in $canonicalJson.PSObject.Properties) {
	if (-not $property.Name.StartsWith('terms.', [System.StringComparison]::Ordinal)) {
		continue
	}
	if ($property.Value -isnot [string]) {
		$warnings.Add("Canonical value for '$($property.Name)' is not a string and was skipped.")
		continue
	}
	$canonicalTerms.Add($property.Name, $property.Value)
}

$lcfTransExecutable = Resolve-LcfTransExecutable -ExplicitPath $LcfTransPath -ToolsDirectory $toolsDirectory
$poHashesBefore = Get-PoFileHashes -Directory $languageDirectory
$lcfTransRan = $false

if ($lcfTransExecutable) {
	Write-Host "Updating Arabic PO files with LcfTrans from '$languageDirectory'..." -ForegroundColor Cyan
	Push-Location -LiteralPath $languageDirectory
	try {
		& $lcfTransExecutable -u $gameDirectory -e 1256
		if ($LASTEXITCODE -ne 0) {
			throw "LcfTrans failed with exit code $LASTEXITCODE."
		}
		$lcfTransRan = $true
	} finally {
		Pop-Location
	}
} else {
	$message = "LcfTrans was not found. Place lcftrans.exe in '$toolsDirectory', add it to PATH, or pass -LcfTransPath. No binary was downloaded."
	$warnings.Add($message)
	Write-Warning $message
	if (-not (Test-Path -LiteralPath $databasePoPath -PathType Leaf)) {
		$warnings.Add('RPG_RT.ldb.po does not exist yet, so canonical terms could not be applied.')
	}
}

if ($lcfTransRan) {
	$poHashesAfterUpdate = Get-PoFileHashes -Directory $languageDirectory
	foreach ($path in $poHashesAfterUpdate.Keys) {
		if (-not $poHashesBefore.ContainsKey($path) -or $poHashesBefore[$path] -ne $poHashesAfterUpdate[$path]) {
			Add-UpdatedFile -Path $path -LanguageDirectory $languageDirectory -Files $filesUpdated
		}
	}
	foreach ($path in $poHashesBefore.Keys) {
		if (-not $poHashesAfterUpdate.ContainsKey($path)) {
			$warnings.Add("LcfTrans removed '$path'; review its .stale.po output if translations were unmatched.")
		}
	}
}

$canonicalContextsFound = 0
$canonicalTranslationsInserted = 0
$canonicalExistingTranslationsPreserved = 0
$canonicalTranslationsOverwritten = 0
$canonicalEntriesMissing = $canonicalTerms.Count
$untranslatedRemaining = 0
if (Test-Path -LiteralPath $databasePoPath -PathType Leaf) {
	$sourcePoBytes = [System.IO.File]::ReadAllBytes($databasePoPath)
	$hadUtf8Bom = $sourcePoBytes.Length -ge 3 -and $sourcePoBytes[0] -eq 0xEF -and $sourcePoBytes[1] -eq 0xBB -and $sourcePoBytes[2] -eq 0xBF
	$originalPoContent = [System.IO.File]::ReadAllText($databasePoPath, [System.Text.Encoding]::UTF8)
	$update = Update-CanonicalPoTerms -Content $originalPoContent -Terms $canonicalTerms -Warnings $warnings -ForceCanonical:$ForceCanonical
	$canonicalContextsFound = $update.ContextsFound
	$canonicalTranslationsInserted = $update.TranslationsInserted
	$canonicalExistingTranslationsPreserved = $update.ExistingTranslationsPreserved
	$canonicalTranslationsOverwritten = $update.TranslationsOverwritten
	$canonicalEntriesMissing = $update.CanonicalEntriesMissing
	if ($update.Content -ne $originalPoContent -or $hadUtf8Bom) {
		$temporaryPath = Join-Path $languageDirectory ('.RPG_RT.ldb.po.' + [guid]::NewGuid().ToString('N') + '.tmp')
		try {
			[System.IO.File]::WriteAllText($temporaryPath, $update.Content, [System.Text.UTF8Encoding]::new($false))
			Move-Item -LiteralPath $temporaryPath -Destination $databasePoPath -Force
			Add-UpdatedFile -Path $databasePoPath -LanguageDirectory $languageDirectory -Files $filesUpdated
		} finally {
			if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
				Remove-Item -LiteralPath $temporaryPath -Force
			}
		}
	}
	$finalPoContent = [System.IO.File]::ReadAllText($databasePoPath, [System.Text.Encoding]::UTF8)
	$untranslatedRemaining = Get-UntranslatedPoEntryCount -Content $finalPoContent
} elseif ($lcfTransRan) {
	$warnings.Add('LcfTrans completed but did not create Language\ar\RPG_RT.ldb.po.')
}

Write-Host ''
Write-Host 'Arabic translation update summary' -ForegroundColor Cyan
Write-Host "  Canonical contexts found: $canonicalContextsFound"
Write-Host "  Canonical translations inserted: $canonicalTranslationsInserted"
Write-Host "  Existing translations preserved: $canonicalExistingTranslationsPreserved"
if ($ForceCanonical) {
	Write-Host "  Existing translations overwritten: $canonicalTranslationsOverwritten"
}
Write-Host "  Canonical entries missing from PO: $canonicalEntriesMissing"
Write-Host "  Untranslated msgctxt entries remaining in RPG_RT.ldb.po: $untranslatedRemaining"
if ($filesUpdated.Count -gt 0) {
	Write-Host '  Files updated:'
	foreach ($file in $filesUpdated) {
		Write-Host "    $file"
	}
} else {
	Write-Host '  Files updated: none'
}
if ($warnings.Count -gt 0) {
	Write-Host '  Warnings:' -ForegroundColor Yellow
	foreach ($warning in $warnings) {
		Write-Host "    - $warning" -ForegroundColor Yellow
	}
} else {
	Write-Host '  Warnings: none'
}

if (-not $lcfTransRan -and -not (Test-Path -LiteralPath $databasePoPath -PathType Leaf)) {
	throw "Cannot update translations without LcfTrans or an existing RPG_RT.ldb.po. $($warnings[0])"
}
if ($runtimeTranscriptStarted) {
	Stop-Transcript | Out-Null
}

