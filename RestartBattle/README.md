# Restart Battle Mod

Adds a **"Restart Battle"** button to the Esc pause menu during combat.

## Contents

- `RestartBattle.cs` — mod source code
- `RestartBattle.dll` — compiled mod, ready to install
- This README

## Installation

1. Ensure BepInEx is installed and working (see below)
2. Copy `RestartBattle.dll` into `BepInEx/plugins/`
3. Launch the game — check `BepInEx/LogOutput.log` for `Loading [Restart Battle 1.0.0]`

## Usage

1. Enter any combat encounter
2. Press **Esc**
3. **"Restart Battle"** appears at the top of the pause menu
4. Click once → button turns red and shows **"Sure?"**
5. Click again within 3 seconds → battle restarts from round 1

---

## BepInEx Setup (Wine / Linux)

BepInEx 5.4.23.5 is already installed at `C:\Games\SpellRogue\`.

### Folder Structure

```
C:\Games\SpellRogue\
├── SpellRogue.exe
├── winhttp.dll              ← BepInEx hook (must be next to the .exe)
├── doorstop_config.ini      ← BepInEx config
└── BepInEx\
    ├── core\                ← BepInEx + Harmony DLLs
    ├── plugins\             ← DROP RestartBattle.dll HERE
    ├── config\
    └── LogOutput.log        ← check this when debugging
```

### .desktop File

The Linux `.desktop` launcher **must** include `WINEDLLOVERRIDES`:

```ini
[Desktop Entry]
Name=SPELLROGUE
Exec=env WINEPREFIX="/home/banyaming/.wine" WINEDLLOVERRIDES="winhttp=n,b" wine-stable C:\\Games\\SpellRogue\\SpellRogue.exe
Type=Application
Path=/home/banyaming/.wine/dosdevices/c:/Games/SpellRogue/
Icon=4345_SpellRogue.0
StartupWMClass=spellrogue.exe
```

Without `WINEDLLOVERRIDES="winhttp=n,b"`, BepInEx silently does nothing.

### doorstop_config.ini

```ini
[General]
enabled = true
target_assembly=BepInEx/core/BepInEx.Preloader.dll   # forward slashes required
redirect_output_log = true
```

### Verifying BepInEx Works

After launching, `BepInEx/LogOutput.log` should contain:
```
[Message: BepInEx] Chainloader startup complete
[Info   : BepInEx] 1 plugin to load
[Info   : Restart Battle] RestartBattle mod loaded!
```

---

## Compiling from Source

Requires Mono (`mcs`) and reference DLLs from the game and BepInEx.

```bash
mcs -target:library \
    -r:BepInEx/core/BepInEx.dll \
    -r:BepInEx/core/0Harmony.dll \
    -r:SpellRogue_Data/Managed/UnityEngine.dll \
    -r:SpellRogue_Data/Managed/UnityEngine.CoreModule.dll \
    -r:SpellRogue_Data/Managed/UnityEngine.UI.dll \
    -r:SpellRogue_Data/Managed/Unity.TextMeshPro.dll \
    -r:SpellRogue_Data/Managed/UnityEngine.IMGUIModule.dll \
    -r:SpellRogue_Data/Managed/Assembly-CSharp.dll \
    -r:SpellRogue_Data/Managed/netstandard.dll \
    -out:RestartBattle.dll \
    RestartBattle.cs
```

---

## How It Works

### Patches Applied

| Class | Method | Type | Purpose |
|-------|--------|------|---------|
| `Wiz.WorldMap.MapEncounterHandler` | `Run` | Prefix | Saves instance, `GameplayTile` arg, and `_mapHandler` field for re-use after restart |
| `Wiz.Combat.CombatHandler` | `Begin` | Postfix | Sets `IsInCombat=true`; re-runs `MapEncounterHandler.Run` after a restart |
| `Wiz.Combat.CombatHandler` | `Continue` | Postfix | Sets `IsInCombat=true` for resumed saves |
| `Wiz.WorldMap.MapEncounterHandler` | `OnCombatEnded` | Postfix | Clears `IsInCombat` and active coroutine reference |
| `Wiz.Combat.CombatTimeline` | `DoPlayVictory` | Postfix | Clears `IsInCombat` |
| `Wiz.Combat.CombatTimeline` | `DoPlayDefeat` | Postfix | Clears `IsInCombat` |
| `Wiz.Combat.CombatTimeline` | `DoPlayFinalBossVictory` | Postfix | Clears `IsInCombat` |
| `Wiz.Combat.CombatTimeline` | `DoPlayFinalBossDefeat` | Postfix | Clears `IsInCombat` |
| `Wiz.UI.Pause.PausePopup` | `OnEnable` | Postfix | Injects the Restart Battle button |

### The `_mapHandler` Problem

`MapEncounterHandler.Run()` null-checks its `_mapHandler` field before proceeding and returns `null` if it's missing. `CombatDebug.RestartCombat` calls `PrepareEnding` which clears this field as part of combat cleanup.

**Fix:** save `_mapHandler` via reflection when `Run` is first called, restore it before calling `Run` again after a restart.

### Post-Combat Rewards

`RestartCombat` (the game's internal debug method) creates a new `CombatHandler` but doesn't re-subscribe `MapEncounterHandler.Run` to it. Without intervention, winning a restarted battle causes a black screen instead of rewards.

**Fix:** after restart, re-call `MapEncounterHandler.Run` with the saved `GameplayTile` argument via a persistent `CoroutineHelper` MonoBehaviour. This re-subscribes the encounter handler to the new combat and restores the normal post-battle rewards flow.

### UI Injection

The button is cloned from the "Abandon Run" button (same parent `Vert_RunButtons`, same style). A `RestartBattleTag` component marks it to prevent duplicate injection across multiple `OnEnable` calls. A `RestartBattleConfirm` component handles the two-step confirmation UX.

---

## Known Limitations

- The mod uses `Wiz.DebugMethods.CombatDebug.RestartCombat` — an internal debug method never intended for live gameplay. Edge cases may exist for unusual game states.
- If the button appears outside of combat, check `BepInEx/LogOutput.log` for unexpected `IsInCombat` state.
