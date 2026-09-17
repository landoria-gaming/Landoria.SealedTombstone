using System;
using System.Collections.Generic;

namespace Landoria.SealedTombstone
{
    internal static class TombstoneAccess
    {
        private const string LockDayKey = "Landoria.SealedTombstone.LockDay";
        private const string BlockedPlayersKey = "Landoria.SealedTombstone.BlockedPlayers";
        private const string AccessCheckRpc = "Landoria_SealedTombstone_AccessCheck";
        private const string IdentityRpc = "Landoria_SealedTombstone_Identity";
        private const string RequestRpc = "Landoria_SealedTombstone_Request";
        private const string AvailabilityRpc = "Landoria_SealedTombstone_Availability";
        private const string DecisionSubmitRpc = "Landoria_SealedTombstone_DecisionSubmit";
        private const string DecisionRpc = "Landoria_SealedTombstone_Decision";

        private static PendingRequest _pendingRequest;
        private static DateTime _lastRequestAt = DateTime.MinValue;
        private static ZRoutedRpc _registeredRpc;
        private static readonly Dictionary<long, long> PeerPlayers = new Dictionary<long, long>();
        private static long _identityServer;

        internal static void RegisterRpcs()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _registeredRpc))
            {
                return;
            }

            rpc.Register<long, string, ZDOID, long>(AccessCheckRpc, ReceiveAccessCheck);
            rpc.Register<long>(IdentityRpc, ReceiveIdentity);
            rpc.Register<long, string, ZDOID, long>(RequestRpc, ReceiveRequest);
            rpc.Register<bool>(AvailabilityRpc, ReceiveAvailability);
            rpc.Register<long, ZDOID, bool, string, long>(
                DecisionSubmitRpc, ReceiveDecisionSubmission);
            rpc.Register<long, ZDOID, bool, string>(DecisionRpc, ReceiveDecision);
            _registeredRpc = rpc;
            SealedTombstonePlugin.Log.LogDebug("Tombstone access RPCs registered for the current session.");
        }

        internal static void ResetSession()
        {
            _pendingRequest = null;
            _lastRequestAt = DateTime.MinValue;
            _registeredRpc = null;
            PeerPlayers.Clear();
            _identityServer = 0L;
            RecentAttackers.Reset();
        }

        internal static void RecordLockDay(TombStone tombstone, long ownerId)
        {
            ZNetView view = tombstone.GetComponent<ZNetView>();
            ZDO zdo = view?.GetZDO();
            if (ownerId == 0L || zdo == null || !view.IsOwner())
            {
                return;
            }

            zdo.Set(LockDayKey, CurrentDay());
            zdo.Set(BlockedPlayersKey, RecentAttackers.ConsumeSnapshot());
            SealedTombstonePlugin.Log.LogDebug($"Locked tombstone {zdo.m_uid} for player {ownerId}.");
        }

        internal static bool AllowInteraction(TombStone tombstone, Humanoid character)
        {
            Player player = character as Player;
            ZDO zdo = tombstone.GetComponent<ZNetView>()?.GetZDO();
            if (player == null || zdo == null)
            {
                return true;
            }

            long playerId = player.GetPlayerID();
            long ownerId = zdo.GetLong(ZDOVars.s_owner, 0L);
            TombstoneInteraction interaction = TombstoneAccessPolicy.Evaluate(
                true, ownerId, playerId, zdo.GetLong(LockDayKey, -1L), CurrentDay(),
                IsBlocked(zdo, playerId));
            if (interaction == TombstoneInteraction.Allow)
            {
                return true;
            }
            if (interaction == TombstoneInteraction.Block)
            {
                player.Message(MessageHud.MessageType.Center,
                    "You cannot request access to this tombstone.");
                SealedTombstonePlugin.Log.LogDebug($"Blocked tombstone request from recent attacker {player.GetPlayerID()}.");
                return false;
            }

            RequestAccess(player, ownerId, zdo.m_uid);
            return false;
        }

        internal static void Tick()
        {
            SyncIdentity();
            if (_pendingRequest == null || !HasExpired(_pendingRequest))
            {
                return;
            }

            SealedTombstonePlugin.Log.LogDebug($"Request from {_pendingRequest.RequesterPlayerId} expired.");
            ClosePopup();
            SendDecision(_pendingRequest, accepted: false);
            _pendingRequest = null;
        }

        private static void RequestAccess(Player requester, long ownerId, ZDOID tombstoneId)
        {
            if (TombstoneAccessPolicy.IsCooldownActive(_lastRequestAt, DateTime.UtcNow))
            {
                requester.Message(MessageHud.MessageType.Center, "Please wait before sending another request.");
                return;
            }

            ZRoutedRpc rpc = ZRoutedRpc.instance;
            ZNet network = ZNet.instance;
            if (rpc == null || network == null)
            {
                requester.Message(MessageHud.MessageType.Center, "The tombstone owner is offline.");
                return;
            }

            long requesterId = requester.GetPlayerID();
            if (network.IsServer())
            {
                ProcessAccessCheck(0L, requesterId, requester.GetPlayerName(), tombstoneId, ownerId);
                return;
            }

            ZNetPeer server = network.GetServerPeer();
            if (server == null)
            {
                requester.Message(MessageHud.MessageType.Center, "The tombstone owner is offline.");
                return;
            }
            rpc.InvokeRoutedRPC(server.m_uid, AccessCheckRpc,
                requesterId, requester.GetPlayerName(), tombstoneId, ownerId);
        }

        private static void ReceiveAccessCheck(
            long sender, long requesterId, string requesterName, ZDOID tombstoneId, long ownerId)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }
            ZNetPeer requester = ZNet.instance.GetPeer(sender);
            if (requester == null || requesterId == 0L)
            {
                return;
            }
            PeerPlayers[sender] = requesterId;
            ProcessAccessCheck(sender, requesterId, requesterName, tombstoneId, ownerId);
        }

        private static void ProcessAccessCheck(
            long requesterPeer, long requesterId, string requesterName, ZDOID tombstoneId, long ownerId)
        {
            long ownerPeer = FindOnlinePeer(ownerId);
            bool ownerIsLocal = Player.m_localPlayer?.GetPlayerID() == ownerId;
            SendAvailability(requesterPeer, ownerPeer != 0L || ownerIsLocal);
            if (ownerIsLocal)
            {
                ReceiveRequest(0L, requesterId, requesterName, tombstoneId, ownerId);
            }
            else if (ownerPeer != 0L)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(ownerPeer, RequestRpc,
                    requesterId, requesterName, tombstoneId, ownerId);
            }
        }

        private static long FindOnlinePeer(long playerId)
        {
            foreach (KeyValuePair<long, long> mapping in PeerPlayers)
            {
                ZNetPeer peer = ZNet.instance.GetPeer(mapping.Key);
                if (mapping.Value == playerId && peer != null && peer.IsReady())
                {
                    return mapping.Key;
                }
            }
            return 0L;
        }

        private static void SyncIdentity()
        {
            ZNet network = ZNet.instance;
            Player player = Player.m_localPlayer;
            if (network == null || player == null || network.IsServer())
            {
                return;
            }
            ZNetPeer server = network.GetServerPeer();
            if (server == null || server.m_uid == _identityServer)
            {
                return;
            }
            _identityServer = server.m_uid;
            ZRoutedRpc.instance?.InvokeRoutedRPC(server.m_uid, IdentityRpc, player.GetPlayerID());
        }

        private static void ReceiveIdentity(long sender, long playerId)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() ||
                ZNet.instance.GetPeer(sender) == null || playerId == 0L)
            {
                return;
            }
            PeerPlayers[sender] = playerId;
        }

        private static void SendAvailability(long requesterPeer, bool online)
        {
            if (requesterPeer == 0L)
            {
                ReceiveAvailability(0L, online);
                return;
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(requesterPeer, AvailabilityRpc, online);
        }

        private static void ReceiveAvailability(long sender, bool ownerOnline)
        {
            Player requester = Player.m_localPlayer;
            if (requester == null || !IsServerResponse(sender))
            {
                return;
            }
            TombstoneAvailabilityResult result = TombstoneRequestPolicy.ApplyAvailability(
                ownerOnline, _lastRequestAt, DateTime.UtcNow);
            _lastRequestAt = result.LastRequestAt;
            requester.Message(MessageHud.MessageType.Center, result.Message);
            if (!ownerOnline) return;
            SealedTombstonePlugin.Log.LogInfo($"Player {requester.GetPlayerID()} requested tombstone access.");
        }

        private static bool IsServerResponse(long sender)
        {
            ZNet network = ZNet.instance;
            if (network == null) return false;
            ZNetPeer server = network.GetServerPeer();
            return TombstoneDecisionPolicy.IsTrustedResponse(
                network.IsServer(), sender, server != null, server?.m_uid ?? 0L);
        }

        private static void ReceiveRequest(
            long sender,
            long requesterId,
            string requesterName,
            ZDOID tombstoneId,
            long ownerId)
        {
            Player owner = Player.m_localPlayer;
            ZDO tombstone = ZDOMan.instance?.GetZDO(tombstoneId);
            if (!IsServerResponse(sender) || owner == null || owner.GetPlayerID() != ownerId ||
                _pendingRequest != null ||
                IsBlocked(tombstone, requesterId))
            {
                return;
            }

            _pendingRequest = new PendingRequest
            {
                RequesterPlayerId = requesterId,
                RequesterName = SafeName(requesterName),
                OwnerPlayerId = ownerId,
                TombstoneId = tombstoneId,
                CreatedAt = DateTime.UtcNow
            };
            ShowDecisionPopup(_pendingRequest);
        }

        private static void ShowDecisionPopup(PendingRequest request)
        {
            if (!UnifiedPopup.IsAvailable())
            {
                SealedTombstonePlugin.Log.LogWarning("The unlock request was rejected because the VV popup is unavailable.");
                Decide(request, accepted: false);
                return;
            }

            TombstoneRequestPresentation presentation =
                TombstonePresentationPolicy.Build(request.RequesterName);
            UnifiedPopup.Push(new YesNoPopup(presentation.Title, presentation.Message,
                () => Decide(request, accepted: true),
                () => Decide(request, accepted: false), localizeText: false));
        }

        private static void Decide(PendingRequest request, bool accepted)
        {
            if (!ReferenceEquals(request, _pendingRequest) || HasExpired(request))
            {
                ClosePopup();
                _pendingRequest = null;
                return;
            }

            ClosePopup();
            _pendingRequest = null;
            SendDecision(request, accepted);
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center,
                accepted ? "Tombstone access granted." : "Tombstone access denied.");
        }

        private static void SendDecision(PendingRequest request, bool accepted)
        {
            Player owner = Player.m_localPlayer;
            if (owner == null || ZRoutedRpc.instance == null)
            {
                return;
            }

            ZNet network = ZNet.instance;
            if (network == null) return;
            if (network.IsServer())
            {
                ForwardDecision(0L, request.RequesterPlayerId, request.TombstoneId,
                    accepted, owner.GetPlayerName(), request.OwnerPlayerId);
            }
            else if (network.GetServerPeer() is ZNetPeer server)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(server.m_uid, DecisionSubmitRpc,
                    request.RequesterPlayerId, request.TombstoneId, accepted,
                    owner.GetPlayerName(), request.OwnerPlayerId);
            }
            SealedTombstonePlugin.Log.LogInfo($"Tombstone request from {request.RequesterPlayerId} accepted={accepted}.");
        }

        private static void ReceiveDecisionSubmission(long sender, long requesterId,
            ZDOID tombstoneId, bool accepted, string ownerName, long ownerId)
        {
            ForwardDecision(sender, requesterId, tombstoneId, accepted, ownerName, ownerId);
        }

        private static void ForwardDecision(long sender, long requesterId, ZDOID tombstoneId,
            bool accepted, string ownerName, long ownerId)
        {
            ZNet network = ZNet.instance;
            ZDO tombstone = ZDOMan.instance?.GetZDO(tombstoneId);
            long mappedOwner = sender == 0L ? ownerId :
                (PeerPlayers.TryGetValue(sender, out long playerId) ? playerId : 0L);
            bool senderExists = sender == 0L || network?.GetPeer(sender) != null;
            if (!TombstoneDecisionPolicy.CanForward(network?.IsServer() == true,
                    senderExists, mappedOwner, ownerId,
                    tombstone?.GetLong(ZDOVars.s_owner, 0L) ?? 0L)) return;
            long requesterPeer = FindOnlinePeer(requesterId);
            if (requesterPeer == 0L && Player.m_localPlayer?.GetPlayerID() == requesterId)
            {
                ReceiveDecision(0L, requesterId, tombstoneId, accepted, ownerName);
                return;
            }
            if (requesterPeer != 0L)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(requesterPeer, DecisionRpc,
                    requesterId, tombstoneId, accepted, ownerName);
            }
        }

        private static void ReceiveDecision(
            long sender,
            long requesterId,
            ZDOID tombstoneId,
            bool accepted,
            string ownerName)
        {
            Player requester = Player.m_localPlayer;
            if (requester == null || requester.GetPlayerID() != requesterId ||
                !IsServerResponse(sender))
            {
                return;
            }

            if (TombstoneDecisionPolicy.ShouldUnlock(accepted))
            {
                Unlock(tombstoneId);
            }

            requester.Message(MessageHud.MessageType.Center,
                TombstoneRequestPolicy.DecisionMessage(accepted, ownerName));
        }

        private static void Unlock(ZDOID tombstoneId)
        {
            ZNetView view = ZNetScene.instance?.FindInstance(tombstoneId)?.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                SealedTombstonePlugin.Log.LogWarning($"Cannot unlock unavailable tombstone {tombstoneId}.");
                return;
            }

            view.ClaimOwnership();
            view.GetZDO().Set(ZDOVars.s_owner, 0L);
            SealedTombstonePlugin.Log.LogInfo($"Tombstone {tombstoneId} was unlocked.");
        }

        private static bool IsBlocked(ZDO tombstone, long playerId)
        {
            return tombstone != null && RecentAttackers.Contains(
                tombstone.GetString(BlockedPlayersKey), playerId);
        }

        private static long CurrentDay()
        {
            return EnvMan.instance != null ? EnvMan.instance.GetDay() : -1L;
        }

        private static bool HasExpired(PendingRequest request)
        {
            return TombstoneAccessPolicy.HasRequestExpired(request.CreatedAt, DateTime.UtcNow);
        }

        private static string SafeName(string name)
        {
            return TombstoneAccessPolicy.SafeName(name);
        }

        private static void ClosePopup()
        {
            if (UnifiedPopup.IsAvailable() && UnifiedPopup.IsVisible())
            {
                UnifiedPopup.Pop();
            }
        }
    }
}
