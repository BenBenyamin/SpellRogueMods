using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Reflection;
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
        public static bool IsInCombat   = false;
        public static bool WasRestarted = false;

        // Saved from MapEncounterHandler.Run so we can re-run after restart
        public static object SavedMapEncounterHandler = null;
        public static object SavedRunArg              = null;
        public static object SavedMapHandler           = null;  // saved _mapHandler field
        public static Coroutine ActiveRunCoroutine     = null;  // tracked so we can stop it on re-restart

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

        public static void CallRestartCombat()
        {
            Log.LogInfo("Calling RestartCombat...");
            try
            {
                var debugType     = AccessTools.TypeByName("Wiz.DebugMethods.CombatDebug");
                var debugInstance = System.Activator.CreateInstance(debugType);
                var restartMethod = AccessTools.Method(debugType, "RestartCombat");
                restartMethod.Invoke(debugInstance, null);
                WasRestarted = true;
                Log.LogInfo("RestartCombat called. WasRestarted=true.");
            }
            catch (System.Exception e)
            {
                Log.LogError($"RestartCombat failed: {e}");
            }
        }
    }

    // Persistent MonoBehaviour used to run coroutines from non-MonoBehaviour context
    public class CoroutineHelper : MonoBehaviour
    {
        private static CoroutineHelper _instance;
        public static CoroutineHelper Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("RestartBattleHelper");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<CoroutineHelper>();
                }
                return _instance;
            }
        }
    }

    // Tag to prevent duplicate button injection
    public class RestartBattleTag : MonoBehaviour { }

    // Confirmation UX on the button
    public class RestartBattleConfirm : MonoBehaviour
    {
        private bool  _waitingConfirm = false;
        private float _confirmTimer   = 0f;
        private const float ConfirmWindow = 3f;
        private TextMeshProUGUI _label;
        private Color _normalColor;
        private readonly Color _confirmColor = new Color(0.9f, 0.2f, 0.2f);

        void Awake()
        {
            _label = GetComponentInChildren<TextMeshProUGUI>();
            if (_label != null) _normalColor = _label.color;
        }

        void Update()
        {
            if (_waitingConfirm)
            {
                _confirmTimer -= Time.deltaTime;
                if (_confirmTimer <= 0f) ResetState();
            }
        }

        public void OnClicked()
        {
            if (!_waitingConfirm)
            {
                _waitingConfirm = true;
                _confirmTimer   = ConfirmWindow;
                if (_label != null) { _label.text = "Sure?"; _label.color = _confirmColor; }
            }
            else
            {
                ResetState();
                RestartBattlePlugin.CallRestartCombat();
            }
        }

        public void ResetState()
        {
            _waitingConfirm = false;
            _confirmTimer   = 0f;
            if (_label != null) { _label.text = "Restart Battle"; _label.color = _normalColor; }
        }

        public void ForceLabel() => StartCoroutine(ForceLabelCoroutine());

        IEnumerator ForceLabelCoroutine()
        {
            yield return null;
            yield return null;
            if (_label != null && !_waitingConfirm)
                _label.text = "Restart Battle";
        }
    }

    // ── Save MapEncounterHandler.Run instance + arg for re-use after restart ──
    [HarmonyPatch(typeof(MapEncounterHandler), "Run")]
    public static class MapEncounterRunPatch
    {
        [HarmonyPrefix]
        static void Prefix(MapEncounterHandler __instance, object[] __args)
        {
            RestartBattlePlugin.SavedMapEncounterHandler = __instance;
            if (__args != null && __args.Length > 0)
                RestartBattlePlugin.SavedRunArg = __args[0];
            // Save _mapHandler — Run() null-checks this field and returns null if missing
            var mhField = AccessTools.Field(typeof(MapEncounterHandler), "_mapHandler");
            if (mhField != null)
                RestartBattlePlugin.SavedMapHandler = mhField.GetValue(__instance);
            RestartBattlePlugin.Log.LogInfo(
                $"MapEncounterHandler.Run saved (arg={(__args?.Length > 0 ? __args[0]?.GetType().Name : "none")}, mapHandler={RestartBattlePlugin.SavedMapHandler != null}).");
        }
    }

    // ── Combat start ─────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(CombatHandler), "Begin")]
    public static class CombatBeginPatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            RestartBattlePlugin.IsInCombat = true;
            RestartBattlePlugin.Log.LogInfo("Combat started (Begin).");

            // If this Begin fired because of a RestartCombat, re-run MapEncounterHandler.Run
            if (RestartBattlePlugin.WasRestarted &&
                RestartBattlePlugin.SavedMapEncounterHandler != null)
            {
                RestartBattlePlugin.WasRestarted = false;
                CoroutineHelper.Instance.StartCoroutine(ReRunEncounterHandler());
            }
        }

        static IEnumerator ReRunEncounterHandler()
        {
            // Wait two frames to let the new combat finish initialising
            yield return null;
            yield return null;

            try
            {
                var handler     = RestartBattlePlugin.SavedMapEncounterHandler;
                var handlerType = handler.GetType();

                // Restore _mapHandler — PrepareEnding clears it, Run() returns null without it
                if (RestartBattlePlugin.SavedMapHandler != null)
                {
                    var mhField = AccessTools.Field(handlerType, "_mapHandler");
                    mhField?.SetValue(handler, RestartBattlePlugin.SavedMapHandler);
                    RestartBattlePlugin.Log.LogInfo("_mapHandler restored.");
                }

                var runMethod  = AccessTools.Method(handlerType, "Run");
                var enumerator = (IEnumerator)runMethod.Invoke(
                    handler, new object[] { RestartBattlePlugin.SavedRunArg });

                if (enumerator != null)
                {
                    // Stop previous re-run coroutine if it's still running
                    if (RestartBattlePlugin.ActiveRunCoroutine != null)
                    {
                        CoroutineHelper.Instance.StopCoroutine(RestartBattlePlugin.ActiveRunCoroutine);
                        RestartBattlePlugin.Log.LogInfo("Stopped previous Run coroutine.");
                        RestartBattlePlugin.ActiveRunCoroutine = null;
                    }
                    RestartBattlePlugin.ActiveRunCoroutine =
                        CoroutineHelper.Instance.StartCoroutine(enumerator);
                    RestartBattlePlugin.Log.LogInfo("MapEncounterHandler.Run re-started successfully.");
                }
                else
                {
                    RestartBattlePlugin.Log.LogError("Run() still returned null after _mapHandler restore.");
                }
            }
            catch (System.Exception e)
            {
                RestartBattlePlugin.Log.LogError($"Re-run MapEncounterHandler.Run failed: {e}");
            }
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

    // ── Combat end — all paths ────────────────────────────────────────────────
    [HarmonyPatch(typeof(MapEncounterHandler), "OnCombatEnded")]
    public static class CombatEndedPatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            RestartBattlePlugin.IsInCombat = false;
            RestartBattlePlugin.ActiveRunCoroutine = null;
            RestartBattlePlugin.Log.LogInfo("Combat ended (MapEncounterHandler).");
        }
    }

    [HarmonyPatch(typeof(CombatTimeline), "DoPlayVictory")]
    public static class VictoryPatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            RestartBattlePlugin.IsInCombat = false;
            RestartBattlePlugin.Log.LogInfo("Combat ended (Victory).");
        }
    }

    [HarmonyPatch(typeof(CombatTimeline), "DoPlayDefeat")]
    public static class DefeatPatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            RestartBattlePlugin.IsInCombat = false;
            RestartBattlePlugin.Log.LogInfo("Combat ended (Defeat).");
        }
    }

    [HarmonyPatch(typeof(CombatTimeline), "DoPlayFinalBossVictory")]
    public static class FinalVictoryPatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            RestartBattlePlugin.IsInCombat = false;
            RestartBattlePlugin.Log.LogInfo("Combat ended (Final Boss Victory).");
        }
    }

    [HarmonyPatch(typeof(CombatTimeline), "DoPlayFinalBossDefeat")]
    public static class FinalDefeatPatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            RestartBattlePlugin.IsInCombat = false;
            RestartBattlePlugin.Log.LogInfo("Combat ended (Final Boss Defeat).");
        }
    }

    // ── Pause popup injection ─────────────────────────────────────────────────
    [HarmonyPatch(typeof(PausePopup), "OnEnable")]
    public static class PausePopupPatch
    {
        [HarmonyPostfix]
        static void Postfix(PausePopup __instance)
        {
            if (!RestartBattlePlugin.IsInCombat)
            {
                var stale = __instance.GetComponentInChildren<RestartBattleTag>(true);
                if (stale != null) GameObject.Destroy(stale.gameObject);
                return;
            }

            var existing = __instance.GetComponentInChildren<RestartBattleTag>(true);
            if (existing != null)
            {
                existing.GetComponent<RestartBattleConfirm>()?.ForceLabel();
                return;
            }

            var allButtons = __instance.GetComponentsInChildren<Button>(true);
            var abandonBtn = allButtons.FirstOrDefault(b =>
                b.GetComponentInChildren<TextMeshProUGUI>()?.text == "Abandon Run");

            if (abandonBtn == null) { RestartBattlePlugin.Log.LogWarning("Abandon Run not found."); return; }

            var menuParent = abandonBtn.transform.parent;
            var newBtn     = GameObject.Instantiate(abandonBtn, menuParent);
            newBtn.name    = "RestartBattleButton";
            newBtn.transform.SetSiblingIndex(0);

            newBtn.gameObject.AddComponent<RestartBattleTag>();
            var confirm = newBtn.gameObject.AddComponent<RestartBattleConfirm>();

            newBtn.GetComponent<Button>().onClick.RemoveAllListeners();
            newBtn.GetComponent<Button>().onClick.AddListener(confirm.OnClicked);

            confirm.ForceLabel();
            RestartBattlePlugin.Log.LogInfo("Restart Battle button injected.");
        }
    }
}
