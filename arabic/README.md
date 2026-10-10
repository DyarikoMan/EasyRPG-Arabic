# EasyRPG-Arabic translation workflow

This toolkit prepares an Arabic translation folder for an RPG Maker 2000/2003 game. It uses LcfTrans to create or update UTF-8 PO files with Windows-1256 project data, then applies the reusable Arabic system terms from `terminology/canonical-terms.json` to matching database contexts. The official Windows LcfTrans 1.1 binary and its MIT notices are included under `tools/` and `licenses/`.

## Translate a game

1. Make and edit your RPG Maker 2000/2003 game normally.
2. Save the project.
3. Run the toolkit from PowerShell:

   ```powershell
   .\arabic\tools\update-arabic.ps1 `
     -GamePath "C:\Games\MyRPG"
   ```

   The included Windows `arabic/tools/lcftrans.exe` is used automatically. To use another LcfTrans build, provide its location:

   ```powershell
   .\arabic\tools\update-arabic.ps1 `
     -GamePath "C:\Games\MyRPG" `
     -LcfTransPath "C:\Tools\LcfTrans\lcftrans.exe"
   ```

   `-LcfTransPath` can be either the executable file or the folder containing `lcftrans.exe`. The bundled binary is from EasyRPG Tools continuous build 369 (LcfTrans 1.1); its SHA-256 and the MIT notices for LcfTrans and liblcf are recorded in the repository.

4. Edit the generated UTF-8 PO files in `C:\Games\MyRPG\Language\ar\`. Put Arabic translations in `msgstr`; keep `msgctxt`, `msgid`, comments, and PO syntax intact.
5. The Maniacs Arabic Editor patch supports tested `TOverEdit` fields such as Actor Name/Nickname and Item Name/Description. For other editor controls that have not been verified, leave placeholder or English source text and translate it in the PO files instead.
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

- `Font.ttf` is the historical Arabic PixelSmall font created by BOU33OU. This unchanged repository copy is distributed under SIL OFL 1.1; see the font folder's `NOTICE.txt`, `OFL.txt`, and `SOURCE.txt`. New-project setup copies it only when the project does not already have a font, preserving custom fonts.
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
