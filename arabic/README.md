# EasyRPG-Arabic translation workflow

This toolkit prepares an Arabic translation folder for an RPG Maker 2000/2003 game. It uses LcfTrans to create or update the UTF-8 PO files, then applies the reusable Arabic system terms from `terminology/canonical-terms.json` to matching database contexts.

## Translate a game

1. Make and edit your RPG Maker 2000/2003 game normally.
2. Save the project.
3. Run the toolkit from PowerShell:

   ```powershell
   .\arabic\tools\update-arabic.ps1 `
     -GamePath "C:\Games\MyRPG"
   ```

   If LcfTrans is not in `PATH` or `arabic/tools/lcftrans.exe`, provide its location:

   ```powershell
   .\arabic\tools\update-arabic.ps1 `
     -GamePath "C:\Games\MyRPG" `
     -LcfTransPath "C:\Tools\LcfTrans\lcftrans.exe"
   ```

   `-LcfTransPath` can be either the executable file or the folder containing `lcftrans.exe`. The script does not download LcfTrans. Get it from an official EasyRPG Tools distribution and review its license before redistributing it.

4. Edit the generated UTF-8 PO files in `C:\Games\MyRPG\Language\ar\`. Put Arabic translations in `msgstr`; keep `msgctxt`, `msgid`, comments, and PO syntax intact.
5. If the old RPG Maker editor corrupts Arabic text, do not type Arabic directly into its fields. Leave placeholder or English source text there and translate it in the PO files instead.
6. Save the PO files.
7. Run `EasyRPG-Arabic Player.exe` from the game directory. Arabic is auto-selected when the Arabic translation is available.

For example:

```po
msgid "DIALOG_001"
msgstr "السلام عليكم"
```

## Multiline strings

PO uses escaped newlines inside quoted strings. An internal `\n` is valid and creates a line break in the translated text. For example:

```po
msgid "First line\nSecond line"
msgstr "السطر اللول\nالسطر الثاني"
```

Do not add an accidental final `\n` to `msgstr` if the source entry does not end with one. A trailing newline can affect translation lookup and message layout.

## Arabic support

- `Font.ttf` is included at `Language\ar\Font\Font.ttf`; the updater copies the template font and `Meta.ini` only when those files are missing, so it does not overwrite a game's existing versions.
- Arabic is selected automatically. When Arabic is the only available translation, the language selector is hidden.
- The player handles mixed Arabic, Latin text, and numbers, including BiDi ordering and shaping.
- The stock UI uses RTL layout automatically for Arabic.

## Canonical terminology

`terminology/canonical-terms.json` contains reusable RPG Maker system terminology. The updater matches these entries by exact `msgctxt` keys such as `terms.exp_short`, not by English `msgid`, and applies them to `Language\ar\RPG_RT.ldb.po` as defaults. It fills untranslated canonical terms and preserves existing translations and customizations by default. Use `-ForceCanonical` to explicitly reset existing matching terms to the shared canonical values:

```powershell
.\arabic\tools\update-arabic.ps1 `
  -GamePath "C:\Games\MyRPG" `
  -ForceCanonical
```

Currency is game-specific and may be overridden in the game's PO. For example, if the shared `terms.gold` value is `G` but a game uses `ذ`, the updater preserves `ذ` unless `-ForceCanonical` is requested.

Current canonical UI terms include:

| Arabic | Meaning |
| --- | --- |
| مستوى | Level / Lv |
| خبرة | EXP |
| دم | HP |
| مانا | MP |

Keep these Arabic terms in Arabic; do not add English fallbacks such as `Lv`, `EXP`, `HP`, or `MP`. Currency is game-specific. The toolkit does not globally translate `G` or `Gold` as dirham or any other currency.

## Toolkit fixture tests

Run the self-contained PowerShell regression fixtures from the repository root:

```powershell
.\arabic\tools\tests\update-arabic.Tests.ps1
```

They cover multiline PO strings, escaped quotes and backslashes, default preservation of a `terms.gold` game override, and the explicit `-ForceCanonical` behavior.

## Updating a project

The updater runs LcfTrans in update mode (`-u`) from the game's `Language\ar` folder. Use it again after changing the RPG Maker project so the PO files can be refreshed while matching translations are retained. Review any `.stale.po` files LcfTrans produces for text that it could not match.
