# Save Editor

Decrypt, edit, and re-encrypt SpellRogue `.sav` files.

## Contents

- `spellrogue_save_editor.py` — command-line tool to decrypt/re-encrypt saves
- This README — save file format reference

## Requirements

```bash
pip install pycryptodome
```

## Usage

```bash
# 1. Decrypt a .sav to JSON
python3 spellrogue_save_editor.py decrypt Session.sav session.json

# 2. Edit session.json in any text editor

# 3. Re-encrypt back (game must be fully closed first)
python3 spellrogue_save_editor.py encrypt session.json Session.sav
```

A `.meta` file is created alongside the JSON on decrypt — keep it there.
It stores the original file header and integrity prefix needed for re-encryption.

---

## Save File Locations

```
C:\users\<user>\AppData\LocalLow\Guidelight Games\SpellRogue\
├── UserData.sav               ← global settings
└── Profiles\
    └── 1\
        ├── Session.sav        ← full run + active combat state
        ├── Session.sav.bak    ← automatic backup
        ├── CombatRound.sav    ← turn-level combat snapshot
        ├── Profile.sav        ← character profile / meta-progression
        └── Runs\
            └── YYYY-MM-DD HH-MM-SS.sav
```

### File Hierarchy

```
Session.sav
  └── CombatRound.sav   ← state at the start of the current turn
```

- Deleting `CombatRound.sav` → resets to start of current **turn**
- Deleting `Session.sav` → loses the whole **run**

---

## Encryption Format

All `.sav` files use **AES-256-CBC encryption**.

```
Bytes 0–3   : Magic header (always 0x20000000)
Bytes 4–7   : Secondary header
Bytes 8–23  : AES-CBC IV (16 bytes, unique per file)
Bytes 24+   : AES-256-CBC encrypted payload
```

Encrypted payload:
```
Bytes 0–31  : 32-byte integrity prefix — preserve as-is when re-encrypting
Bytes 32+   : JSON content WITHOUT the leading {"version": prefix
```

The game reconstructs the full JSON by prepending `{"version": ` on load.

### Keys

Extracted from `Assembly-CSharp.dll → Wiz.UserData.CryptSaver`:

```
AES-256 Key : GS3cwXk+9PqYBwC/N0iwfpIthlL0tB0TJ5/aODtcnLo=  (base64)
HMAC key    : abcdef1234567890  (UTF-8)
```

---

## Session.sav JSON Reference

### Top-Level Keys

| Key | Description |
|-----|-------------|
| `version` | Game version string |
| `buildId` | Build number |
| `game` | Full run state: wizard, cards, artifacts, gold, map progress |
| `mapState` | Current act, position on map, tile conditions |
| `combatSnapshot` | Active combat (only present mid-battle) |
| `statistics` | Run stats: damage dealt, dice rolled, resets used, etc. |
| `achievements` | In-run objective tracking |
| `score` | Score counters |

### game Object

```json
"game": {
  "wizard": { "health": 51, "maxHealth": 70, "diceAmount": 3, "spellBookSlots": 6 },
  "cards": [ ... ],
  "potions": [ ... ],
  "artifacts": [ ... ],
  "relics": [ ... ],
  "gold": 64,
  "crystals": 3
}
```

### combatSnapshot Object

```json
"combatSnapshot": {
  "combatData": {
    "roundNumber": 8,
    "hasPerformedTurnAction": true,
    "statistics": {
      "initialHp": 51,
      "damageDealt": 250,
      "damageReceived": 104,
      "resetsUsed": 2
    }
  },
  "entitiesSnapshot": {
    "wizard":    { "health": 1, "startingHealth": 51, "statusEffects": [...] },
    "enemies":   [ { "health": 90, "maxHealth": 170, "statusEffects": [...] } ],
    "spells":    [ ... ],
    "artifacts": [ ... ],
    "potions":   [ ... ]
  }
}
```

### Useful Edits

| What to change | Path |
|---|---|
| Wizard HP | `game.wizard.health` and `combatSnapshot.entitiesSnapshot.wizard.health` |
| Max HP | `game.wizard.maxHealth` |
| Gold | `game.gold` |
| Crystals | `game.crystals` |
| Enemy HP (in combat) | `combatSnapshot.entitiesSnapshot.enemies[N].health` |
| Turn resets used | `combatSnapshot.combatData.statistics.resetsUsed` |
| Combat round | `combatSnapshot.combatData.roundNumber` |
