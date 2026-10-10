# Maniacs Arabic Editor Patch

## Supported Arabic text

The patch supports Arabic rendering in the Event Editor command list and tested
Database lists. It also supports Arabic input and display in Maniacs
`TOverEdit` controls, including normal typing and Ctrl+V paste. Tested fields
include Actor Name/Nickname and Item Name/Description; other input controls are
not yet verified.

Set Maniacs **Text Encoding** to **Arabic (Windows)**. EasyRPG-Arabic defaults
projects to CP1256, so `Encoding=1256` is normally unnecessary; users can still
override the project encoding.

## Install

Extract this ZIP, run `install.bat`, and select the Maniacs folder containing
`rpg2003.exe`. The installer backs up conflicting plugin files before copying
the patch files.

To uninstall, run `uninstall.bat` and select the same folder. It restores
backed-up files or removes files the patch added; backups remain under
`.ManiacsArabicEditor\backups`.

The installer also adds `ArabicSupport`, containing the existing CP1256 PO
updater, canonical system terms, the Arabic PixelSmall font, and bundled
LcfTrans 1.1. On a newly created project, Maniacs initializes `Language\ar`,
copies the font if no project font exists, and sets a 426x240 window resolution
after creating the project data. Existing projects are not automatically
updated, and an existing custom font is never overwritten. LcfTrans and liblcf
MIT notices and the font's OFL 1.1 license are included with the support files.
