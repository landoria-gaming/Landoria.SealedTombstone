using HarmonyLib;

namespace Landoria.SealedTombstone
{
    [HarmonyPatch(typeof(Game), "Start")]
    internal static class RpcRegistrationPatch
    {
        private static void Postfix()
        {
            TombstoneAccess.RegisterRpcs();
        }
    }

    [HarmonyPatch(typeof(TombStone), nameof(TombStone.Setup))]
    internal static class TombstoneSetupPatch
    {
        private static void Postfix(TombStone __instance, long ownerUID)
        {
            TombstoneAccess.RecordLockDay(__instance, ownerUID);
        }
    }

    [HarmonyPatch(typeof(TombStone), nameof(TombStone.Interact))]
    internal static class TombstoneInteractPatch
    {
        private static bool Prefix(TombStone __instance, Humanoid character)
        {
            return TombstoneAccess.AllowInteraction(__instance, character);
        }
    }

    [HarmonyPatch(typeof(Player), "OnDamaged")]
    internal static class PlayerDamagedPatch
    {
        private static void Postfix(Player __instance, HitData hit)
        {
            RecentAttackers.Record(__instance, hit);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
    internal static class PlayerDeathPatch
    {
        private static void Prefix(Player __instance)
        {
            RecentAttackers.SnapshotForDeath(__instance);
        }
    }
}
