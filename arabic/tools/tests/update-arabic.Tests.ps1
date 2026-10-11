$ErrorActionPreference = 'Stop'

$toolsDirectory = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$arabicDirectory = Split-Path -Parent $toolsDirectory
$updateScript = Join-Path $toolsDirectory 'update-arabic.ps1'
$defaultsPath = Join-Path $arabicDirectory 'new-project-defaults.ini'
$hadDefaultsBeforeTest = Test-Path -LiteralPath $defaultsPath -PathType Leaf
$defaultsBeforeTest = if ($hadDefaultsBeforeTest) { [System.IO.File]::ReadAllBytes($defaultsPath) } else { $null }
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
	if (Test-Path -LiteralPath $defaultsPath -PathType Leaf) { Remove-Item -LiteralPath $defaultsPath -Force }
	& $updateScript -GamePath $gamePath -InitializeNewProject
	$initializedIni = [System.IO.File]::ReadAllText($iniPath, [System.Text.Encoding]::UTF8)
	if ($initializedIni -notmatch '(?m)^WinW=426\r?$' -or $initializedIni -notmatch '(?m)^WinH=240\r?$') {
		throw "New-project initialization did not set RPG_RT.ini to 426x240. Current INI: $initializedIni"
	}
	if (-not $initializedIni.Contains('GameTitle=مشروع جديد') -or -not $initializedIni.Contains("[Other]`r`nKeep=unchanged")) {
		throw 'New-project initialization changed unrelated RPG_RT.ini values.'
	}
	foreach ($key in @('EightDirectionMovement', 'PixelMovement', 'RunWithShift', 'SmoothCamera')) {
		if ($initializedIni -notmatch ('(?im)^' + [regex]::Escape($key) + '=false\r?$')) { throw "Missing defaults did not explicitly initialize $key=false." }
	}

	$crlf = [string][char]13 + [string][char]10
	$freshGame = Join-Path $fixtureRoot 'FreshProjectAllOff'
	$null = New-Item -ItemType Directory -Force -Path $freshGame
	& $updateScript -GamePath $freshGame -InitializeNewProject
	$freshIni = [System.IO.File]::ReadAllText((Join-Path $freshGame 'RPG_RT.ini'), [System.Text.Encoding]::ASCII)
	if ($freshIni -notmatch '(?m)^WinW=426\r?$' -or $freshIni -notmatch '(?m)^WinH=240\r?$') { throw 'Fresh-project initialization did not retain 426x240 widescreen values.' }
	foreach ($key in @('EightDirectionMovement', 'PixelMovement', 'RunWithShift', 'SmoothCamera')) {
		if ($freshIni -notmatch ('(?im)^' + [regex]::Escape($key) + '=false\r?$')) { throw "Fresh all-off defaults did not explicitly initialize $key=false." }
	}

	$falseDefaults = [string]::Join($crlf, @('[EasyRPG]', 'EightDirectionMovement=false', 'PixelMovement=false', 'RunWithShift=false', 'SmoothCamera=false', ''))
	[System.IO.File]::WriteAllText($defaultsPath, $falseDefaults, [System.Text.Encoding]::ASCII)
	$explicitFalseGame = Join-Path $fixtureRoot 'ExplicitFalseProject'
	$null = New-Item -ItemType Directory -Force -Path $explicitFalseGame
	& $updateScript -GamePath $explicitFalseGame -InitializeNewProject
	$explicitFalseIni = [System.IO.File]::ReadAllText((Join-Path $explicitFalseGame 'RPG_RT.ini'), [System.Text.Encoding]::ASCII)
	foreach ($key in @('EightDirectionMovement', 'PixelMovement', 'RunWithShift', 'SmoothCamera')) {
		if ($explicitFalseIni -notmatch ('(?im)^' + [regex]::Escape($key) + '=false\r?$')) { throw "Explicit false defaults did not preserve $key=false." }
	}

	$trueDefaults = [string]::Join($crlf, @('[EasyRPG]', 'EightDirectionMovement=true', 'PixelMovement=true', 'RunWithShift=true', 'SmoothCamera=true', ''))
	[System.IO.File]::WriteAllText($defaultsPath, $trueDefaults, [System.Text.Encoding]::ASCII)
	$allOnGame = Join-Path $fixtureRoot 'AllMovementOnProject'
	$null = New-Item -ItemType Directory -Force -Path $allOnGame
	$lf = [string][char]10
	$allOnIniInput = [string]::Join($lf, @('[RPG_RT]', 'GameTitle=Keep title', 'WinW=320', '[Other]', 'Keep=unchanged'))
	$inputBody = [System.Text.UTF8Encoding]::new($false).GetBytes($allOnIniInput)
	$inputWithBom = [byte[]]::new($inputBody.Length + 3)
	$inputWithBom[0] = 0xEF; $inputWithBom[1] = 0xBB; $inputWithBom[2] = 0xBF
	[Array]::Copy($inputBody, 0, $inputWithBom, 3, $inputBody.Length)
	$allOnIniPath = Join-Path $allOnGame 'RPG_RT.ini'
	[System.IO.File]::WriteAllBytes($allOnIniPath, $inputWithBom)
	& $updateScript -GamePath $allOnGame -InitializeNewProject
	$allOnBytes = [System.IO.File]::ReadAllBytes($allOnIniPath)
	if ($allOnBytes[0] -ne 0xEF -or $allOnBytes[1] -ne 0xBB -or $allOnBytes[2] -ne 0xBF) { throw 'New-project initialization did not preserve the UTF-8 BOM.' }
	$allOnIni = [System.Text.UTF8Encoding]::new($false).GetString($allOnBytes, 3, $allOnBytes.Length - 3)
	if ($allOnIni.Contains([string][char]13) -or $allOnIni.EndsWith($lf)) { throw 'New-project initialization did not preserve LF newlines and the missing final newline.' }
	if (-not $allOnIni.Contains('[Other]' + $lf + 'Keep=unchanged') -or -not $allOnIni.Contains('GameTitle=Keep title') -or
		$allOnIni -notmatch '(?m)^WinW=426$' -or $allOnIni -notmatch '(?m)^WinH=240$') { throw 'Movement defaults changed unrelated INI content or widescreen values.' }
	foreach ($key in @('EightDirectionMovement', 'PixelMovement', 'RunWithShift', 'SmoothCamera')) {
		if ($allOnIni -notmatch ('(?im)^' + [regex]::Escape($key) + '=true$')) { throw "All-on defaults did not set $key=true." }
	}

	$invalidDefaults = [string]::Join($crlf, @('[EasyRPG]', 'EightDirectionMovement=true', 'PixelMovement=invalid', 'RunWithShift=true', 'SmoothCamera=true', ''))
	[System.IO.File]::WriteAllText($defaultsPath, $invalidDefaults, [System.Text.Encoding]::ASCII)
	$invalidDefaultsGame = Join-Path $fixtureRoot 'InvalidDefaultsProject'
	$null = New-Item -ItemType Directory -Force -Path $invalidDefaultsGame
	& $updateScript -GamePath $invalidDefaultsGame -InitializeNewProject
	$invalidDefaultsIni = [System.IO.File]::ReadAllText((Join-Path $invalidDefaultsGame 'RPG_RT.ini'), [System.Text.Encoding]::ASCII)
	foreach ($key in @('EightDirectionMovement', 'PixelMovement', 'RunWithShift', 'SmoothCamera')) {
		if ($invalidDefaultsIni -notmatch ('(?im)^' + [regex]::Escape($key) + '=false\r?$')) { throw "Invalid managed defaults did not fall back to all false ($key)." }
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

	# A normal updater run must preserve movement settings in an existing project.
	$existingMovementIni = [string]::Join($crlf, @('[RPG_RT]', 'GameTitle=Keep title', 'WinW=320', 'WinH=240', '[EasyRPG]', 'EightDirectionMovement=true', 'PixelMovement=true', 'RunWithShift=true', 'SmoothCamera=true', ''))
	[System.IO.File]::WriteAllText($iniPath, $existingMovementIni, [System.Text.Encoding]::ASCII)
	$movementSettingsBeforeOpen = [System.IO.File]::ReadAllBytes($iniPath)
	& $updateScript -GamePath $gamePath -ForceCanonical
	if ([Convert]::ToBase64String($movementSettingsBeforeOpen) -ne [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($iniPath))) {
		throw 'A normal updater run changed an existing project RPG_RT.ini, including movement settings.'
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
	if ($hadDefaultsBeforeTest) { [System.IO.File]::WriteAllBytes($defaultsPath, $defaultsBeforeTest) }
	elseif (Test-Path -LiteralPath $defaultsPath -PathType Leaf) { Remove-Item -LiteralPath $defaultsPath -Force }
	if (Test-Path -LiteralPath $fixtureRoot -PathType Container) {
		$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
		$resolvedFixture = (Resolve-Path -LiteralPath $fixtureRoot).ProviderPath
		if (-not $resolvedFixture.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
			throw "Refusing to remove fixture outside the temporary directory: $resolvedFixture"
		}
		Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
	}
}
