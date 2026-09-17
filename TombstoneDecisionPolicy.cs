namespace Landoria.SealedTombstone
{
    internal static class TombstoneDecisionPolicy
    {
        internal static bool CanForward(
            bool isServer, bool senderPeerExists, long mappedPlayerId,
            long expectedOwnerId, long tombstoneOwnerId)
        {
            return isServer && senderPeerExists && mappedPlayerId != 0L &&
                   mappedPlayerId == expectedOwnerId && tombstoneOwnerId == expectedOwnerId;
        }

        internal static bool IsTrustedResponse(
            bool isServerHost, long sender, bool hasServerPeer, long serverPeerId)
        {
            return isServerHost ? sender == 0L : hasServerPeer && sender == serverPeerId;
        }

        internal static bool ShouldUnlock(bool accepted) => accepted;
    }
}
