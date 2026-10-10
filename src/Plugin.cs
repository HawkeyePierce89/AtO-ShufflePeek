using BepInEx;

namespace AtOShufflePeek
{
    [BepInPlugin("karmanov.ato.shufflepeek", "ShufflePeek", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            Logger.LogInfo("ShufflePeek 1.0.0 loaded");
        }
    }
}
