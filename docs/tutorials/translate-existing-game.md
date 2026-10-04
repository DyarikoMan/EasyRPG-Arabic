# Translate an existing RPG Maker game into Arabic

1. Make a backup of the game folder before updating its translation files.
2. Run the updater against the folder containing `RPG_RT.exe`. Provide
   `-LcfTransPath` if LcfTrans is not on `PATH` or at
   `arabic/tools/lcftrans.exe`:

   ```powershell
   .\arabic\tools\update-arabic.ps1 `
     -GamePath "C:\Games\MyRPG" `
     -LcfTransPath "C:\Tools\lcftrans.exe"
   ```

3. Open the generated or updated UTF-8 PO files under `Language/ar/`. Translate
   `msgstr`; do not change `msgid` or `msgctxt`. Keep comments, placeholders,
   control codes, and PO escaping intact.
4. Save the PO files and run EasyRPG-Arabic's `Player.exe` from the game folder
   to test the translation.

The updater runs LcfTrans in update mode. Existing translations are preserved.
Canonical system terms are inserted only where their `msgstr` is untranslated,
and customized system terms are preserved by default. `-ForceCanonical` resets
matching system terms to the shared canonical values when you explicitly want
that. Currency is game-specific; the toolkit does not assume that `G` or `Gold`
means dirham.

Run the updater again after changing the original RPG Maker project so new or
changed source strings can be reflected in the PO files. If LcfTrans creates
`.stale.po` files, review them for translations that no longer matched the
updated source.

Custom scripts, custom windows, and unusual layouts may need additional testing
in EasyRPG-Arabic. Not every existing game will work perfectly without further
adjustments.
