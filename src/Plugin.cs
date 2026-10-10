using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AtOShufflePeek
{
    [BepInPlugin("karmanov.ato.shufflepeek", "ShufflePeek", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> PositionX;
        internal static ConfigEntry<int> PositionY;
        internal static ConfigEntry<int> FontSize;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;

        internal static OverlayBehaviour Overlay;

        private void Awake()
        {
            Log = Logger;

            try
            {
                Enabled = Config.Bind("General", "Enabled", true,
                    "Show the card layout overlay after the shuffle.");
                PositionX = Config.Bind("Overlay", "PositionX", 20,
                    "Overlay left edge in screen pixels.");
                PositionY = Config.Bind("Overlay", "PositionY", 20,
                    "Overlay top edge in screen pixels.");
                FontSize = Config.Bind("Overlay", "FontSize", 18,
                    new ConfigDescription("Overlay font size.", new AcceptableValueRange<int>(10, 48)));
                ToggleKey = Config.Bind("Hotkey", "Toggle", new KeyboardShortcut(KeyCode.F8),
                    "Toggle the overlay while a layout is captured.");

                var host = new GameObject("ShufflePeek");
                DontDestroyOnLoad(host);
                host.hideFlags = HideFlags.HideAndDontSave;
                Overlay = host.AddComponent<OverlayBehaviour>();

                SceneManager.activeSceneChanged += OnActiveSceneChanged;

                Patches.Apply(new Harmony("karmanov.ato.shufflepeek"));

                Log.LogInfo("ShufflePeek 1.0.0 loaded");
            }
            catch (System.Exception e)
            {
                Log.LogError($"ShufflePeek failed to initialise: {e}");
            }
        }

        private static void OnActiveSceneChanged(Scene from, Scene to)
        {
            try
            {
                if (Overlay != null)
                    Overlay.Hide();
            }
            catch (System.Exception e)
            {
                Log.LogError($"Scene change handler failed: {e}");
            }
        }
    }
}
