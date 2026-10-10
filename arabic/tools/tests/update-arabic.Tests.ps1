$ErrorActionPreference = 'Stop'

$toolsDirectory = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$arabicDirectory = Split-Path -Parent $toolsDirectory
$updateScript = Join-Path $toolsDirectory 'update-arabic.ps1'
$canonicalPath = Join-Path $arabicDirectory 'terminology\canonical-terms.json'
$canonical = Get-Content -LiteralPath $canonicalPath -Raw -Encoding UTF8 | ConvertFrom-Json

if ($canonical.'terms.gold' -cne 'G') {
	throw 'Fixture requires terms.gold to be the canonical value G.'
}
$canonicalTermCount = @($canonical.PSObject.Properties | Where-Object { $_.Name.StartsWith('terms.', [System.StringComparison]::Ordinal) }).Count
if ($canonicalTermCount -ne 143) {
	throw "Expected 143 canonical terms, found $canonicalTermCount."
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
	'',
	'msgctxt "game.dialogue.1"',
	'msgid "Greeting"',
	'msgstr "السلام"',
	''
)
$fixtureBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($fixtureLines -join "`r`n")
$poWithBom = [byte[]]::new($fixtureBytes.Length + 3)
$poWithBom[0] = 0xEF
$poWithBom[1] = 0xBB
$poWithBom[2] = 0xBF
[Array]::Copy($fixtureBytes, 0, $poWithBom, 3, $fixtureBytes.Length)
[System.IO.File]::WriteAllBytes($poPath, $poWithBom)
$iniPath = Join-Path $gamePath 'RPG_RT.ini'
[System.IO.File]::WriteAllText($iniPath, "[RPG_RT]`r`nGameTitle=مشروع جديد`r`nWinW=320`r`n[Other]`r`nKeep=unchanged`r`n", [System.Text.UTF8Encoding]::new($false))

$oldPath = $env:PATH
try {
	# Keep this test independent of any LcfTrans installation on the test machine.
	$env:PATH = Join-Path $fixtureRoot 'empty-path'
	& $updateScript -GamePath $gamePath -InitializeNewProject
	$initializedIni = [System.IO.File]::ReadAllText($iniPath, [System.Text.Encoding]::UTF8)
	if ($initializedIni -notmatch '(?m)^WinW=426\r?$' -or $initializedIni -notmatch '(?m)^WinH=240\r?$') {
		throw "New-project initialization did not set RPG_RT.ini to 426x240. Current INI: $initializedIni"
	}
	if (-not $initializedIni.Contains('GameTitle=مشروع جديد') -or -not $initializedIni.Contains("[Other]`r`nKeep=unchanged")) {
		throw 'New-project initialization changed unrelated RPG_RT.ini values.'
	}
	$fontPath = Join-Path $languageDirectory 'Font\Font.ttf'
	if (-not (Test-Path -LiteralPath $fontPath -PathType Leaf) -or
		(Get-FileHash -LiteralPath $fontPath -Algorithm SHA256).Hash -ne '68B412A4D8443FE677DD37412BB44D117CADE607572B2CC9689177DA3934952F') {
		throw 'New-project initialization did not copy the historical Arabic PixelSmall font.'
	}

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
	if (-not $defaultPo.Contains('msgctxt "game.dialogue.1"' + "`r`n" + 'msgid "Greeting"' + "`r`n" + 'msgstr "السلام"')) {
		throw 'Existing Arabic dialogue did not survive a later updater run.'
	}
	if ($defaultPo.Contains(('msgstr "' + $canonical.'terms.hp_short' + '\n"'))) {
		throw 'Canonical insertion added an accidental final newline.'
	}

	$defaultBytes = [System.IO.File]::ReadAllBytes($poPath)
	if ($defaultBytes.Length -ge 3 -and $defaultBytes[0] -eq 0xEF -and $defaultBytes[1] -eq 0xBB -and $defaultBytes[2] -eq 0xBF) {
		throw 'Updated PO contains a UTF-8 BOM.'
	}

	[System.IO.File]::WriteAllText($iniPath, "[RPG_RT]`r`nGameTitle=Keep title`r`nWinW=320`r`nWinH=240`r`n", [System.Text.Encoding]::ASCII)
	& $updateScript -GamePath $gamePath -ForceCanonical
	$existingIni = [System.IO.File]::ReadAllText($iniPath, [System.Text.Encoding]::ASCII)
	if ($existingIni -notmatch '(?m)^WinW=320\r?$' -or $existingIni -notmatch '(?m)^WinH=240\r?$' -or -not $existingIni.Contains('GameTitle=Keep title')) {
		throw 'A normal updater run changed an existing project resolution or title.'
	}
	$forcedPo = [System.IO.File]::ReadAllText($poPath, [System.Text.Encoding]::UTF8)
	if (-not $forcedPo.Contains('msgctxt "terms.gold"' + "`r`n" + 'msgid "G"' + "`r`n" + 'msgstr "G"')) {
		throw '-ForceCanonical did not replace the terms.gold override.'
	}
	if (-not $forcedPo.Contains('msgctxt "game.actor.1"' + "`r`n" + 'msgid "Hero"' + "`r`n" + 'msgstr "custom game translation"')) {
		throw '-ForceCanonical changed a game-specific translation.'
	}

	# A deterministic command shim confirms that the updater selects Windows-1256.
	$stubPath = Join-Path $fixtureRoot 'lcftrans-stub.cmd'
	$stubContent = @'
@echo off
echo %*>>"%~dp0lcftrans-arguments.txt"
exit /b 0
'@
	[System.IO.File]::WriteAllText($stubPath, $stubContent, [System.Text.Encoding]::ASCII)
	$runtimeLogPath = Join-Path $fixtureRoot 'runtime-updater.log'
	& $updateScript -GamePath $gamePath -LcfTransPath $stubPath -RuntimeLogPath $runtimeLogPath
	$stubArguments = [System.IO.File]::ReadAllText((Join-Path $fixtureRoot 'lcftrans-arguments.txt'))
	if ($stubArguments -notmatch '(?m)(^|\s)-e\s+1256(\s|$)') {
		throw "LcfTrans was not invoked with Windows-1256. Captured arguments: $stubArguments"
	}
	if (-not (Test-Path -LiteralPath $runtimeLogPath -PathType Leaf) -or
		-not [System.IO.File]::ReadAllText($runtimeLogPath).Contains('Arabic translation update summary')) {
		throw 'The hidden runtime updater transcript did not capture its completion summary.'
	}

	$customGame = Join-Path $fixtureRoot 'CustomFontGame'
	$customArabic = Join-Path $customGame 'Language\ar'
	$customFontDirectory = Join-Path $customArabic 'Font'
	$null = New-Item -ItemType Directory -Force -Path $customFontDirectory
	$customPo = 'msgctxt "terms.gold"' + [Environment]::NewLine + 'msgid "G"' + [Environment]::NewLine + 'msgstr ""' + [Environment]::NewLine
	[System.IO.File]::WriteAllText((Join-Path $customArabic 'RPG_RT.ldb.po'), $customPo, [System.Text.Encoding]::UTF8)
	$customFontPath = Join-Path $customFontDirectory 'Font.ttf'
	$customFontBytes = [byte[]]@(0x43, 0x55, 0x53, 0x54, 0x4F, 0x4D)
	[System.IO.File]::WriteAllBytes($customFontPath, $customFontBytes)
	& $updateScript -GamePath $customGame -InitializeNewProject
	if ([Convert]::ToBase64String([System.IO.File]::ReadAllBytes($customFontPath)) -ne [Convert]::ToBase64String($customFontBytes)) {
		throw 'New-project initialization overwrote an existing custom font.'
	}

	Write-Host 'PASS: multiline PO parsing, 143 canonical terms, Arabic preservation, font copy/no-overwrite, new-project 426x240 initialization, existing resolution preservation, escaping, UTF-8, -ForceCanonical, LcfTrans CP1256 selection, and runtime transcript.' -ForegroundColor Green
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
