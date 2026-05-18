# SpellRogue Mods

Modding tools and documentation for SpellRogue (Guidelight Games).

## Repository Structure

```
./
├── README.md                  ← you are here
├── SPELLROGUE.desktop         ← desktop shortcut for Ubuntu
├── SaveEditor/                ← decrypt, edit, and re-encrypt .sav files
│   ├── README.md
│   └── spellrogue_save_editor.py
├── RestartBattle/             ← BepInEx mod: adds "Restart Battle" to the Esc menu
│   ├── README.md
│   ├── RestartBattle.cs
│   └── RestartBattle.dll
└── Mapping/                   ← reverse engineering reference
    ├── README.md
    └── SpellRogue_ClassMap.txt
```

## Quick Start

| I want to… | Go to |
|---|---|
| Edit save files (gold, health, stats) | [`SaveEditor/`](./SaveEditor/) |
| Add a "Restart Battle" button in-game | [`RestartBattle/`](./RestartBattle/) |
| Write a new mod / understand the codebase | [`Mapping/`](./Mapping/) |

## Environment

- **Game:** SpellRogue (Steam), running under **Wine** on Linux
- **Unity version:** 6000.0.30
- **BepInEx:** 5.4.23.5 (installed at `C:\Games\SpellRogue\`)
- **Save file location:** `C:\users\banyaming\AppData\LocalLow\Guidelight Games\SpellRogue\`

## BepInEx Setup (Wine)

BepInEx is already installed. The `.desktop` launcher file must include:
```
WINEDLLOVERRIDES="winhttp=n,b"
```
Without this, BepInEx silently does nothing under Wine. See [`RestartBattle/README.md`](./RestartBattle/) for the full setup including the `.desktop` file contents.
