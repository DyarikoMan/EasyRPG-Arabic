# Create a new Arabic RPG Maker game

This workflow uses the RPG Maker editor for the project and UTF-8 PO files for
Arabic text.

1. Create or edit your RPG Maker 2000/2003 project normally, then save it.
2. If the older editor corrupts Arabic, use English or ASCII placeholders in
   its text fields. You can translate those strings in PO files instead.
3. Generate or update the Arabic translation files. From the EasyRPG-Arabic
   repository, run:

   ```powershell
   .\arabic\tools\update-arabic.ps1 `
     -GamePath "C:\Games\MyRPG" `
     -LcfTransPath "C:\Tools\lcftrans.exe"
   ```

   `-LcfTransPath` is optional when LcfTrans is available on `PATH` or at
   `arabic/tools/lcftrans.exe`.
4. Edit the generated UTF-8 PO files under `Language/ar/`. Put Arabic text in
   `msgstr`; leave `msgid`, `msgctxt`, comments, and PO syntax intact. Empty
   standard system terms can be filled from the canonical terminology. Your
   game-specific translations remain editable.
5. Run EasyRPG-Arabic's `Player.exe` from the game folder beside `RPG_RT.exe`.
   To use widescreen rendering, run:

   ```powershell
   Player.exe --game-resolution widescreen
   ```

A typical translation folder looks like this:

```text
Language/
└── ar/
    ├── Meta.ini
    ├── Font/
    │   └── Font.ttf
    ├── RPG_RT.ldb.po
    └── Map0001.po
```

For example, a PO translation can contain:

```po
msgid "DIALOG_001"
msgstr "السلام عليكم"
```

The Arabic font is provided in `Language/ar/Font/Font.ttf`; Arabic is selected
automatically when its translation is available.
