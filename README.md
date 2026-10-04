# EasyRPG-Arabic

EasyRPG-Arabic is a fork of EasyRPG Player focused on making it practical 
to create and translate RPG Maker 2000/2003 games in Arabic.

It adds Arabic shaping with HarfBuzz, ICU Unicode BiDi, RTL menus and UI
layouts, and true RTL dialogue typewriter rendering — Arabic dialogue is
revealed from right to left. Mixed Arabic, Latin text, and numbers are handled
automatically.

The project also includes tools for translating existing RPG Maker 2000/2003
games using LcfTrans and gettext PO files.

## Screenshots

![Arabic dialogue and choices](docs/screenshots/arabic-dialogue.png)

## Download

Get EasyRPG-Arabic **v0.1** from the
[GitHub Releases page](https://github.com/DyarikoMan/EasyRPG-Arabic/releases/tag/v0.1-arabic).
This release is experimental and should be considered a pre-release.

- **EasyRPG-Arabic-Windows-x64.zip** — recommended download for most users.
- **Player.exe** — standalone Windows x64 executable.
- **Arabic-Translation-Toolkit.zip** — tools for translating RPG Maker 2000/2003 games.

## Quick start

1. Put `Player.exe` in the game folder beside `RPG_RT.exe`.
2. Put the Arabic translation files in `Language/ar/`. A typical folder looks like:

   ```text
   Language/
   └── ar/
       ├── Meta.ini
       ├── Font/
       │   └── Font.ttf
       ├── RPG_RT.ldb.po
       └── Map0001.po
   ```

3. Run `Player.exe` from the game folder. Arabic is selected automatically when
   an Arabic translation is available.

For optional widescreen rendering, run:

```powershell
Player.exe --game-resolution widescreen
```

## Tutorials

- [Create a new Arabic RPG Maker 2000/2003 game](docs/tutorials/create-arabic-game.md)
- [Translate an existing RPG Maker 2000/2003 game](docs/tutorials/translate-existing-game.md)
- [Arabic dialogue, choices, and RTL typewriter](docs/tutorials/arabic-dialogue-and-choices.md)

## Translating a game

See the [Arabic translation toolkit guide](arabic/README.md) for details. Run
the updater from the repository or toolkit folder:

```powershell
.\arabic\tools\update-arabic.ps1 `
  -GamePath "C:\Games\MyRPG" `
  -LcfTransPath "C:\Tools\lcftrans.exe"
```

The workflow uses LcfTrans to create or update PO files. The toolkit includes
a small starter set of RPG Maker system terms currently translated in Moroccan
Darija. These are only defaults: you can freely edit them to use Modern
Standard Arabic or another Arabic dialect.

## Status and limitations

EasyRPG-Arabic v0.1 has been tested with the stock RPG Maker 2000/2003 UI.
Custom games, custom windows, and unusual layouts may still reveal RTL or text
rendering issues. Please report problems or suggestions on the
[GitHub issue tracker](https://github.com/DyarikoMan/EasyRPG-Arabic/issues).

## Upstream project

EasyRPG-Arabic is a fork focused on Arabic support; it is not the upstream
EasyRPG Player project and does not claim ownership of upstream code. EasyRPG
Player is developed by the EasyRPG Project:

- [EasyRPG Player source](https://github.com/EasyRPG/Player)
- [EasyRPG Project website](https://easyrpg.org/)

Upstream attribution is retained in this repository.

## Building and license

- Build instructions: [docs/BUILDING.md](docs/BUILDING.md)
- License: [COPYING](COPYING)
- Authors and contributors: [docs/AUTHORS.md](docs/AUTHORS.md)

EasyRPG Player and this fork are distributed under the GPLv3, according to the
repository license. See `COPYING` for the license text and retain upstream
copyright and attribution notices.
