# Maniacs Arabic Editor Patch

1. Install RPG Maker 2003 with the Maniacs Patch.
2. Extract this ZIP, then run `install.bat`. Select the Maniacs folder containing `rpg2003.exe`.
3. Start Maniacs and set **Text Encoding** to **Arabic (Windows)**.
4. Type Arabic normally, then use EasyRPG-Arabic to run the game.

The Event Editor command list and some Database lists render Arabic. Some older
text-entry fields may still show mojibake; the saved project data remains correct.

To remove the patch, run `uninstall.bat` and select the same Maniacs folder.
Existing files are backed up before replacement. Uninstall restores those files
or removes patch files it installed, and retains its backups under
`.ManiacsArabicEditor\backups` in the Maniacs folder.
