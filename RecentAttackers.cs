using System;
using System.Collections.Generic;

namespace Landoria.SealedTombstone
{
    internal static class RecentAttackers
    {
        private static readonly Dictionary<long, DateTime> LastHits =
            new Dictionary<long, DateTime>();

        private static long[] _deathSnapshot = new long[0];

        internal static void Record(Player victim, HitData hit)
        {
            Player attacker = hit?.GetAttacker() as Player;
            long attackerId = attacker?.GetPlayerID() ?? 0L;
            if (!RecentAttackerPolicy.ShouldRecord(
                    victim == Player.m_localPlayer, attacker != null,
                    victim.GetPlayerID(), attackerId))
            {
                return;
            }
            LastHits[attackerId] = DateTime.UtcNow;
        }

        internal static void SnapshotForDeath(Player player)
        {
            if (player != Player.m_localPlayer)
            {
                return;
            }

            _deathSnapshot = RecentAttackerPolicy.Snapshot(LastHits, DateTime.UtcNow);
            LastHits.Clear();
            SealedTombstonePlugin.Log.LogDebug($"Captured {_deathSnapshot.Length} recent PvP attackers.");
        }

        internal static string ConsumeSnapshot()
        {
            string value = RecentAttackerPolicy.Serialize(_deathSnapshot);
            _deathSnapshot = new long[0];
            return value;
        }

        internal static bool Contains(string serializedIds, long playerId)
        {
            return RecentAttackerPolicy.Contains(serializedIds, playerId);
        }

        internal static void Reset()
        {
            LastHits.Clear();
            _deathSnapshot = new long[0];
        }
    }
}
