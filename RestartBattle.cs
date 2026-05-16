using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;
using Wiz.Combat;
using Wiz.UI.Pause;
using Wiz.WorldMap;

namespace RestartBattleMod
{
    [BepInPlugin("com.spellrogue.restartbattle", "Restart Battle", "1.0.0")]
    public class RestartBattlePlugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static bool IsInCombat = false;

        void Awake()
        {
            Log = Logger;
            Log.LogInfo("RestartBattle mod loaded!");
            try
            {
                new Harmony("com.spellrogue.restartbattle").PatchAll();
                Log.LogInfo("Harmony patches applied.");
            }
            catch (System.Exception e)
            {
                Log.LogError($"Harmony patching failed: {e}");
            }
        }

        public static void DoRestartBattle()
        {
            Log.LogInfo("Restart Battle clicked — calling RestartCombat...");
            try
            {
                var debugType     = AccessTools.TypeByName("Wiz.DebugMethods.CombatDebug");
                var debugInstance = System.Activator.CreateInstance(debugType);
                var restartMethod = AccessTools.Method(debugType, "RestartCombat");
                restartMethod.Invoke(debugInstance, null);
                Log.LogInfo("RestartCombat called successfully.");
            }
            catch (System.Exception e)
            {
                Log.LogError($"RestartCombat failed: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(CombatHandler), "Begin")]
    public static class CombatBeginPatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            RestartBattlePlugin.IsInCombat = true;
            RestartBattlePlugin.Log.LogInfo("Combat started (Begin).");
        }
    }

    [HarmonyPatch(typeof(CombatHandler), "Continue")]
    public static class CombatContinuePatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            RestartBattlePlugin.IsInCombat = true;
            RestartBattlePlugin.Log.LogInfo("Combat started (Continue).");
        }
    }

    [HarmonyPatch(typeof(MapEncounterHandler), "OnCombatEnded")]
    public static class CombatEndPatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            RestartBattlePlugin.IsInCombat = false;
            RestartBattlePlugin.Log.LogInfo("Combat ended.");
        }
    }

    // Tag component so we can reliably detect our injected button
    public class RestartBattleTag : MonoBehaviour { }

    [HarmonyPatch(typeof(PausePopup), "OnEnable")]
    public static class PausePopupPatch
    {
        [HarmonyPostfix]
        static void Postfix(PausePopup __instance)
        {
            if (!RestartBattlePlugin.IsInCombat) return;

            // Already injected into this popup instance — skip
            if (__instance.GetComponentInChildren<RestartBattleTag>(true) != null) return;

            // Find "Abandon Run" button — it's always in Vert_RunButtons
            var allButtons = __instance.GetComponentsInChildren<Button>(true);
            var abandonBtn = allButtons.FirstOrDefault(b =>
            {
                var lbl = b.GetComponentInChildren<TextMeshProUGUI>();
                return lbl != null && lbl.text == "Abandon Run";
            });

            if (abandonBtn == null)
            {
                RestartBattlePlugin.Log.LogWarning("Abandon Run button not found.");
                return;
            }

            var menuParent = abandonBtn.transform.parent; // Vert_RunButtons

            // Clone Abandon Run as our template (same style)
            var newBtn = GameObject.Instantiate(abandonBtn, menuParent);
            newBtn.name = "RestartBattleButton";
            newBtn.gameObject.AddComponent<RestartBattleTag>();

            // Set label
            var label = newBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = "Restart Battle";

            // Place at top of menu
            newBtn.transform.SetSiblingIndex(0);

            // Clear ALL listeners (including Abandon Run's) and add only ours
            newBtn.onClick.RemoveAllListeners();
            newBtn.onClick.AddListener(RestartBattlePlugin.DoRestartBattle);

            RestartBattlePlugin.Log.LogInfo("Restart Battle button injected at top of pause menu.");
        }
    }
}
