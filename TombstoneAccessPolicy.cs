using System;

namespace Landoria.SealedTombstone
{
    internal enum TombstoneInteraction { Allow, RequestAccess, Block }

    internal static class TombstoneAccessPolicy
    {
        internal const int UnlockAfterDays = 10;
        internal static readonly TimeSpan RequestExpiry = TimeSpan.FromSeconds(30);
        internal static readonly TimeSpan RequestCooldown = TimeSpan.FromMinutes(2);

        internal static TombstoneInteraction Evaluate(
            bool hasPlayerAndTombstone, long ownerId, long playerId,
            long lockDay, long currentDay, bool blocked)
        {
            if (!hasPlayerAndTombstone || ownerId == 0L || ownerId == playerId)
            {
                return TombstoneInteraction.Allow;
            }
            if (blocked) return TombstoneInteraction.Block;
            return IsExpired(lockDay, currentDay)
                ? TombstoneInteraction.Allow
                : TombstoneInteraction.RequestAccess;
        }

        internal static bool IsExpired(long lockDay, long currentDay) =>
            lockDay >= 0L && currentDay >= 0L &&
            currentDay - lockDay >= UnlockAfterDays;

        internal static bool HasRequestExpired(DateTime createdAt, DateTime now) =>
            now - createdAt > RequestExpiry;

        internal static bool IsCooldownActive(DateTime lastRequestAt, DateTime now) =>
            now - lastRequestAt < RequestCooldown;

        internal static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "A player";
            string safeName = name.Replace("<", string.Empty).Replace(">", string.Empty);
            return safeName.Length <= 64 ? safeName : safeName.Substring(0, 64);
        }
    }

    internal sealed class TombstoneAvailabilityResult
    {
        internal DateTime LastRequestAt;
        internal string Message;
    }

    internal static class TombstoneRequestPolicy
    {
        internal static TombstoneAvailabilityResult ApplyAvailability(
            bool ownerOnline, DateTime lastRequestAt, DateTime now)
        {
            return new TombstoneAvailabilityResult
            {
                LastRequestAt = ownerOnline ? now : lastRequestAt,
                Message = ownerOnline
                    ? "Access request sent to the tombstone owner."
                    : "The tombstone owner is offline."
            };
        }

        internal static string DecisionMessage(bool accepted, string ownerName) =>
            accepted
                ? TombstoneAccessPolicy.SafeName(ownerName) +
                  " granted access to the tombstone."
                : TombstoneAccessPolicy.SafeName(ownerName) +
                  " denied or did not answer the request.";
    }

    internal sealed class TombstoneRequestPresentation
    {
        internal string Title;
        internal string Message;
    }

    internal static class TombstonePresentationPolicy
    {
        internal static TombstoneRequestPresentation Build(string requesterName) =>
            new TombstoneRequestPresentation
            {
                Title = "Tombstone access request",
                Message = "Allow " + TombstoneAccessPolicy.SafeName(requesterName) +
                          " to loot this tombstone?"
            };
    }
}
