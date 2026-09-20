using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace Landoria.SealedTombstone
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class SealedTombstonePlugin : BaseUnityPlugin
    {
        private const string PluginGuid = "Landoria.SealedTombstone";
        private const string PluginName = "Landoria.SealedTombstone";
        private const string PluginVersion = "1.0.10";


        internal static ManualLogSource Log { get; private set; }


        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Logger.LogInfo($"AssemblyVersion: {GetType().Assembly.GetName().Version}.");
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Log.LogInfo($"{PluginName} {PluginVersion} is loaded.");
        }

        private void Update()
        {
            TombstoneAccess.Tick();
        }

        private void OnDestroy()
        {
            TombstoneAccess.ResetSession();
            Log?.LogInfo($"{PluginName} {PluginVersion} is unloaded.");
            _harmony?.UnpatchSelf();
            _harmony = null;
            Log = null;
        }
    }
}
