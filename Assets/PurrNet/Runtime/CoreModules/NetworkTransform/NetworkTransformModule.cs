using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;
using Unity.Profiling;

namespace PurrNet.Modules
{
    public class NetworkTransformModule : INetworkModule, IPromoteToServerModule
    {
        public static long entriesWrittenCount;
        public static long adaptiveHoldCount;
        public static long sharedPacketCount;

        static readonly ProfilerMarker _postFixedUpdateMarker = new ProfilerMarker("NetworkTransform.PostFixedUpdate");
        static readonly ProfilerMarker _gatherStateMarker = new ProfilerMarker("NetworkTransform.GatherState");
        static readonly ProfilerMarker _prepareUnreliableMarker = new ProfilerMarker("NetworkTransform.PrepareState");
        static readonly ProfilerMarker _queueChangedMarker = new ProfilerMarker("NetworkTransform.QueueChangedState");
        static readonly ProfilerMarker _decodeUnreliableMarker = new ProfilerMarker("NetworkTransform.DecodeState");
        static readonly ProfilerMarker _processAckMarker = new ProfilerMarker("NetworkTransform.ProcessAck");
        static readonly ProfilerMarker _replayMarker = new ProfilerMarker("NetworkTransform.ReplayState");
        static readonly ProfilerMarker _flushAckMarker = new ProfilerMarker("NetworkTransform.FlushAck");
        static readonly ProfilerMarker _shareKeysMarker = new ProfilerMarker("NetworkTransform.ShareKeys");
        static readonly ProfilerMarker _commitMarker = new ProfilerMarker("NetworkTransform.CommitPacket");

        private readonly List<NetworkTransform> _networkTransforms = new();
        private readonly List<NetworkTransform> _byIndex = new();
        private readonly Stack<int> _freeIndices = new();
        private readonly List<NetworkTransform> _changedTransforms = new();
        private readonly Dictionary<PlayerID, NTUnreliableSendStream> _sendStreams = new();
        private readonly Dictionary<PlayerID, NTUnreliableRecvStream> _recvStreams = new();
        private readonly ScenePlayersModule _scenePlayers;
        private readonly PlayersBroadcaster _broadcaster;
        private readonly NetworkManager _manager;
        private readonly SceneID _scene;
        private readonly HierarchyFactory _factory;
        private bool _asServer;
        private ushort _currentTick;

        public NetworkTransformModule(NetworkManager manager, PlayersBroadcaster broadcaster,
            ScenePlayersModule scenePlayers, SceneID scene, HierarchyFactory factory)
        {
            _manager = manager;
            _scenePlayers = scenePlayers;
            _broadcaster = broadcaster;
            _scene = scene;
            _factory = factory;
        }

        public void PromoteToServerModule()
        {
            for (var i = 0; i < _networkTransforms.Count; i++)
                _networkTransforms[i].PromoteNTRegistrationToServer();

            _asServer = true;
            ReleaseAllStreams();

            for (var i = 0; i < _networkTransforms.Count; i++)
                _networkTransforms[i].ResetUnreliableStream();
        }

        public void PostPromoteToServerModule()
        {
        }

        public void Enable(bool asServer)
        {
            _asServer = asServer;
            _broadcaster.Subscribe<NetworkTransformUnreliableDelta>(OnUnreliableDelta);
            _broadcaster.Subscribe<NetworkTransformUnreliableAck>(OnUnreliableAck);
            _broadcaster.Subscribe<NetworkTransformUnreliableNack>(OnUnreliableNack);
            _scenePlayers.onPlayerUnloadedScene += OnPlayerUnloadedScene;
        }

        public void Disable(bool asServer)
        {
            _broadcaster.Unsubscribe<NetworkTransformUnreliableDelta>(OnUnreliableDelta);
            _broadcaster.Unsubscribe<NetworkTransformUnreliableAck>(OnUnreliableAck);
            _broadcaster.Unsubscribe<NetworkTransformUnreliableNack>(OnUnreliableNack);
            _scenePlayers.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
            ReleaseAllStreams();
        }

        private void ReleaseAllStreams()
        {
            foreach (var stream in _sendStreams.Values)
                NTUnreliable.Release(stream.ring);
            foreach (var stream in _recvStreams.Values)
                NTUnreliable.Release(stream.ring);
            _sendStreams.Clear();
            _recvStreams.Clear();
        }

        private void OnPlayerUnloadedScene(PlayerID player, SceneID scene, bool asServer)
        {
            if (scene != _scene)
                return;

            // Client streams are keyed PlayerID.Server; when the local player leaves the scene
            // both ends must restart, or a re-join pairs a fresh sender seq with a stale recv window.
            if (!asServer)
            {
                ReleaseAllStreams();
                return;
            }

            if (_sendStreams.Remove(player, out var send))
                NTUnreliable.Release(send.ring);
            if (_recvStreams.Remove(player, out var recv))
                NTUnreliable.Release(recv.ring);
        }

        private bool IsLive(NetworkTransform nt) => nt is not null && nt.GetNTIndex(_asServer) >= 0;

        private void ClearBaseline(NTUnreliableSendStream stream, NetworkID nid)
        {
            if (!TryGetRegisteredTransform(nid, out var nt))
                return;

            int index = nt.GetNTIndex(_asServer);
            if (index >= 0 && index < stream.baselines.Length)
                stream.baselines[index] = default;
        }

        private void ClearAdaptive(NTUnreliableSendStream stream, NetworkID nid)
        {
            if (TryGetRegisteredTransform(nid, out var nt))
                stream.SetAdaptive(nt, null);
        }

        internal NTUnreliableSendStream GetSendStream(PlayerID player)
        {
            if (!_sendStreams.TryGetValue(player, out var stream))
            {
                stream = new NTUnreliableSendStream { asServer = _asServer };
                stream.EnsureBaselineCapacity(_byIndex.Count);
                _sendStreams.Add(player, stream);
            }

            return stream;
        }

        internal static void AddPending(NTUnreliableSendStream stream, NetworkTransform nt)
        {
            if (!nt || !nt.id.HasValue)
                return;

            var nid = nt.id.Value;
            int lo = 0;
            int hi = stream.pending.Count - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                var midNt = stream.pending[mid];
                if (!midNt || !midNt.id.HasValue)
                {
                    stream.pending.RemoveAt(mid);
                    hi = stream.pending.Count - 1;
                    continue;
                }

                var midNid = midNt.id.Value;
                if (midNid.Equals(nid))
                {
                    stream.pending[mid] = nt;
                    stream.SetPending(nt, true);
                    return;
                }

                if (midNid < nid)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            stream.pending.Insert(lo, nt);
            stream.SetPending(nt, true);
        }

        internal static bool RemovePending(NTUnreliableSendStream stream, NetworkID nid)
        {
            int lo = 0;
            int hi = stream.pending.Count - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                var midNt = stream.pending[mid];
                if (!midNt || !midNt.id.HasValue)
                {
                    stream.pending.RemoveAt(mid);
                    hi = stream.pending.Count - 1;
                    continue;
                }

                var midNid = midNt.id.Value;
                if (midNid.Equals(nid))
                {
                    stream.pending.RemoveAt(mid);
                    stream.SetPending(midNt, false);
                    return true;
                }

                if (midNid < nid)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            return false;
        }

        internal NTUnreliableRecvStream GetRecvStream(PlayerID player)
        {
            if (!_recvStreams.TryGetValue(player, out var stream))
            {
                stream = new NTUnreliableRecvStream();
                _recvStreams.Add(player, stream);
            }

            return stream;
        }

        internal static bool MarkReceived(NTUnreliableRecvStream stream, ushort seq, out long packetOrder)
        {
            if (!stream.ackInit)
            {
                stream.ackInit = true;
                stream.latestSeq = seq;
                stream.latestOrder = 0;
                stream.ackBits = 0;
                packetOrder = 0;
                return true;
            }

            var diff = (short)(seq - stream.latestSeq);

            switch (diff)
            {
                case > 0:
                {
                    stream.ackBits = diff switch
                    {
                        >= 33 => 0,
                        32 => 1u << 31,
                        _ => (stream.ackBits << diff) | (1u << (diff - 1))
                    };

                    stream.latestOrder += diff;
                    stream.latestSeq = seq;
                    packetOrder = stream.latestOrder;
                    return true;
                }
                case 0:
                    packetOrder = stream.latestOrder;
                    return false;
            }

            int d = -diff;
            if (d > 32)
            {
                packetOrder = stream.latestOrder - d;
                return false;
            }

            uint mask = 1u << (d - 1);
            if ((stream.ackBits & mask) != 0)
            {
                packetOrder = stream.latestOrder - d;
                return false;
            }

            stream.ackBits |= mask;
            packetOrder = stream.latestOrder - d;
            return true;
        }

        private static bool TryGetRecvBaseline(NTUnreliableRecvStream stream, ushort seq, NetworkID nid,
            out NetworkTransformState state, out NetworkTransformVelocity velocity, out byte gen, out ushort tick)
        {
            state = default;
            velocity = default;
            gen = default;
            tick = default;

            ref var slot = ref stream.ring[seq % NTUnreliable.RING_SIZE];
            if (!slot.used || slot.seq != seq || slot.entries == null)
                return false;

            // Entries are ascending-nid by construction (packets are written from the id-sorted list).
            var list = slot.entries;
            int lo = 0, hi = list.Count - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                var midNid = list[mid].nid;

                if (midNid.Equals(nid))
                {
                    state = list[mid].state;
                    velocity = list[mid].velocity;
                    gen = list[mid].gen;
                    tick = list[mid].tick;
                    return true;
                }

                if (nid > midNid)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            return false;
        }

        internal static bool IsValidEntryBounds(int bodyStart, int bodyLength, int packetEnd)
        {
            return bodyStart >= 0 && bodyStart <= packetEnd &&
                   bodyLength > 0 && bodyLength <= packetEnd - bodyStart;
        }

        private void OnUnreliableDelta(PlayerID player, NetworkTransformUnreliableDelta data, bool asServer)
        {
            if (data.scene != _scene)
                return;

            var sender = asServer ? player : PlayerID.Server;
            var stream = GetRecvStream(sender);

            if (data.ack.HasValue && _sendStreams.TryGetValue(sender, out var sendStream))
            {
                var ack = data.ack.Value;
                ProcessAck(sendStream, ack.seq, ack.ackBits);
            }

            using var decodeScope = _decodeUnreliableMarker.Auto();

            bool oldAckInit = stream.ackInit;
            ushort oldLatestSeq = stream.latestSeq;
            long oldLatestOrder = stream.latestOrder;
            uint oldAckBits = stream.ackBits;
            bool oldAckDirty = stream.ackDirty;

            if (!MarkReceived(stream, data.seq, out var packetOrder))
                return;

            bool committed = false;
            var decoded = ListPool<NTUnreliableEntry>.Instantiate();

            try
            {
                using var packet = BitPackerPool.Get(data.packet);
                packet.ResetPositionAndMode(true);

                long packetEndLong = data.packet.length * 8L;
                if (packetEndLong > int.MaxValue)
                    return;
                int packetEnd = (int)packetEndLong;

                int ntCount = default;
                int lastDist = 0;
                NetworkID lastNid = default;
                PackedInt lastLen = default;

                Packer<int>.Read(packet, ref ntCount);

                // Every entry consumes at least one body bit in addition to its framing.
                // This rejects hostile counts before they can turn a tiny packet into a long loop.
                if (ntCount < 0 || ntCount > packetEnd - packet.positionInBits)
                    return;

                for (var i = 0; i < ntCount; i++)
                {
                    PackedInt length = default;
                    DeltaPacker<PackedInt>.Read(packet, lastLen, ref length);
                    lastLen = length;
                    DeltaPacker<NetworkID>.Read(packet, lastNid, ref lastNid);

                    int bodyStart = packet.positionInBits;
                    if (!IsValidEntryBounds(bodyStart, length.value, packetEnd))
                        return;
                    int bodyEnd = bodyStart + length.value;

                    // The abs/gen/dist header is fixed-layout and MUST be consumed even for entries
                    // that get skipped: the dist chain is cross-entry decoder state.
                    bool isAbsolute = packet.ReadBits(1) == 1;
                    byte gen = default;
                    int dist = 0;

                    if (isAbsolute)
                    {
                        Packer<byte>.Read(packet, ref gen);
                    }
                    else if (packet.ReadBits(1) == 1)
                    {
                        dist = lastDist;
                    }
                    else
                    {
                        dist = (int)packet.ReadBits(NTUnreliable.DISTANCE_BITS) + 1;
                        lastDist = dist;
                    }

                    if (packet.positionInBits > bodyEnd)
                        return;

                    bool recorded = false;

                    if (_factory.TryGetIdentity(_scene, lastNid, out var identity) && identity is NetworkTransform nt &&
                        (!asServer || nt.IsControlling(player, false)))
                    {
                        NetworkTransformState state = default;
                        NetworkTransformVelocity velocity = default;
                        bool ok;

                        if (isAbsolute)
                        {
                            state = nt.ReadAbsoluteState(packet);
                            ok = true;
                        }
                        else if (dist > 0 && TryGetRecvBaseline(stream, (ushort)(data.seq - dist), lastNid,
                                     out var baseline, out var baseVel, out gen, out var baselineTick) &&
                                 (short)(data.tick - baselineTick) >= 1)
                        {
                            int tickDist = (short)(data.tick - baselineTick);
                            var predicted = NTUnreliable.GetDeltaPrediction(baseline, baseVel, tickDist);
                            state = nt.ReadDeltaState(packet, baseline, predicted);
                            velocity = NetworkTransformVelocity.Derive(baseline, state, tickDist);
                            ok = true;
                        }
                        else
                        {
                            ok = false;
                        }

                        if (packet.positionInBits > bodyEnd)
                            return;

                        if (ok)
                        {
                            NetworkIdentity frameParent = null;
                            if (state.frame == NetworkTransformFrame.LocalIdentity)
                                _factory.TryGetIdentity(_scene, state.parentId, out frameParent);

                            if (nt.TryApplyUnreliableState(state, gen, packetOrder, data.tick, frameParent, isAbsolute))
                            {
                                decoded.Add(new NTUnreliableEntry
                                {
                                    nid = lastNid,
                                    state = state,
                                    velocity = velocity,
                                    tick = data.tick,
                                    gen = gen
                                });
                                recorded = true;
                            }
                        }
                    }

                    // Anything not recorded must not become an acked baseline on the sender —
                    // acks are packet-granular, so a NACK is the only per-entry signal.
                    if (!recorded)
                        SendNack(sender, lastNid);

                    packet.SetBitPosition(bodyEnd);
                }

                ref var slot = ref stream.ring[data.seq % NTUnreliable.RING_SIZE];
                if (slot.entries != null)
                    ListPool<NTUnreliableEntry>.Destroy(slot.entries);
                slot = new NTUnreliableSlot { used = true, seq = data.seq, entries = decoded };
                decoded = null;
                stream.ackDirty = true;
                stream.packetsSinceAck++;
                committed = true;
            }
            catch (System.Exception exception) when (exception is System.IndexOutOfRangeException or
                                                      System.ArgumentOutOfRangeException or
                                                      System.OverflowException)
            {
                // A short or otherwise malformed packet is equivalent to packet loss. The finally
                // block restores the receive window so a corrupt datagram cannot be acknowledged.
            }
            finally
            {
                if (decoded != null)
                    ListPool<NTUnreliableEntry>.Destroy(decoded);

                if (!committed)
                {
                    stream.ackInit = oldAckInit;
                    stream.latestSeq = oldLatestSeq;
                    stream.latestOrder = oldLatestOrder;
                    stream.ackBits = oldAckBits;
                    stream.ackDirty = oldAckDirty;
                }
            }

            // Only sample the clock offset from packets that decoded cleanly — the
            // rollback above doesn't cover offset state, so a malformed packet must
            // not touch it.
            if (committed)
                UpdateOffsetEstimate(stream, data.tick);

            if (committed && ShouldFlushAckAfterPacket(stream))
                SendStandaloneAck(sender, stream);
        }

        internal static bool ShouldFlushAckAfterPacket(NTUnreliableRecvStream stream)
        {
            return stream.ackDirty && stream.packetsSinceAck >= NTUnreliable.ACK_PACKET_THRESHOLD;
        }

        internal static bool ShouldFlushAckAfterTick(NTUnreliableRecvStream stream)
        {
            if (!stream.ackDirty)
                return false;

            stream.ackDelayTicks++;
            return stream.ackDelayTicks >= NTUnreliable.ACK_INTERVAL_TICKS;
        }

        private void OnUnreliableAck(PlayerID player, NetworkTransformUnreliableAck data, bool asServer)
        {
            if (data.scene != _scene)
                return;

            var key = asServer ? player : PlayerID.Server;
            if (_sendStreams.TryGetValue(key, out var stream))
                ProcessAck(stream, data.seq, data.ackBits);
        }

        internal void ProcessAck(NTUnreliableSendStream stream, ushort seq, uint ackBits)
        {
            using var _ = _processAckMarker.Auto();
            List<int> completed = null;

            try
            {
                TryAdoptAck(stream, seq, ref completed);
                for (int i = 0; i < 32; i++)
                {
                    if ((ackBits & (1u << i)) != 0)
                        TryAdoptAck(stream, (ushort)(seq - 1 - i), ref completed);
                }

                if (completed != null)
                    RemovePendingCompleted(stream, completed);
            }
            finally
            {
                if (completed != null)
                    ListPool<int>.Destroy(completed);
            }
        }

        private static void RemovePendingCompleted(NTUnreliableSendStream stream, List<int> completed)
        {
            if (completed.Count == 0)
                return;

            var flags = stream.ackCompleted;
            for (int i = 0; i < completed.Count; i++)
                flags[completed[i]] = true;

            var pending = stream.pending;
            int writeIndex = 0;
            for (int readIndex = 0; readIndex < pending.Count; readIndex++)
            {
                var nt = pending[readIndex];
                int index = nt is null ? -1 : nt.GetNTIndex(stream.asServer);
                if (index >= 0 && index < flags.Length && flags[index])
                {
                    stream.pendingByIndex[index] = false;
                    continue;
                }

                pending[writeIndex++] = nt;
            }

            if (writeIndex < pending.Count)
                pending.RemoveRange(writeIndex, pending.Count - writeIndex);

            for (int i = 0; i < completed.Count; i++)
                flags[completed[i]] = false;
        }

        private void TryAdoptAck(NTUnreliableSendStream stream, ushort seq, ref List<int> completed)
        {
            ref var slot = ref stream.ring[seq % NTUnreliable.RING_SIZE];
            if (!slot.used || slot.seq != seq || slot.acked)
                return;

            slot.acked = true;

            var list = slot.entries;
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];

                var nt = entry.transform;
                if (!IsLive(nt) || !nt.ntNid.Equals(entry.nid))
                {
                    if (!TryGetRegisteredTransform(entry.nid, out nt))
                        continue;
                }

                // ACK slots are processed newest-first. Once a newer state for this transform was
                // adopted, older retransmissions cannot improve its baseline or complete a newer
                // revision, so skip the registration/generation work entirely.
                int index = nt.GetNTIndex(stream.asServer);
                ref var currentBaseline = ref stream.baselines[index];
                if (currentBaseline.has && slot.order <= currentBaseline.order)
                    continue;

                uint expectedEpoch = stream.generationOverrides.Count > 0 &&
                                     stream.generationOverrides.TryGetValue(entry.nid, out var generation)
                    ? generation.epoch
                    : nt.sendGenEpoch;

                bool restSettled = !nt.hasSyncStrategy ||
                                   stream.adaptiveByIndex[index] is not { } lastWrite ||
                                   (lastWrite.restConfirmed && lastWrite.redundancy == 0);

                bool completes = entry.genEpoch == expectedEpoch && entry.revision == nt.capturedRevision &&
                                 restSettled;

                if (currentBaseline.has && !slot.anchor && !completes)
                    continue;

                if (stream.nackFloor.Count > 0 && stream.nackFloor.TryGetValue(entry.nid, out var floor))
                {
                    if (slot.order < floor)
                        continue;
                    stream.nackFloor.Remove(entry.nid);
                }

                currentBaseline.has = true;
                currentBaseline.state = entry.state;
                currentBaseline.velocity = entry.velocity;
                currentBaseline.tick = entry.tick;
                currentBaseline.gen = entry.gen;
                currentBaseline.genEpoch = entry.genEpoch;
                currentBaseline.revision = entry.revision;
                currentBaseline.order = slot.order;

                if (completes)
                {
                    completed ??= ListPool<int>.Instantiate();
                    completed.Add(index);
                }
            }

            // Once this packet is acknowledged, every state needed for future deltas lives
            // in stream.baselines. Retaining the packet's full snapshots until ring wrap makes
            // clean-link memory scale with 64 packets instead of the actual in-flight window.
            ListPool<NTUnreliableEntry>.Destroy(slot.entries);
            slot.entries = null;
        }

        private void OnUnreliableNack(PlayerID player, NetworkTransformUnreliableNack data, bool asServer)
        {
            if (data.scene != _scene)
                return;

            var key = asServer ? player : PlayerID.Server;
            if (_sendStreams.TryGetValue(key, out var stream))
            {
                ClearBaseline(stream, data.id);
                ClearAdaptive(stream, data.id);
                stream.nackFloor[data.id] = stream.nextOrder;

                if (stream.pendingInitialized && TryGetRegisteredTransform(data.id, out var nt))
                {
                    var localPlayer = GetLocalPlayer();
                    if (IsSendCandidate(nt, key, localPlayer))
                        AddPending(stream, nt);
                }
            }
        }

        private void SendNack(PlayerID sender, NetworkID nid)
        {
            // Reliable: the NACK is the only per-entry correction against packet-granular acks;
            // losing it while the ack lands wedges a resting object on a phantom baseline.
            var nack = new NetworkTransformUnreliableNack { scene = _scene, id = nid };
            _broadcaster.Send(sender, nack, Channel.ReliableUnordered);
        }

        private void SendStandaloneAck(PlayerID sender, NTUnreliableRecvStream stream)
        {
            using var _ = _flushAckMarker.Auto();

            stream.ackDirty = false;
            stream.ackDelayTicks = 0;
            stream.packetsSinceAck = 0;

            var ack = new NetworkTransformUnreliableAck
            {
                scene = _scene,
                seq = stream.latestSeq,
                ackBits = stream.ackBits
            };
            _broadcaster.Send(sender, ack, Channel.Unreliable);
        }

        private void FlushAcks()
        {
            foreach (var (sender, stream) in _recvStreams)
            {
                if (!stream.ackDirty)
                    continue;

                if (!ShouldFlushAckAfterTick(stream))
                    continue;

                SendStandaloneAck(sender, stream);
            }
        }

        private PlayerID GetLocalPlayer()
        {
            if (_manager.TryGetModule<PlayersManager>(false, out var _players))
                return _players.localPlayerId.GetValueOrDefault();
            return PlayerID.Server;
        }

        private bool IsSendCandidate(NetworkTransform nt, PlayerID player, PlayerID localPlayer)
        {
            if (!nt || !nt.IsSpawned(_asServer) || !nt.id.HasValue)
                return false;

            if (player == PlayerID.Server)
                return nt.IsControlling(localPlayer, false);

            return !nt.IsControlling(player, false) && nt.IsObserver(player);
        }

        private NTUnreliableSendStream PrepareSendStream(PlayerID player, PlayerID localPlayer)
        {
            var stream = GetSendStream(player);
            if (stream.pendingInitialized)
                return stream;

            stream.pendingInitialized = true;

            for (int i = 0; i < _networkTransforms.Count; i++)
            {
                var nt = _networkTransforms[i];
                if (IsSendCandidate(nt, player, localPlayer))
                    AddPending(stream, nt);
            }

            return stream;
        }

        private void QueueChangedStates(List<NetworkTransform> changed, PlayerID localPlayer)
        {
            using var _ = _queueChangedMarker.Auto();

            foreach (var (player, stream) in _sendStreams)
            {
                if (!stream.pendingInitialized)
                    continue;

                // Binary insertion is cheaper for tiny changesets. Larger changesets are
                // already NetworkID-sorted by the gather loop, so merge them in linear time.
                if (changed.Count <= 4)
                {
                    for (int i = 0; i < changed.Count; i++)
                    {
                        var nt = changed[i];
                        if (IsSendCandidate(nt, player, localPlayer))
                            AddPending(stream, nt);
                    }
                    continue;
                }

                // A continuously moving transform normally remains pending while its previous
                // revision is in flight. In that common benchmark/gameplay case every changed ID
                // is already present, so avoid allocating and copying the same full list again.
                if (!HasMissingSendCandidate(stream, changed, player, localPlayer))
                    continue;

                var additions = ListPool<NetworkTransform>.Instantiate();
                try
                {
                    for (int i = 0; i < changed.Count; i++)
                    {
                        var nt = changed[i];
                        if (IsSendCandidate(nt, player, localPlayer))
                            additions.Add(nt);
                    }

                    MergePending(stream, additions);
                }
                finally
                {
                    ListPool<NetworkTransform>.Destroy(additions);
                }
            }
        }

        private bool HasMissingSendCandidate(NTUnreliableSendStream stream, List<NetworkTransform> changed,
            PlayerID player, PlayerID localPlayer)
        {
            for (int changedIndex = 0; changedIndex < changed.Count; changedIndex++)
            {
                var nt = changed[changedIndex];
                if (!stream.IsPending(nt) && IsSendCandidate(nt, player, localPlayer))
                    return true;
            }

            return false;
        }

        internal static void MergePending(NTUnreliableSendStream stream, List<NetworkTransform> additions)
        {
            if (additions.Count == 0)
                return;

            for (int i = 0; i < additions.Count; i++)
                stream.SetPending(additions[i], true);

            if (stream.pending.Count == 0)
            {
                stream.pending.AddRange(additions);
                return;
            }

            var merged = ListPool<NetworkTransform>.Instantiate();
            try
            {
                int pendingIndex = 0;
                int additionIndex = 0;

                while (pendingIndex < stream.pending.Count && additionIndex < additions.Count)
                {
                    var pending = stream.pending[pendingIndex];
                    var addition = additions[additionIndex];
                    var pendingId = pending.id!.Value;
                    var additionId = addition.id!.Value;

                    if (pendingId.Equals(additionId))
                    {
                        merged.Add(addition);
                        pendingIndex++;
                        additionIndex++;
                    }
                    else if (pendingId < additionId)
                    {
                        merged.Add(pending);
                        pendingIndex++;
                    }
                    else
                    {
                        merged.Add(addition);
                        additionIndex++;
                    }
                }

                while (pendingIndex < stream.pending.Count)
                    merged.Add(stream.pending[pendingIndex++]);
                while (additionIndex < additions.Count)
                    merged.Add(additions[additionIndex++]);

                stream.pending.Clear();
                stream.pending.AddRange(merged);
            }
            finally
            {
                ListPool<NetworkTransform>.Destroy(merged);
            }
        }

        // Worst-case bits for one entry's framing (len delta + nid delta).
        private const int ENTRY_HEADER_BITS = 128;
        // Wrapper overhead: broadcast type hash + scene + seq + ByteData length prefix.
        private const int UNRELIABLE_PACKET_OVERHEAD = 32;

        internal static long CalculateBudgetBits(int transportMtu)
        {
            long payloadBytes = (long)transportMtu - UNRELIABLE_PACKET_OVERHEAD;
            if (payloadBytes < 128)
                payloadBytes = 128;
            return payloadBytes * 8L;
        }

        private struct NTEntryPlan
        {
            public bool absolute;
            public bool sameDist;
            public int dist;
            public byte gen;
            public BitPacker stateBits;
            public int bits;
        }

        private static void WriteEntry(BitPacker packer, in NTEntryPlan plan)
        {
            if (plan.absolute)
            {
                packer.WriteBits(1, 1);
                Packer<byte>.Write(packer, plan.gen);
            }
            else
            {
                packer.WriteBits(0, 1);

                if (plan.sameDist)
                {
                    packer.WriteBits(1, 1);
                }
                else
                {
                    packer.WriteBits(0, 1);
                    packer.WriteBits((ulong)(plan.dist - 1), NTUnreliable.DISTANCE_BITS);
                }
            }

            packer.WriteBitsWithoutConsumingIt(plan.stateBits, plan.stateBits.positionInBits);
        }

        private static NTWriteResult TryWriteEntry(NetworkTransform nt, NTUnreliableSendStream stream,
            ushort currentTick, int lastDist, out NTEntryPlan plan, out int newLastDist,
            out NetworkTransformVelocity velocity, out byte gen, out uint genEpoch)
        {
            plan = default;
            newLastDist = lastDist;
            velocity = default;
            var nid = nt.ntNid;
            var generation = stream.generationOverrides.Count > 0 &&
                             stream.generationOverrides.TryGetValue(nid, out var overridden)
                ? overridden
                : new NTUnreliableGeneration { gen = nt.sendGen, epoch = nt.sendGenEpoch };
            gen = generation.gen;
            genEpoch = generation.epoch;

            ref var baseline = ref stream.baselines[nt.GetNTIndex(stream.asServer)];
            bool hasAcked = baseline.has && baseline.genEpoch == genEpoch;

            ref readonly var current = ref nt.capturedState;
            NTLastAdaptiveWrite lastWrite = null;
            bool hasLastWrite = nt.hasSyncStrategy && (lastWrite = stream.GetAdaptive(nt)) != null;

            // Suppression must not depend on baseline age — a resting object's baseline never
            // refreshes, and re-sending absolutes for it every 32 packets floods static scenes.
            if (hasAcked && baseline.revision == nt.capturedRevision &&
                (!nt.hasSyncStrategy || !hasLastWrite ||
                 (lastWrite.restConfirmed && lastWrite.redundancy == 0)))
                return NTWriteResult.SkipAcked;

            if (nt.hasSyncStrategy)
            {
                bool breakWrite = false;
                bool brokeEarly = false;
                bool scheduledProbe = false;
                byte breakRedundancy = nt.activeStrategy?.breakRedundancy ?? NTUnreliable.BREAK_REDUNDANCY;

                if (hasLastWrite)
                {
                    int sinceLastWrite = (short)(currentTick - lastWrite.tick);
                    bool resting = lastWrite.revision == nt.capturedRevision;
                    bool restGate = !resting || lastWrite.restConfirmed;

                    if (sinceLastWrite >= 1 && restGate)
                    {
                        int spacing = nt.adaptiveSendSpacing;
                        int refreshInterval = lastWrite.refreshInterval >= 2 && lastWrite.refreshInterval < spacing
                            ? lastWrite.refreshInterval
                            : spacing;

                        bool canSkip = sinceLastWrite < spacing &&
                                       nt.CanSkipCached(lastWrite, currentTick, current);
                        bool refreshDue = ((uint)currentTick + (uint)nid.GetHashCode()) %
                            (uint)refreshInterval == 0;

                        if (canSkip && !refreshDue && (lastWrite.redundancy == 0 ||
                                                       sinceLastWrite < NTUnreliable.REDUNDANCY_INTERVAL))
                            return NTWriteResult.Hold;

                        breakWrite = !canSkip && lastWrite.redundancy == 0 &&
                                     sinceLastWrite < spacing &&
                                     sinceLastWrite > breakRedundancy;

                        brokeEarly = !canSkip && sinceLastWrite < spacing;
                        scheduledProbe = canSkip && refreshDue;
                    }
                }

                if (!hasLastWrite)
                {
                    lastWrite = new NTLastAdaptiveWrite
                    {
                        tick = currentTick,
                        state = current,
                        revision = nt.capturedRevision
                    };
                    stream.SetAdaptive(nt, lastWrite);
                }
                else if (lastWrite.tick != currentTick)
                {
                    bool sameAsPrev = lastWrite.revision == nt.capturedRevision;
                    bool restBreak = sameAsPrev && !lastWrite.restConfirmed;
                    int spacing = nt.adaptiveSendSpacing;

                    if (brokeEarly)
                    {
                        int observed = (short)(currentTick - lastWrite.tick) - 1;
                        lastWrite.refreshInterval = (byte)(observed < 2 ? 2 : observed > spacing ? spacing : observed);
                    }
                    else if (scheduledProbe && lastWrite.refreshInterval >= 2 && lastWrite.refreshInterval < spacing)
                    {
                        lastWrite.refreshInterval++;
                    }

                    lastWrite.prevPrevTick = lastWrite.prevTick;
                    lastWrite.prevPrevState = lastWrite.prevState;
                    lastWrite.hasPrevPrev = lastWrite.hasPrev;

                    lastWrite.prevTick = lastWrite.tick;
                    lastWrite.prevState = lastWrite.state;
                    lastWrite.hasPrev = true;

                    lastWrite.tick = currentTick;
                    lastWrite.state = current;
                    lastWrite.revision = nt.capturedRevision;
                    lastWrite.restConfirmed = sameAsPrev;
                    lastWrite.redundancy = breakWrite || restBreak
                        ? breakRedundancy
                        : (byte)(lastWrite.redundancy > 0 ? lastWrite.redundancy - 1 : 0);
                }
            }

            int dist = hasAcked ? (int)(stream.nextOrder - baseline.order) : 0;
            int tickDist = hasAcked ? (short)(currentTick - baseline.tick) : 0;
            bool canDelta = hasAcked && dist >= 1 && dist <= NTUnreliable.MAX_BASELINE_AGE && tickDist >= 1 &&
                            nt.CanDeltaAgainst(baseline.state);

            var cache = nt.unreliableEncodeCache ??= new NTEncodeCache();
            cache.BeginTick(currentTick);

            if (canDelta)
            {
                gen = baseline.gen;
                genEpoch = baseline.genEpoch;
                plan.sameDist = dist == lastDist;
                plan.dist = dist;

                if (!plan.sameDist)
                    newLastDist = dist;

                if (!cache.TryGetDelta(baseline.tick, baseline.velocity, out var stateBits, out velocity))
                {
                    var predicted = NTUnreliable.GetDeltaPrediction(baseline.state, baseline.velocity, tickDist);
                    stateBits = cache.ClaimDeltaSlot(baseline.tick, baseline.velocity);
                    nt.WriteDeltaState(stateBits, baseline.state, predicted);
                    velocity = NetworkTransformVelocity.Derive(baseline.state, current, tickDist);
                    cache.CompleteDeltaSlot(velocity);
                }

                plan.stateBits = stateBits;
                plan.bits = 2 + (plan.sameDist ? 0 : NTUnreliable.DISTANCE_BITS) + stateBits.positionInBits;
            }
            else
            {
                plan.absolute = true;
                plan.gen = gen;
                var stateBits = cache.GetAbsolute(nt);
                plan.stateBits = stateBits;
                plan.bits = 9 + stateBits.positionInBits;

                if (nt.hasSyncStrategy && lastWrite != null)
                {
                    lastWrite.tick = currentTick;
                    lastWrite.state = current;
                    lastWrite.revision = nt.capturedRevision;
                    lastWrite.hasPrev = false;
                    lastWrite.hasPrevPrev = false;
                    lastWrite.restConfirmed = true;
                    lastWrite.redundancy = 0;
                    lastWrite.refreshInterval = 0;
                }
            }

            return NTWriteResult.Written;
        }

        private void FlushUnreliablePacket(PlayerID player, NTUnreliableSendStream stream, BitPacker packer,
            List<NTUnreliableEntry> pending, int countPos, int writtenCount, NTShareGroup record)
        {
            var lastPos = packer.positionInBits;
            packer.SetBitPosition(countPos);
            Packer<int>.Write(packer, writtenCount);
            packer.SetBitPosition(lastPos);

            CommitPacket(player, stream, packer, pending);

            if (record != null)
            {
                record.packets.Add(packer);
                record.entries.Add(pending);
            }
            else
            {
                packer.Dispose();
            }
        }

        private void CommitPacket(PlayerID player, NTUnreliableSendStream stream, BitPacker packer,
            List<NTUnreliableEntry> pending)
        {
            using var _ = _commitMarker.Auto();
            ushort seq = stream.nextSeq;
            ref var slot = ref stream.ring[seq % NTUnreliable.RING_SIZE];
            if (slot.entries != null)
                ListPool<NTUnreliableEntry>.Destroy(slot.entries);
            slot = new NTUnreliableSlot
            {
                used = true,
                anchor = NTUnreliable.IsAnchorTick(_currentTick),
                seq = seq,
                order = stream.nextOrder,
                entries = pending
            };
            stream.nextSeq += 1;
            stream.nextOrder += 1;

            var delta = new NetworkTransformUnreliableDelta(_scene, seq, _currentTick, packer);

            if (_recvStreams.TryGetValue(player, out var recv) && recv.ackInit && recv.ackDirty)
            {
                recv.ackDirty = false;
                recv.ackDelayTicks = 0;
                recv.packetsSinceAck = 0;
                delta.ack = new NetworkTransformUnreliableAckHeader
                {
                    seq = recv.latestSeq,
                    ackBits = recv.ackBits
                };
            }

            _broadcaster.Send(player, delta, Channel.Unreliable);
        }

        private struct NTShareKey
        {
            public NetworkID nid;
            public ushort tick;
            public int dist;
            public byte gen;
            public byte mode;
            public uint genEpoch;
            public NetworkTransformVelocity velocity;
            public byte writeFlags;
            public byte writeRefresh;
            public byte writeRedundancy;
            public ushort writeTick;
            public ushort writePrevTick;
            public ushort writePrevPrevTick;
            public uint writeRevision;
        }

        private struct NTAdaptiveSnapshot
        {
            public int index;
            public ushort tick;
            public ushort prevTick;
            public ushort prevPrevTick;
            public uint revision;
            public NetworkTransformState state;
            public NetworkTransformState prevState;
            public NetworkTransformState prevPrevState;
            public byte refreshInterval;
            public byte redundancy;
            public bool hasPrev;
            public bool hasPrevPrev;
            public bool restConfirmed;

            public static NTAdaptiveSnapshot From(int index, NTLastAdaptiveWrite w) => new()
            {
                index = index,
                tick = w.tick,
                prevTick = w.prevTick,
                prevPrevTick = w.prevPrevTick,
                revision = w.revision,
                state = w.state,
                prevState = w.prevState,
                prevPrevState = w.prevPrevState,
                refreshInterval = w.refreshInterval,
                redundancy = w.redundancy,
                hasPrev = w.hasPrev,
                hasPrevPrev = w.hasPrevPrev,
                restConfirmed = w.restConfirmed
            };

            public void ApplyTo(NTLastAdaptiveWrite w)
            {
                w.tick = tick;
                w.prevTick = prevTick;
                w.prevPrevTick = prevPrevTick;
                w.revision = revision;
                w.state = state;
                w.prevState = prevState;
                w.prevPrevState = prevPrevState;
                w.refreshInterval = refreshInterval;
                w.redundancy = redundancy;
                w.hasPrev = hasPrev;
                w.hasPrevPrev = hasPrevPrev;
                w.restConfirmed = restConfirmed;
            }
        }

        private sealed class NTShareGroup
        {
            public ulong hash;
            public long budgetBits;
            public int keyCount;
            public NTShareKey[] keys = new NTShareKey[64];
            public readonly List<BitPacker> packets = new();
            public readonly List<List<NTUnreliableEntry>> entries = new();
            public readonly List<NTAdaptiveSnapshot> adaptive = new();
        }

        private readonly List<NTShareGroup> _shareGroups = new();
        private int _shareGroupCount;
        private NTShareKey[] _shareKeys = new NTShareKey[64];

        private static ulong MixKey(ulong hash, in NTShareKey key)
        {
            const ulong prime = 1099511628211UL;
            var v = key.velocity;
            hash = (hash ^ key.nid.id.value) * prime;
            hash = (hash ^ key.nid.scope.id.value) * prime;
            hash = (hash ^ (key.tick | ((ulong)(uint)key.dist << 16) | ((ulong)key.gen << 48) | ((ulong)key.mode << 56))) * prime;
            hash = (hash ^ key.genEpoch) * prime;
            hash = (hash ^ ((uint)v.posX | ((ulong)(uint)v.posY << 32))) * prime;
            hash = (hash ^ ((uint)v.posZ | ((ulong)(uint)v.scaleX << 32))) * prime;
            hash = (hash ^ ((uint)v.scaleY | ((ulong)(uint)v.scaleZ << 32))) * prime;
            hash = (hash ^ ((ushort)v.rotX | ((ulong)(ushort)v.rotY << 16) | ((ulong)(ushort)v.rotZ << 32) |
                            ((ulong)(ushort)v.rotW << 48))) * prime;
            hash = (hash ^ (key.writeTick | ((ulong)key.writePrevTick << 16) | ((ulong)key.writePrevPrevTick << 32) |
                            ((ulong)key.writeFlags << 48) | ((ulong)key.writeRefresh << 56))) * prime;
            hash = (hash ^ (key.writeRevision | ((ulong)key.writeRedundancy << 32))) * prime;
            return hash;
        }

        private static bool KeyEquals(in NTShareKey a, in NTShareKey b)
        {
            return a.mode == b.mode && a.tick == b.tick && a.dist == b.dist && a.gen == b.gen &&
                   a.genEpoch == b.genEpoch && a.nid.Equals(b.nid) &&
                   a.velocity.posX == b.velocity.posX && a.velocity.posY == b.velocity.posY &&
                   a.velocity.posZ == b.velocity.posZ && a.velocity.rotX == b.velocity.rotX &&
                   a.velocity.rotY == b.velocity.rotY && a.velocity.rotZ == b.velocity.rotZ &&
                   a.velocity.rotW == b.velocity.rotW && a.velocity.scaleX == b.velocity.scaleX &&
                   a.velocity.scaleY == b.velocity.scaleY && a.velocity.scaleZ == b.velocity.scaleZ &&
                   a.writeFlags == b.writeFlags && a.writeTick == b.writeTick &&
                   a.writePrevTick == b.writePrevTick && a.writePrevPrevTick == b.writePrevPrevTick &&
                   a.writeRevision == b.writeRevision && a.writeRefresh == b.writeRefresh &&
                   a.writeRedundancy == b.writeRedundancy;
        }

        private bool TryBuildShareKeys(NTUnreliableSendStream stream, ushort currentTick, out int keyCount,
            out ulong hash)
        {
            keyCount = 0;
            hash = 14695981039346656037UL;

            if (stream.generationOverrides.Count > 0)
                return false;

            var pending = stream.pending;
            if (_shareKeys.Length < pending.Count)
                System.Array.Resize(ref _shareKeys, System.Math.Max(pending.Count, _shareKeys.Length * 2));

            for (int i = 0; i < pending.Count;)
            {
                var nt = pending[i];
                if (!IsLive(nt))
                {
                    pending.RemoveAt(i);
                    continue;
                }

                if (nt.adaptiveDebugDumpEnabled)
                    return false;

                var nid = nt.ntNid;
                ref var baseline = ref stream.baselines[nt.GetNTIndex(stream.asServer)];
                bool hasAcked = baseline.has && baseline.genEpoch == nt.sendGenEpoch;

                NTLastAdaptiveWrite lastWrite = null;
                bool hasLastWrite = nt.hasSyncStrategy && (lastWrite = stream.GetAdaptive(nt)) != null;

                if (hasAcked && baseline.revision == nt.capturedRevision &&
                    (!nt.hasSyncStrategy || !hasLastWrite ||
                     (lastWrite.restConfirmed && lastWrite.redundancy == 0)))
                {
                    pending.RemoveAt(i);
                    stream.SetPending(nt, false);
                    continue;
                }

                int dist = hasAcked ? (int)(stream.nextOrder - baseline.order) : 0;
                int tickDist = hasAcked ? (short)(currentTick - baseline.tick) : 0;
                bool canDelta = hasAcked && dist >= 1 && dist <= NTUnreliable.MAX_BASELINE_AGE && tickDist >= 1 &&
                                nt.CanDeltaAgainst(baseline.state);

                ref var key = ref _shareKeys[keyCount++];
                key.nid = nid;

                if (canDelta)
                {
                    key.mode = 1;
                    key.tick = baseline.tick;
                    key.dist = dist;
                    key.gen = baseline.gen;
                    key.genEpoch = baseline.genEpoch;
                    key.velocity = baseline.velocity;
                }
                else
                {
                    key.mode = 2;
                    key.tick = 0;
                    key.dist = 0;
                    key.gen = nt.sendGen;
                    key.genEpoch = nt.sendGenEpoch;
                    key.velocity = default;
                }

                if (hasLastWrite)
                {
                    key.writeFlags = (byte)(1 | (lastWrite.hasPrev ? 2 : 0) | (lastWrite.hasPrevPrev ? 4 : 0) |
                                            (lastWrite.restConfirmed ? 8 : 0));
                    key.writeTick = lastWrite.tick;
                    key.writePrevTick = lastWrite.prevTick;
                    key.writePrevPrevTick = lastWrite.prevPrevTick;
                    key.writeRevision = lastWrite.revision;
                    key.writeRefresh = lastWrite.refreshInterval;
                    key.writeRedundancy = lastWrite.redundancy;
                }
                else
                {
                    key.writeFlags = 0;
                    key.writeTick = 0;
                    key.writePrevTick = 0;
                    key.writePrevPrevTick = 0;
                    key.writeRevision = 0;
                    key.writeRefresh = 0;
                    key.writeRedundancy = 0;
                }

                hash = MixKey(hash, key);
                i++;
            }

            return true;
        }

        private NTShareGroup FindShareGroup(ulong hash, int keyCount, long budgetBits)
        {
            for (int g = 0; g < _shareGroupCount; g++)
            {
                var group = _shareGroups[g];
                if (group.hash != hash || group.keyCount != keyCount || group.budgetBits != budgetBits)
                    continue;

                bool equal = true;
                for (int i = 0; i < keyCount && equal; i++)
                    equal = KeyEquals(group.keys[i], _shareKeys[i]);

                if (equal)
                    return group;
            }

            return null;
        }

        private NTShareGroup AcquireShareGroup(ulong hash, int keyCount, long budgetBits)
        {
            if (_shareGroupCount == _shareGroups.Count)
                _shareGroups.Add(new NTShareGroup());

            var group = _shareGroups[_shareGroupCount++];
            group.hash = hash;
            group.keyCount = keyCount;
            group.budgetBits = budgetBits;

            if (group.keys.Length < keyCount)
                group.keys = new NTShareKey[System.Math.Max(keyCount, group.keys.Length * 2)];

            System.Array.Copy(_shareKeys, group.keys, keyCount);
            return group;
        }

        private void ReleaseShareGroups()
        {
            for (int g = 0; g < _shareGroupCount; g++)
            {
                var group = _shareGroups[g];
                for (int p = 0; p < group.packets.Count; p++)
                    group.packets[p].Dispose();
                group.packets.Clear();
                group.entries.Clear();
                group.adaptive.Clear();
            }

            _shareGroupCount = 0;
        }

        private void ReplayShareGroup(PlayerID player, NTUnreliableSendStream stream, NTShareGroup group)
        {
            using var _ = _replayMarker.Auto();

            for (int p = 0; p < group.packets.Count; p++)
            {
                var source = group.entries[p];
                var entries = ListPool<NTUnreliableEntry>.Instantiate();
                entries.AddRange(source);
                entriesWrittenCount += entries.Count;
                sharedPacketCount++;
                CommitPacket(player, stream, group.packets[p], entries);
            }

            var adaptive = stream.adaptiveByIndex;
            for (int a = 0; a < group.adaptive.Count; a++)
            {
                var snapshot = group.adaptive[a];
                if (snapshot.index < 0 || snapshot.index >= adaptive.Length)
                    continue;

                var lastWrite = adaptive[snapshot.index];
                if (lastWrite == null)
                {
                    lastWrite = new NTLastAdaptiveWrite();
                    adaptive[snapshot.index] = lastWrite;
                }

                snapshot.ApplyTo(lastWrite);
            }
        }

        private void SendStatesShared(PlayerID player, PlayerID localPlayer)
        {
            var stream = PrepareSendStream(player, localPlayer);
            if (stream.pending.Count == 0)
                return;

            if (stream.budgetBits == 0)
                stream.budgetBits = CalculateBudgetBits(_manager.GetMTU(player, Channel.Unreliable, _asServer));

            _shareKeysMarker.Begin();
            bool shareable = TryBuildShareKeys(stream, _currentTick, out int keyCount, out ulong hash);
            var group = shareable && keyCount > 0 ? FindShareGroup(hash, keyCount, stream.budgetBits) : null;
            _shareKeysMarker.End();

            if (!shareable)
            {
                if (stream.pending.Count > 0)
                    SendUnreliableStates(player, stream, null);
                return;
            }

            if (keyCount == 0)
                return;

            if (group != null)
            {
                ReplayShareGroup(player, stream, group);
                return;
            }

            group = AcquireShareGroup(hash, keyCount, stream.budgetBits);
            SendUnreliableStates(player, stream, group);
        }

        private void SendUnreliableStates(PlayerID player, NTUnreliableSendStream stream, NTShareGroup record)
        {
            _prepareUnreliableMarker.Begin();

            if (stream.budgetBits == 0)
                stream.budgetBits = CalculateBudgetBits(_manager.GetMTU(player, Channel.Unreliable, _asServer));

            long budgetBits = stream.budgetBits;

            BitPacker packer = null;
            List<NTUnreliableEntry> pending = null;
            int countPos = 0;
            int writtenCount = 0;
            int lastDist = 0;
            NetworkID lastNid = default;
            PackedInt lastLen = default;

            for (var i = 0; i < stream.pending.Count;)
            {
                var nt = stream.pending[i];

                // Observer, ownership, registration, and reset callbacks maintain membership.
                // Keep only a defensive destroyed/unregistered guard in the hot send loop.
                if (!IsLive(nt))
                {
                    stream.pending.RemoveAt(i);
                    continue;
                }

                var nid = nt.ntNid;
                var writeResult = TryWriteEntry(nt, stream, _currentTick, lastDist, out var plan, out var newLastDist,
                    out var velocity, out var wireGen, out var wireGenEpoch);

                if (nt.adaptiveDebugDumpEnabled)
                    nt.DebugDumpLine($"send,tick={_currentTick},to={player},result={writeResult}," +
                                     $"pos={NetworkTransform.DebugPos(nt.capturedState)}");

                if (writeResult == NTWriteResult.SkipAcked)
                {
                    stream.pending.RemoveAt(i);
                    stream.SetPending(nt, false);
                    continue;
                }

                if (writeResult == NTWriteResult.Hold)
                {
                    adaptiveHoldCount++;
                    i++;
                    continue;
                }

                entriesWrittenCount++;

                int entryBits = plan.bits;

                if (writtenCount > 0 && packer!.positionInBits + entryBits + ENTRY_HEADER_BITS > budgetBits)
                {
                    FlushUnreliablePacket(player, stream, packer, pending, countPos, writtenCount, record);
                    packer = null;
                    pending = null;
                    writtenCount = 0;
                    lastDist = 0;
                    lastNid = default;
                    lastLen = default;

                    // the flush advanced nextOrder; baseline distances change, so re-encode
                    if (TryWriteEntry(nt, stream, _currentTick, lastDist, out plan, out newLastDist, out velocity,
                            out wireGen, out wireGenEpoch) != NTWriteResult.Written)
                        continue;
                    entryBits = plan.bits;
                }

                if (packer == null)
                {
                    packer = BitPackerPool.Get();
                    pending = ListPool<NTUnreliableEntry>.Instantiate();
                    countPos = packer.positionInBits;
                    Packer<int>.Write(packer, 0);
                }

                PackedInt length = entryBits;

                DeltaPacker<PackedInt>.Write(packer, lastLen, length);
                lastLen = length;
                DeltaPacker<NetworkID>.Write(packer, lastNid, nid);
                WriteEntry(packer, plan);

                lastNid = nid;
                lastDist = newLastDist;
                writtenCount += 1;
                pending.Add(new NTUnreliableEntry
                {
                    nid = lastNid,
                    state = nt.capturedState,
                    velocity = velocity,
                    tick = _currentTick,
                    gen = wireGen,
                    genEpoch = wireGenEpoch,
                    revision = nt.capturedRevision,
                    transform = nt
                });

                if (record != null && nt.hasSyncStrategy && stream.GetAdaptive(nt) is { } written)
                    record.adaptive.Add(NTAdaptiveSnapshot.From(nt.GetNTIndex(stream.asServer), written));
                i++;
            }

            if (writtenCount > 0)
                FlushUnreliablePacket(player, stream, packer, pending, countPos, writtenCount, record);

            _prepareUnreliableMarker.End();
        }

        private void SendStatesTo(PlayerID player, PlayerID localPlayer)
        {
            var stream = PrepareSendStream(player, localPlayer);
            if (stream.pending.Count > 0)
                SendUnreliableStates(player, stream, null);
        }

        public void Register(NetworkTransform networkTransform)
        {
            if (!networkTransform.id.HasValue)
                return;
            AddTrs(networkTransform);

            int registeredIndex = networkTransform.GetNTIndex(_asServer);
            if (registeredIndex < 0)
            {
                int index;
                if (_freeIndices.Count > 0)
                {
                    index = _freeIndices.Pop();
                    _byIndex[index] = networkTransform;
                }
                else
                {
                    index = _byIndex.Count;
                    _byIndex.Add(networkTransform);
                    foreach (var stream in _sendStreams.Values)
                        stream.EnsureBaselineCapacity(_byIndex.Count);
                }

                networkTransform.SetNTIndex(_asServer, index);
                networkTransform.ntNid = networkTransform.id.Value;
                networkTransform.ntRegistered = true;
            }

            if (_sendStreams.Count == 0)
                return;

            var localPlayer = GetLocalPlayer();
            foreach (var (player, stream) in _sendStreams)
            {
                if (!stream.pendingInitialized)
                    continue;

                if (IsSendCandidate(networkTransform, player, localPlayer))
                    AddPending(stream, networkTransform);
            }
        }

        private void AddTrs(NetworkTransform networkTransform)
        {
            if (_networkTransforms.Contains(networkTransform))
                return;

            for (int i = 0; i < _networkTransforms.Count; i++)
            {
                var networkID = _networkTransforms[i].id;
                if (networkID != null && networkTransform.id != null &&
                    networkID.Value > networkTransform.id.Value)
                {
                    _networkTransforms.Insert(i, networkTransform);
                    return;
                }
            }

            _networkTransforms.Add(networkTransform);
        }

        public void Unregister(NetworkTransform networkTransform)
        {
            _networkTransforms.Remove(networkTransform);

            networkTransform.unreliableEncodeCache?.Dispose();
            networkTransform.unreliableEncodeCache = null;

            int index = networkTransform.GetNTIndex(_asServer);
            if (index >= _byIndex.Count || (index >= 0 && !ReferenceEquals(_byIndex[index], networkTransform)))
                index = -1;
            bool wasRegistered = index >= 0;
            var nid = wasRegistered ? networkTransform.ntNid : networkTransform.id.GetValueOrDefault();

            if (wasRegistered || networkTransform.id.HasValue)
            {
                foreach (var stream in _sendStreams.Values)
                {
                    if (wasRegistered && index < stream.baselines.Length)
                    {
                        stream.baselines[index] = default;
                        stream.adaptiveByIndex[index] = null;
                    }
                    stream.nackFloor.Remove(nid);
                    stream.generationOverrides.Remove(nid);
                    RemovePending(stream, nid);
                    PurgeRing(stream.ring, nid);
                }

                foreach (var stream in _recvStreams.Values)
                    PurgeRing(stream.ring, nid);
            }

            if (wasRegistered)
            {
                _byIndex[index] = null;
                _freeIndices.Push(index);
                networkTransform.SetNTIndex(_asServer, -1);
                networkTransform.ntRegistered = networkTransform.ntIndex >= 0 || networkTransform.ntServerIndex >= 0;
                if (!networkTransform.ntRegistered)
                    networkTransform.ntNid = default;
            }
        }

        private bool TryGetRegisteredTransform(NetworkID nid, out NetworkTransform result)
        {
            int lo = 0;
            int hi = _networkTransforms.Count - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                var nt = _networkTransforms[mid];
                if (!nt || !nt.id.HasValue)
                {
                    result = null;
                    return false;
                }

                var midNid = nt.id.Value;
                if (midNid.Equals(nid))
                {
                    result = nt;
                    return true;
                }

                if (midNid < nid)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            result = null;
            return false;
        }

        internal void InvalidateSendBaseline(PlayerID player, NetworkID nid, bool enqueue = true)
        {
            if (!_sendStreams.TryGetValue(player, out var stream))
                return;

            ClearBaseline(stream, nid);
            ClearAdaptive(stream, nid);
            stream.nackFloor.Remove(nid);
            stream.generationOverrides.Remove(nid);
            PurgeRing(stream.ring, nid);

            if (enqueue && stream.pendingInitialized && TryGetRegisteredTransform(nid, out var nt) &&
                IsSendCandidate(nt, player, GetLocalPlayer()))
                AddPending(stream, nt);
            else
                RemovePending(stream, nid);
        }

        internal void PrepareTargetedReset(PlayerID target, NetworkID nid, byte gen, uint genEpoch)
        {
            foreach (var (player, stream) in _sendStreams)
            {
                if (player == target)
                {
                    InvalidateSendBaseline(target, nid, true);
                    continue;
                }

                if (!stream.generationOverrides.ContainsKey(nid))
                    stream.generationOverrides[nid] = new NTUnreliableGeneration { gen = gen, epoch = genEpoch };
            }
        }

        internal void ClearGenerationOverrides(NetworkID nid)
        {
            bool hasTransform = TryGetRegisteredTransform(nid, out var nt);
            var localPlayer = hasTransform ? GetLocalPlayer() : default;

            foreach (var (player, stream) in _sendStreams)
            {
                stream.generationOverrides.Remove(nid);

                if (!hasTransform || !stream.pendingInitialized)
                    continue;

                if (IsSendCandidate(nt, player, localPlayer))
                    AddPending(stream, nt);
                else
                    RemovePending(stream, nid);
            }
        }

        // A late ack must not resurrect a despawned nid's baseline for a pooled object
        // that respawned with the same NetworkID.
        private static void PurgeRing(NTUnreliableSlot[] ring, NetworkID nid)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                var entries = ring[i].entries;
                if (entries == null)
                    continue;

                for (int e = entries.Count - 1; e >= 0; e--)
                {
                    if (entries[e].nid.Equals(nid))
                        entries.RemoveAt(e);
                }
            }
        }

        private int _vouchBucketTicks;

        private void UpdateOffsetEstimate(NTUnreliableRecvStream stream, ushort senderTick)
        {
            var tickManager = _manager ? _manager.tickModule : null;
            if (tickManager == null)
                return;

            uint localTick = tickManager.localTick;
            ushort off = (ushort)((ushort)localTick - senderTick);

            if (!stream.offsetInit)
            {
                stream.offsetInit = true;
                stream.offsetRef = off;
                stream.devMaxA = 0;
                stream.devMaxB = short.MinValue;
                stream.bucketStart = localTick;
                return;
            }

            short dev = (short)(off - stream.offsetRef);
            if (stream.devMaxA == short.MinValue || dev > stream.devMaxA)
                stream.devMaxA = dev;
        }

        private void UpdateVouchedTick(NTUnreliableRecvStream stream, uint localTick)
        {
            if (!stream.offsetInit)
            {
                stream.vouchedValid = false;
                return;
            }

            if (_vouchBucketTicks == 0)
                _vouchBucketTicks = System.Math.Max(_manager.tickModule.tickRate, 16);

            uint elapsed = localTick - stream.bucketStart;
            if (elapsed >= (uint)(_vouchBucketTicks * 2))
            {
                stream.devMaxA = short.MinValue;
                stream.devMaxB = short.MinValue;
                stream.bucketStart = localTick;
            }
            else if (elapsed >= (uint)_vouchBucketTicks)
            {
                stream.devMaxB = stream.devMaxA;
                stream.devMaxA = short.MinValue;
                stream.bucketStart += (uint)_vouchBucketTicks;
            }

            int devMax = stream.devMaxA == short.MinValue ? int.MinValue : stream.devMaxA;
            if (stream.devMaxB != short.MinValue && stream.devMaxB > devMax)
                devMax = stream.devMaxB;

            if (devMax == int.MinValue)
            {
                if (!stream.hasFrozenDev)
                {
                    stream.vouchedValid = false;
                    return;
                }

                devMax = stream.frozenDevMax;
            }
            else
            {
                stream.frozenDevMax = (short)devMax;
                stream.hasFrozenDev = true;
            }

            stream.vouchedTick = (ushort)((ushort)localTick -
                                          (ushort)(stream.offsetRef + devMax + NTUnreliable.VOUCH_SLACK_TICKS));
            stream.vouchedValid = true;
        }

        private void RenderAdaptiveStates(uint localTick)
        {
            foreach (var stream in _recvStreams.Values)
                UpdateVouchedTick(stream, localTick);

            for (var i = 0; i < _networkTransforms.Count; i++)
            {
                var nt = _networkTransforms[i];
                if (!IsLive(nt))
                    continue;

                ushort vouchedTick = 0;
                bool hasVouched = false;
                var sender = _asServer ? nt.owner.GetValueOrDefault() : PlayerID.Server;

                if (_recvStreams.TryGetValue(sender, out var stream) && stream.vouchedValid)
                {
                    vouchedTick = stream.vouchedTick;
                    hasVouched = true;
                }

                if (!nt.TryTickAdaptiveRender(localTick, vouchedTick, hasVouched, out var state))
                    continue;

                NetworkIdentity frameParent = null;
                if (state.frame == NetworkTransformFrame.LocalIdentity &&
                    (!_factory.TryGetIdentity(_scene, state.parentId, out frameParent) || !frameParent))
                    continue;

                nt.ApplyAdaptiveSample(state, frameParent);
            }
        }

        public void PostFixedUpdate()
        {
            using var _ = _postFixedUpdateMarker.Auto();

            var localPlayer = GetLocalPlayer();
            uint localTick = _manager.tickModule.localTick;
            _currentTick = (ushort)localTick;

            RenderAdaptiveStates(localTick);

            int ntCount = _networkTransforms.Count;
            _changedTransforms.Clear();

            _gatherStateMarker.Begin();
            for (var i = 0; i < ntCount; i++)
            {
                var nt = _networkTransforms[i];
                if (nt.IsControlling(localPlayer, _asServer))
                {
                    uint previousRevision = nt.capturedRevision;
                    nt.GatherState();
                    nt.CaptureUnreliableState(_currentTick);

                    if (nt.capturedRevision != previousRevision && nt.id.HasValue)
                        _changedTransforms.Add(nt);
                }
            }
            _gatherStateMarker.End();

            QueueChangedStates(_changedTransforms, localPlayer);

            if (!_asServer)
            {
                SendStatesTo(PlayerID.Server, localPlayer);
            }
            else if (_scenePlayers.TryGetPlayersInScene(_scene, out var players))
            {
                for (var i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    if (player == localPlayer)
                        continue;

                    SendStatesShared(player, localPlayer);
                }

                ReleaseShareGroups();
            }

            FlushAcks();

            // Preserve the public legacy HasChanges/Delta* contract. This mirrors the old
            // module's save point without participating in the new per-peer baselines.
            for (var i = 0; i < ntCount; i++)
            {
                var nt = _networkTransforms[i];
                if (nt.IsControlling(localPlayer, _asServer))
                    nt.DeltaSave();
            }
        }
    }
}
