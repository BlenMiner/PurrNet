var PurrRtcPeerLibrary = {
    $PurrRtcPeer__deps: ['$PurrRtc'],
    $PurrRtcPeer: {
        setupTimeout: 8000,
        maxSignalBytes: 32 * 1024,
        maxCandidateBytes: 4096,
        maxCandidates: 64,
        maxSessionSignalBytes: 128 * 1024,

        signalSize: function (json) {
            if (typeof json !== 'string' || json.length > PurrRtcPeer.maxSignalBytes) return -1;
            var size = new TextEncoder().encode(json).length;
            return size <= PurrRtcPeer.maxSignalBytes ? size : -1;
        },

        emit: function (session, signal) {
            if (!PurrRtc.live(session)) return;
            var json = JSON.stringify(signal);
            var size = PurrRtcPeer.signalSize(json);
            if (size < 0 || session.sentSignalBytes + size > PurrRtcPeer.maxSessionSignalBytes)
                throw new Error('WebRTC peer signaling exceeds its size limit.');
            session.sentSignalBytes += size;
            session.signal(json);
        },

        publishDescription: function (session) {
            if (!PurrRtc.live(session)) return;
            PurrRtcPeer.emit(session, {
                type: session.pc.localDescription.type,
                sdp: session.pc.localDescription.sdp
            });
            session.descriptionSent = true;
            var candidates = session.localCandidates;
            session.localCandidates = [];
            candidates.forEach(function (candidate) {
                PurrRtcPeer.emit(session, { type: 'candidate', candidate: candidate });
            });
        },

        start: async function (session, iceServersJson) {
            if (!PurrRtc.live(session)) return;
            if (typeof RTCPeerConnection !== 'function' ||
                (typeof isSecureContext !== 'undefined' && !isSecureContext))
                throw new Error('WebRTC peer connections are unavailable.');
            if (iceServersJson.length > 16 * 1024)
                throw new Error('WebRTC ICE configuration exceeds its size limit.');
            var iceServers = JSON.parse(iceServersJson || '[]');
            if (!Array.isArray(iceServers) || iceServers.length > 8)
                throw new Error('Invalid WebRTC ICE configuration.');
            var pc = session.pc = new RTCPeerConnection({ iceServers: iceServers, bundlePolicy: 'max-bundle' });
            var checkConnection = function () {
                if (pc.connectionState === 'failed' || pc.connectionState === 'closed' ||
                    pc.iceConnectionState === 'failed' || pc.iceConnectionState === 'closed')
                    PurrRtc.fail(session, 'WebRTC peer connection failed.');
            };
            pc.onconnectionstatechange = checkConnection;
            pc.oniceconnectionstatechange = checkConnection;
            pc.onicecandidate = function (event) {
                if (!PurrRtc.live(session) || !event.candidate) return;
                try {
                    var candidate = event.candidate.toJSON();
                    var candidateSize = PurrRtcPeer.signalSize(JSON.stringify({ type: 'candidate', candidate: candidate }));
                    if (++session.localCandidateCount > PurrRtcPeer.maxCandidates ||
                        candidateSize < 0 || candidateSize > PurrRtcPeer.maxCandidateBytes)
                        throw new Error('WebRTC peer has too many ICE candidates.');
                    // The receiving host creates its peer only after the offer arrives.
                    if (session.descriptionSent)
                        PurrRtcPeer.emit(session, { type: 'candidate', candidate: candidate });
                    else
                        session.localCandidates.push(candidate);
                } catch (error) {
                    PurrRtc.fail(session, error.message || 'WebRTC candidate signaling failed.');
                }
            };
            pc.ondatachannel = function (event) {
                if (!PurrRtc.live(session)) {
                    try { event.channel.close(); } catch (_) { }
                    return;
                }
                var channel = event.channel;
                var method = { 'purr-0': 0, 'purr-1': 1, 'purr-2': 2, 'purr-4': 4 }[channel.label];
                var unreliable = method === 1 || method === 4;
                if (session.initiator || method === undefined || session.channels[method] ||
                    channel.ordered !== (method === 2) ||
                    (unreliable ? channel.maxRetransmits !== 0 : channel.maxRetransmits != null) ||
                    channel.maxPacketLifeTime != null) {
                    try { channel.close(); } catch (_) { }
                    PurrRtc.fail(session, 'Unexpected WebRTC peer data channel.');
                    return;
                }
                PurrRtc.bindChannel(session, method, channel);
                if (channel.readyState === 'open') channel.onopen();
            };
            if (session.initiator) {
                PurrRtc.createChannel(session, 0, { ordered: false });
                PurrRtc.createChannel(session, 1, { ordered: false, maxRetransmits: 0 });
                PurrRtc.createChannel(session, 2, { ordered: true });
                PurrRtc.createChannel(session, 4, { ordered: false, maxRetransmits: 0 });
                var offer = await pc.createOffer();
                if (!PurrRtc.live(session)) return;
                await pc.setLocalDescription(offer);
                if (!PurrRtc.live(session)) return;
                PurrRtcPeer.publishDescription(session);
            }
        },

        apply: async function (session, signal) {
            if (!PurrRtc.live(session)) return;
            var pc = session.pc;
            if (signal.type === 'candidate') {
                if (!pc.remoteDescription) {
                    session.remoteCandidates.push(signal.candidate);
                    return;
                }
                await pc.addIceCandidate(signal.candidate);
                return;
            }
            await pc.setRemoteDescription({ type: signal.type, sdp: signal.sdp });
            if (!PurrRtc.live(session)) return;
            var candidates = session.remoteCandidates;
            session.remoteCandidates = [];
            for (var i = 0; i < candidates.length; ++i) {
                await pc.addIceCandidate(candidates[i]);
                if (!PurrRtc.live(session)) return;
            }
            if (!session.initiator) {
                var answer = await pc.createAnswer();
                if (!PurrRtc.live(session)) return;
                await pc.setLocalDescription(answer);
                if (!PurrRtc.live(session)) return;
                PurrRtcPeer.publishDescription(session);
            }
        },

        receiveSignal: function (id, json) {
            var session = PurrRtc.sessions[id];
            if (!session || !session.peer || !PurrRtc.live(session)) return;
            try {
                var size = PurrRtcPeer.signalSize(json);
                if (size < 0 || session.receivedSignalBytes + size > PurrRtcPeer.maxSessionSignalBytes)
                    throw new Error('WebRTC peer signaling exceeds its size limit.');
                var signal = JSON.parse(json);
                if (!signal || typeof signal !== 'object') throw new Error('Invalid WebRTC peer signal.');
                if (signal.type === 'candidate') {
                    if (++session.remoteCandidateCount > PurrRtcPeer.maxCandidates || size > PurrRtcPeer.maxCandidateBytes ||
                        !signal.candidate || typeof signal.candidate.candidate !== 'string' ||
                        signal.candidate.candidate.length === 0)
                        throw new Error('Invalid WebRTC ICE candidate.');
                } else if (signal.type === (session.initiator ? 'answer' : 'offer') &&
                    typeof signal.sdp === 'string' && signal.sdp.length > 0 && !session.descriptionReceived) {
                    session.descriptionReceived = true;
                } else {
                    throw new Error('Unexpected WebRTC peer description.');
                }
                session.receivedSignalBytes += size;
                // Serialize signaling even when ICE callbacks and Unity ticks interleave.
                session.signalWork = session.signalWork.then(function () {
                    return PurrRtcPeer.apply(session, signal);
                }).catch(function (error) {
                    PurrRtc.fail(session, error.message || 'WebRTC peer signaling failed.');
                });
            } catch (error) {
                PurrRtc.fail(session, error.message || 'Invalid WebRTC peer signal.');
            }
        },

        create: function (initiator, iceServersJson, maxMessageSize, opened, closed, data, error, signal) {
            var session = {
                id: PurrRtc.nextId++, active: true, connected: false, peer: true,
                initiator: !!initiator, pc: null, channels: {},
                maxMessageSize: Math.min(maxMessageSize, 65535),
                sendSequence: 0, receiveSequence: null,
                receivedOrdered: true, earlyReliable: [], earlyReliableBytes: 0,
                localCandidates: [], remoteCandidates: [], localCandidateCount: 0, remoteCandidateCount: 0,
                sentSignalBytes: 0, receivedSignalBytes: 0,
                descriptionSent: false, descriptionReceived: false,
                opened: opened, closed: closed, data: data, error: error, signal: signal
            };
            PurrRtc.sessions[session.id] = session;
            session.timer = setTimeout(function () {
                PurrRtc.fail(session, 'WebRTC peer connection timed out.');
            }, PurrRtcPeer.setupTimeout);
            session.signalWork = Promise.resolve().then(function () {
                return PurrRtcPeer.start(session, iceServersJson);
            }).catch(function (error) {
                PurrRtc.fail(session, error.message || 'WebRTC peer setup failed.');
            });
            return session.id;
        }
    },

    PurrRtc_CreatePeer__deps: ['$UTF8ToString', '$stringToNewUTF8', 'malloc', 'free'],
    PurrRtc_CreatePeer: function (initiator, iceServersPtr, maxMessageSize, opened, closed, data, error, signal) {
        var id = PurrRtcPeer.create(initiator, UTF8ToString(iceServersPtr), maxMessageSize,
            function () { {{{ makeDynCall('vi', 'opened') }}}(id); },
            function () { {{{ makeDynCall('vi', 'closed') }}}(id); },
            function (bytes, method) {
                var pointer = _malloc(Math.max(1, bytes.length));
                try {
                    HEAPU8.set(bytes, pointer);
                    {{{ makeDynCall('viiii', 'data') }}}(id, pointer, bytes.length, method);
                } finally { _free(pointer); }
            },
            function (message) {
                var pointer = stringToNewUTF8(message);
                try { {{{ makeDynCall('vii', 'error') }}}(id, pointer); }
                finally { _free(pointer); }
            },
            function (message) {
                var pointer = stringToNewUTF8(message);
                try { {{{ makeDynCall('vii', 'signal') }}}(id, pointer); }
                finally { _free(pointer); }
            });
        return id;
    },

    PurrRtc_ReceiveSignal__deps: ['$UTF8ToString'],
    PurrRtc_ReceiveSignal: function (id, jsonPtr) {
        PurrRtcPeer.receiveSignal(id, UTF8ToString(jsonPtr));
    }
};

autoAddDeps(PurrRtcPeerLibrary, '$PurrRtcPeer');
mergeInto(LibraryManager.library, PurrRtcPeerLibrary);
