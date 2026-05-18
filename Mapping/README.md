# Mapping & Reverse Engineering Reference

Reference documentation for SpellRogue's `Assembly-CSharp.dll`.

## Contents

- `SpellRogue_ClassMap.txt` — all 3,097 classes with every method and field index
- This README — key classes, modding patterns, and decompilation guide

---

## Decompilation

SpellRogue is a **Unity Mono** build. The compiled game logic lives entirely in:
```
SpellRogue_Data/Managed/Assembly-CSharp.dll
```

This is standard .NET IL — fully readable by decompilers.

### Recommended Tool: ILSpy

```bash
# Install (requires dotnet)
dotnet tool install ilspycmd -g

# Decompile a specific class
ilspycmd Assembly-CSharp.dll -t Wiz.UserData.CryptSaver

# Decompile everything to a folder
ilspycmd Assembly-CSharp.dll -p -o ./decompiled/
```

GUI version: https://github.com/icsharpcode/ILSpy/releases  
Alternative: **dnSpy** (also supports editing and recompiling IL directly)

### Python Analysis (used to build this reference)

```python
import dnfile  # pip install dnfile  — reads .NET metadata tables
import pefile  # pip install pefile  — reads PE binary / RVAs

dnpe   = dnfile.dnPE('Assembly-CSharp.dll')
tables = dnpe.net.metadata.streams[b'#~'].tables

typedef_table = tables[2]   # all classes
field_table   = tables[4]   # all fields
method_table  = tables[6]   # all methods
```

### Using SpellRogue_ClassMap.txt

Search the file for a class or method name to get its index, then look it up in ILSpy for the full body.

```bash
grep -i "CombatHandler" SpellRogue_ClassMap.txt
grep -i "RestartCombat" SpellRogue_ClassMap.txt
```

---

## Key Classes & Methods

### Save System

| Class | Notable Methods |
|-------|----------------|
| `Wiz.UserData.CryptSaver` | `Save`, `Load`, `EncryptStringToBytes`, `DecryptBytesToString`, `ComputeHMAC` |
| `Wiz.UserData.Profile` | `LoadActiveSession`, `SaveActiveSession` |
| `Wiz.GameSessionManager` | `ContinueGame`, `StartGameWith` |

### Combat

| Class | Notable Methods |
|-------|----------------|
| `Wiz.Combat.CombatHandler` | `Begin`, `Continue`, `PrepareEnding`, `LoadSceneAndBegin`, `LoadSceneAndContinue` |
| `Wiz.Combat.CombatTimeline` | `DoPlayIntro`, `DoPlayVictory`, `DoPlayDefeat`, `DoPlayFinalBossVictory`, `DoPlayFinalBossDefeat` |

### World Map

| Class | Notable Methods |
|-------|----------------|
| `Wiz.WorldMap.MapEncounterHandler` | `Run(GameplayTile)`, `OnCombatEnded`, `RunTileResults`, `RunActEnd` |
| `Wiz.WorldMap.WorldMapHandler` | `Continue`, `EnterTileEncounter` |

**Important:** `MapEncounterHandler.Run` is the coroutine that monitors combat and triggers rewards. It null-checks `_mapHandler` at startup — this field can be cleared by `PrepareEnding`.

### UI

| Class | Notable Methods |
|-------|----------------|
| `Wiz.UI.Pause.PausePopup` | `OnEnable`, `GetNavigationSelectables` |
| `Wiz.UI.Menus` | `Open`, `Create`, `Show` |
| `Wiz.UI.UiPopup` | Base popup class. `RefreshActiveState` |

### Debug Namespace (`Wiz.DebugMethods`)

The game ships with a full debug suite, never exposed to players.

| Class | Notable Methods |
|-------|----------------|
| `CombatDebug` | `RestartCombat`, `Kill`, `Heal`, `DealDamage`, `RefreshAll`, `EnchantDice`, `SpawnEnemy` |
| `EncounterDebug` | Encounter manipulation tools |
| `PopupDebug` | Debug UI popups |
| `ExportDebug` | Data export utilities |

All debug classes extend `DebugCommandTable` — see the note below.

---

## Important: Non-MonoBehaviour Classes

Several key game classes extend `DebugCommandTable`, **not** `MonoBehaviour`. This means `FindObjectOfType<T>()` and `FindObjectsOfType<T>()` will **not** find them at runtime.

**Affected classes:**
- `Wiz.Combat.CombatHandler`
- `Wiz.WorldMap.MapEncounterHandler`
- `Wiz.WorldMap.WorldMapHandler`
- All `Wiz.DebugMethods.*` classes

### How to Instantiate and Call via Reflection

```csharp
// Instantiate (for classes with a no-arg constructor like debug classes)
var type     = AccessTools.TypeByName("Wiz.DebugMethods.CombatDebug");
var instance = System.Activator.CreateInstance(type);
var method   = AccessTools.Method(type, "RestartCombat");
method.Invoke(instance, null);

// Access a private field
var field = AccessTools.Field(typeof(Wiz.WorldMap.MapEncounterHandler), "_mapHandler");
var value = field.GetValue(handlerInstance);
field.SetValue(handlerInstance, value);
```

### How to Intercept Instances via Harmony

Since you can't find these at runtime, save a reference when they're created:

```csharp
[HarmonyPatch(typeof(Wiz.WorldMap.MapEncounterHandler), "Run")]
public static class SaveHandlerPatch
{
    [HarmonyPrefix]
    static void Prefix(Wiz.WorldMap.MapEncounterHandler __instance, object[] __args)
    {
        MySavedInstance = __instance;
        MySavedArg      = __args?.Length > 0 ? __args[0] : null;
    }
}
```

---

## Writing New Mods

### Project Template

```csharp
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace MyMod
{
    [BepInPlugin("com.yourname.modname", "Mod Display Name", "1.0.0")]
    public class MyPlugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;

        void Awake()
        {
            Log = Logger;
            Log.LogInfo("MyMod loaded!");
            new Harmony("com.yourname.modname").PatchAll();
        }
    }

    [HarmonyPatch(typeof(SomeGameClass), "SomeMethod")]
    public static class MyPatch
    {
        [HarmonyPrefix]
        static void Prefix(SomeGameClass __instance)
        {
            // fires before SomeMethod
        }

        [HarmonyPostfix]
        static void Postfix(SomeGameClass __instance)
        {
            // fires after SomeMethod
        }
    }
}
```

### Compile Command

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
    -out:MyMod.dll \
    MyMod.cs
```

### Injecting UI Buttons into the Pause Menu

The pause menu is `Wiz.UI.Pause.PausePopup`. Patch `OnEnable` and insert into `Vert_RunButtons`:

```csharp
[HarmonyPatch(typeof(PausePopup), "OnEnable")]
public static class PauseMenuPatch
{
    [HarmonyPostfix]
    static void Postfix(PausePopup __instance)
    {
        var buttons   = __instance.GetComponentsInChildren<Button>(true);
        var abandonBtn = buttons.FirstOrDefault(b =>
            b.GetComponentInChildren<TextMeshProUGUI>()?.text == "Abandon Run");

        if (abandonBtn == null) return;

        var newBtn = GameObject.Instantiate(abandonBtn, abandonBtn.transform.parent);
        newBtn.GetComponentInChildren<TextMeshProUGUI>().text = "My Button";
        newBtn.transform.SetSiblingIndex(0);
        newBtn.GetComponent<Button>().onClick.RemoveAllListeners();
        newBtn.GetComponent<Button>().onClick.AddListener(() => { /* do stuff */ });
    }
}
```

**Note:** The button text may get reset by the `TweenButton` component. Fix with a one-frame coroutine:
```csharp
IEnumerator SetTextNextFrame(TextMeshProUGUI label, string text)
{
    yield return null;
    yield return null;
    label.text = text;
}
```

### Running Coroutines Outside MonoBehaviour

For calling coroutines from non-MonoBehaviour code, use a persistent helper:

```csharp
public class CoroutineHelper : MonoBehaviour
{
    private static CoroutineHelper _instance;
    public static CoroutineHelper Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("CoroutineHelper");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<CoroutineHelper>();
            }
            return _instance;
        }
    }
}

// Usage from anywhere:
CoroutineHelper.Instance.StartCoroutine(MyEnumerator());
```

### Debugging

Check `BepInEx/LogOutput.log` after every launch. Add logging with:
```csharp
Logger.LogInfo("message");    // inside BaseUnityPlugin
Logger.LogWarning("message");
Logger.LogError("message");
```
