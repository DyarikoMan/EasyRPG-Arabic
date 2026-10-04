$ErrorActionPreference = 'Stop'

$toolsDirectory = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$arabicDirectory = Split-Path -Parent $toolsDirectory
$updateScript = Join-Path $toolsDirectory 'update-arabic.ps1'
$canonicalPath = Join-Path $arabicDirectory 'terminology\canonical-terms.json'
$canonical = Get-Content -LiteralPath $canonicalPath -Raw -Encoding UTF8 | ConvertFrom-Json

if ($canonical.'terms.gold' -cne 'G') {
	throw 'Fixture requires terms.gold to be the canonical value G.'
}

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('EasyRpgArabicPoTest-' + [guid]::NewGuid().ToString('N'))
$gamePath = Join-Path $fixtureRoot 'Game'
$languageDirectory = Join-Path $gamePath 'Language\ar'
$poPath = Join-Path $languageDirectory 'RPG_RT.ldb.po'
$null = New-Item -ItemType Directory -Force -Path $languageDirectory

$overrideValue = [string][char]0x0630
$fixtureLines = @(
	'msgctxt "terms.hp_short"',
	'msgid "HP"',
	'msgstr ""',
	'',
	'msgctxt "terms.gold"',
	'msgid "G"',
	'msgstr ""',
	('"' + $overrideValue + '"'),
	'',
	'msgctxt "terms.exp_short"',
	'msgid "EXP"',
	'msgstr ""',
	'"custom "',
	'"multiline"',
	'',
	'msgctxt "terms.level"',
	'msgid "Level"',
	'msgstr "quote: \" and path: C:\\tmp"',
	'',
	'msgctxt "game.actor.1"',
	'msgid "Hero"',
	'msgstr "custom game translation"',
	'',
	'msgctxt "game.actor.2"',
	'msgid "New actor"',
	'msgstr ""',
	''
)
$fixtureBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($fixtureLines -join "`r`n")
$poWithBom = [byte[]]::new($fixtureBytes.Length + 3)
$poWithBom[0] = 0xEF
$poWithBom[1] = 0xBB
$poWithBom[2] = 0xBF
[Array]::Copy($fixtureBytes, 0, $poWithBom, 3, $fixtureBytes.Length)
[System.IO.File]::WriteAllBytes($poPath, $poWithBom)

$oldPath = $env:PATH
try {
	# Keep this test independent of any LcfTrans installation on the test machine.
	$env:PATH = Join-Path $fixtureRoot 'empty-path'
	& $updateScript -GamePath $gamePath

	$defaultPo = [System.IO.File]::ReadAllText($poPath, [System.Text.Encoding]::UTF8)
	if (-not $defaultPo.Contains(('msgctxt "terms.hp_short"' + "`r`n" + 'msgid "HP"' + "`r`n" + 'msgstr "' + $canonical.'terms.hp_short' + '"'))) {
		throw 'Empty canonical msgstr was not populated.'
	}
	if (-not $defaultPo.Contains(('msgctxt "terms.gold"' + "`r`n" + 'msgid "G"' + "`r`n" + 'msgstr ""' + "`r`n" + '"' + $overrideValue + '"'))) {
		throw 'Multiline Arabic game override for terms.gold was not preserved.'
	}
	if (-not $defaultPo.Contains(('msgctxt "terms.exp_short"' + "`r`n" + 'msgid "EXP"' + "`r`n" + 'msgstr ""' + "`r`n" + '"custom "' + "`r`n" + '"multiline"'))) {
		throw 'Existing multiline translated value was not preserved.'
	}
	if (-not $defaultPo.Contains('msgstr "quote: \" and path: C:\\tmp"')) {
		throw 'Escaped quotes or backslashes were not preserved.'
	}
	if (-not $defaultPo.Contains('msgctxt "game.actor.1"' + "`r`n" + 'msgid "Hero"' + "`r`n" + 'msgstr "custom game translation"')) {
		throw 'Game-specific translation changed.'
	}
	if (-not $defaultPo.Contains('msgctxt "game.actor.2"' + "`r`n" + 'msgid "New actor"' + "`r`n" + 'msgstr ""')) {
		throw 'Untranslated game-specific entry changed.'
	}
	if ($defaultPo.Contains(('msgstr "' + $canonical.'terms.hp_short' + '\n"'))) {
		throw 'Canonical insertion added an accidental final newline.'
	}

	$defaultBytes = [System.IO.File]::ReadAllBytes($poPath)
	if ($defaultBytes.Length -ge 3 -and $defaultBytes[0] -eq 0xEF -and $defaultBytes[1] -eq 0xBB -and $defaultBytes[2] -eq 0xBF) {
		throw 'Updated PO contains a UTF-8 BOM.'
	}

	& $updateScript -GamePath $gamePath -ForceCanonical
	$forcedPo = [System.IO.File]::ReadAllText($poPath, [System.Text.Encoding]::UTF8)
	if (-not $forcedPo.Contains('msgctxt "terms.gold"' + "`r`n" + 'msgid "G"' + "`r`n" + 'msgstr "G"')) {
		throw '-ForceCanonical did not replace the terms.gold override.'
	}
	if (-not $forcedPo.Contains('msgctxt "game.actor.1"' + "`r`n" + 'msgid "Hero"' + "`r`n" + 'msgstr "custom game translation"')) {
		throw '-ForceCanonical changed a game-specific translation.'
	}

	Write-Host 'PASS: multiline PO parsing, canonical insertion, override preservation, escaping, UTF-8, and -ForceCanonical.' -ForegroundColor Green
} finally {
	$env:PATH = $oldPath
	if (Test-Path -LiteralPath $fixtureRoot -PathType Container) {
		$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
		$resolvedFixture = (Resolve-Path -LiteralPath $fixtureRoot).ProviderPath
		if (-not $resolvedFixture.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
			throw "Refusing to remove fixture outside the temporary directory: $resolvedFixture"
		}
		Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
	}
}
