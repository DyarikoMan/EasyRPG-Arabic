# Bootstrapper Architecture

## Goal and current boundary

The target user flow detects a vanilla Steam RPG Maker 2003 installation,
recommends the supported components, and lets the user install them with one
click. The result lives in a separate managed folder such as
`RPG Maker 2003 - Arabic`; the vanilla Steam folder is only a read-only source.

The setup requires an extracted official Maniacs package supplied by the user;
it does not bundle or redistribute Maniacs files. The installer displays that
package's `LICENSE.txt` and requires explicit acceptance before applying its
modules. It does not launch `utility.exe` or `patch.exe`.

## Repository layout

```text
tools/maniacs-arabic-setup/
  README.md
  ARCHITECTURE.md
  packaging/publish.ps1
  src/
    Setup.App/                 # Windows GUI; packaged as one no-console EXE
    Setup.Core/                # detection, planning, state, transaction engine
    Setup.Core/Modules/        # module interface, planner, Arabic Editor module
    Setup.Core/Maniacs/        # package inspector and manifest models
  data/maniacs/
    layouts/                   # package structure and architecture definitions
    recipes/                   # release/build detection and file fingerprints
    compatibility/             # Arabic Editor validation by Maniacs recipe
  tests/Setup.Core.Tests/      # planner, validation, state, and transaction tests
  dist/                        # generated user-facing setup executable
  legacy-prototype/            # archived interactive PowerShell prototype
```

Module operations are compiled into Setup.Core, not loaded as arbitrary scripts
from the install folder. Maniacs package knowledge is data-driven and kept out
of the GUI: layout manifests describe structure and architecture, release
recipes identify exact builds and fingerprints, and a separate compatibility
manifest records which recipes have been validated with the Arabic Editor
patch. These JSON files are embedded in the single-file executable but can be
updated without changing the GUI or inspector implementation. A future
authorized manifest-update channel must authenticate its data before loading
it. Large payloads can be downloaded from an authorized release source after
hash/signature verification rather than embedded in the executable.

## Module contract

Each module has a stable ID, display name, version, dependencies, prerequisites,
apply operation, verifier, owned-file manifest, and rollback/uninstall policy.
The planner checks prerequisites and dependency cycles, then topologically
orders selected modules. Applying a module returns the files and hashes it
actually installed; verification must pass before the next module runs.

The base installation is an immutable input node, not an installable module:

```text
vanilla-rm2003
  ├── maniacs
  │     └── maniacs-english
  │             └── arabic-editor
  └── easyrpg-arabic
```

The recommended profile selects all four modules. Future optional modules can
join the graph without embedding their ordering in the UI. If two modules need
to own or replace the same path, the manifest must declare shared ownership or
an explicit predecessor/backup relationship.

### Current package mapping

| Module | Input and responsibility | Status |
| --- | --- | --- |
| `maniacs` | Validate the exact user-supplied package and vanilla source, copy only recipe-listed vanilla editor files (and observed optional helpers), then apply the Common files and license to staging. | Functional for exact recipe `maniacs-241029`; verifies source/package hashes and editor architectures. |
| `maniacs-english` | Copy the recipe-mapped English payload, preserve `rpg2003.OLD`, apply the embedded WDiff delta to staged `rpg2003.exe`, and verify the output. | Functional for exact recipe `maniacs-241029`; it does not launch `patch.exe`. |
| `arabic-editor` | Install the three existing editor DLLs and set the managed install's `Plugin/cmdcs.xml` Text Encoding to Arabic (Windows) / CP1256. Preserve previous plugin settings and restore them on uninstall when unchanged. | Module apply/verify/rollback is implemented. Planner blocks it until `maniacs-english` is available. |
| `easyrpg-arabic` | Add the EasyRPG-Arabic Player runtime as a separately managed component; verify its executable and version. | Not implemented in this setup. |

The existing Arabic Editor ZIP stays at
`tools/maniacs-arabic-editor/dist/Maniacs-Arabic-Editor-Windows-x86.zip` and is
embedded into the setup executable and consumed by the `arabic-editor` module.
Do not copy its source or modify that implementation. The module writes only
the three required DLLs into a valid staged Maniacs `Plugin` folder, refuses to
overwrite conflicting files, verifies their SHA-256 hashes against the ZIP,
and records them as module-owned files.

### Maniacs package recipes

The generic inspector discovers a selected package root (or one immediate
extracted child), checks a registered layout's required paths and PE machine
types, detects a build from recipe markers, and verifies exact known-release
fingerprints. The initial layout is in
`data/maniacs/layouts/common-english-v1.json`; the 2024-10-29 release recipe and
its fingerprints are in `data/maniacs/recipes/maniacs-241029.json`; Arabic
Editor validation is separately recorded in
`data/maniacs/compatibility/arabic-editor.json`.

For the inspected package, utility/editor/plugin binaries are x86 and the
included `RPG_RT.exe` runtime is x64. The 241029 recipe also records the
required vanilla source hashes, staged payload mappings, WDiff version, target,
input/output sizes and hashes, CRCs, output timestamp, and expected Plugin
contents. Its WDiff reader validates the complete stream and applies copy-from-
input and literal-data operations in memory; only the staged editor target is
replaced. A matching layout with an unknown build,
a known build with changed fingerprints, an architecture mismatch, or a changed
layout stops for validation. Only an exact recipe match can report the Arabic
patch as validated. The recipe and compatibility data remain separate from the
generic parser and module code; a future release using the same WDiff format can
normally be added with new fingerprints and recipe values.

Adding a release should normally require a layout update only if its structure
changed, a new recipe with build markers and package fingerprints, and a
compatibility entry after Arabic Editor validation. Generic parsing, planning,
and UI code should not gain release-specific version branches.

## Installation transaction

1. Detect Steam libraries and locate the common RPG Maker 2003 folder. Show the
   detected source and recommended modules before install. This detection and
   GUI are implemented.
2. Validate Steam app ID `362870`, expected Steam data files, and the 32-bit
   editor executable. Capture source fingerprints for audit; never write to the
   source.
3. Resolve selected modules and prerequisites in topological dependency order.
   The planner blocks unavailable modules, including the not-yet-implemented
   EasyRPG-Arabic Player runtime.
4. Build into a unique sibling staging folder. Apply modules in dependency
   order, recording each operation and checking its verifier before proceeding.
5. Write managed installation state into the stage, then perform final
   cross-module checks. The folder is not presented as installed before these
   checks pass.
6. Finalize with same-volume directory moves: keep the current managed install
   as a rollback generation, move the verified stage into the final path, and
   restore the previous generation if the swap fails.

The transaction engine creates an empty unique sibling staging folder. Modules
copy only their explicitly owned source and package files into that stage, then
the coordinator writes versioned JSON state there. It retains an existing destination as
a rollback generation only after explicit replacement approval, and restores
that prior destination if finalization fails. These primitives are implemented
and covered by tests. The GUI currently blocks before staging because the
recommended set contains unfinished modules.

When a module fails, stop immediately. Since work is isolated in staging, the
normal rollback is to discard staging; module rollback handlers undo external
effects such as shortcuts or registry entries. The vanilla source is never an
undo target. Existing user files in a managed install must be preserved during
updates or kept outside the replaceable application tree.

Uninstall uses ownership records: remove a file only if it is still present
with the hash recorded for its owning module. Preserve changed files and
unmanaged user data, and report them. Shared files are removed only when no
installed module still claims them. A full-install removal can offer to move
the remaining managed folder to the Recycle Bin after listing what it contains.

## Installation state

Store a versioned JSON record under the managed installation, for example
`.EasyRPG-Arabic/setup-state.json`:

```json
{
  "schemaVersion": 1,
  "installId": "<stable-guid>",
  "generation": 1,
  "source": {
    "kind": "steam-rpg-maker-2003",
    "appId": "362870",
    "path": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\RPG Maker 2003",
    "fingerprint": { "rpg2003.exe": "sha256:<digest>" }
  },
  "installPath": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\RPG Maker 2003 - Arabic",
  "modules": [
    {
      "id": "arabic-editor",
      "version": "<package-version>",
      "manifestHash": "sha256:<digest>",
      "files": [
        { "path": "Plugin/EasyCmdTos.dll", "sha256": "<digest>" },
        { "path": "Plugin/EasyRpgCommands.dll", "sha256": "<digest>" },
        { "path": "Plugin/0Harmony.dll", "sha256": "<digest>" }
      ]
    }
  ],
  "createdUtc": "<timestamp>",
  "updatedUtc": "<timestamp>"
}
```

The actual record includes every installed module and its owned files, not only
the example Arabic Editor entry. Validate relative file paths against the
managed root when reading state. The record supports repair, update planning,
version display, and safe module-level removal.

## One-click and packaging constraints

The current UI is a WinForms Windows GUI published as the self-contained
`dist/EasyRPG-Arabic-Setup.exe` for Windows x64. It has no console subsystem,
detects Steam installs, offers Browse, shows the recommended modules, and has
an expandable **Advanced options** destination field. It does not require
Visual Studio, CMake, Git, BAT use, or manual DLL copying from users.

The Maniacs, English, and Arabic Editor modules are ready when the exact
user-supplied 241029 package is selected and accepted. EasyRPG-Arabic Player
integration remains unavailable. The setup distribution must not contain
proprietary Steam RPG Maker or Maniacs files without clear redistribution
permission.
