using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Landoria.SealedTombstone
{
    internal static class RecentAttackerPolicy
    {
        internal static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

        internal static bool ShouldRecord(
            bool victimIsLocal, bool hasPlayerAttacker,
            long victimId, long attackerId)
        {
            return victimIsLocal && hasPlayerAttacker && attackerId != 0L &&
                   attackerId != victimId;
        }

        internal static long[] Snapshot(
            IReadOnlyDictionary<long, DateTime> lastHits, DateTime deathAt)
        {
            DateTime threshold = deathAt - Window;
            return lastHits.Where(entry => entry.Value >= threshold)
                .Select(entry => entry.Key).ToArray();
        }

        internal static string Serialize(IEnumerable<long> playerIds) =>
            string.Join(",", playerIds.Select(id =>
                id.ToString(CultureInfo.InvariantCulture)));

        internal static bool Contains(string serializedIds, long playerId)
        {
            if (string.IsNullOrEmpty(serializedIds)) return false;
            string expected = playerId.ToString(CultureInfo.InvariantCulture);
            return serializedIds.Split(',').Any(id => id == expected);
        }
    }
}
