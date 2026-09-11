// Data-channel delivery values match LiteNetLib's DeliveryMethod enum.
var PurrRtcLibrary = {
    $PurrRtc: {
        nextId: 1,
        sessions: {},
        setupTimeout: 4000,
        unreliableBufferLimit: 64 * 1024,
        reliableBufferLimit: 1024 * 1024,

        live: function (session) {
            return session.active && PurrRtc.sessions[session.id] === session;
        },

        dispose: function (session) {
            if (!PurrRtc.live(session)) return false;
            session.active = false;
            delete PurrRtc.sessions[session.id];
            clearTimeout(session.timer);
            if (session.abort) session.abort.abort();
            Object.keys(session.channels).forEach(function (key) {
                var channel = session.channels[key];
                channel.onopen = channel.onclose = channel.onerror = channel.onmessage = null;
                try { channel.close(); } catch (_) { }
            });
            if (session.pc) {
                session.pc.onconnectionstatechange = null;
                session.pc.oniceconnectionstatechange = null;
                session.pc.onicegatheringstatechange = null;
                try { session.pc.close(); } catch (_) { }
            }
            return true;
        },

        fail: function (session, message) {
            if (!PurrRtc.live(session)) return;
            var connected = session.connected;
            PurrRtc.dispose(session);
            // Switching protocols after connecting would replay room authentication.
            if (connected) {
                session.error(message || 'WebRTC connection closed.');
                session.closed();
            } else {
                session.fallback();
            }
        },

        receive: function (session, method, event) {
            if (!PurrRtc.live(session) || !session.connected) return;
            if (!(event.data instanceof ArrayBuffer)) {
                PurrRtc.fail(session, 'WebRTC received a non-binary message.');
                return;
            }
            var bytes = new Uint8Array(event.data);
            if (bytes.length > 65535 || bytes.length > session.maxMessageSize + (method === 1 ? 4 : 0)) {
                PurrRtc.fail(session, 'WebRTC message exceeds the transport size limit.');
                return;
            }
            // Relay authentication is the first reliable-ordered response. Other
            // SCTP streams can overtake it, so preserve its position in Unity's queue.
            if (!session.receivedOrdered && method !== 2) {
                if (method === 0) {
                    if (session.earlyReliableBytes + bytes.length > PurrRtc.reliableBufferLimit ||
                        session.earlyReliable.length >= 256) {
                        PurrRtc.fail(session, 'WebRTC received too much data before authentication.');
                        return;
                    }
                    session.earlyReliable.push(bytes);
                    session.earlyReliableBytes += bytes.length;
                }
                return;
            }
            if (method === 1) {
                if (bytes.length < 4) {
                    PurrRtc.fail(session, 'WebRTC received an invalid sequenced message.');
                    return;
                }
                var sequence = new DataView(event.data).getUint32(0, true);
                if (session.receiveSequence !== null) {
                    var distance = (sequence - session.receiveSequence) >>> 0;
                    if (distance === 0 || distance >= 0x80000000) return;
                }
                session.receiveSequence = sequence;
                bytes = bytes.subarray(4);
            }
            session.data(bytes, method);
            if (method === 2 && !session.receivedOrdered) {
                session.receivedOrdered = true;
                var pending = session.earlyReliable;
                session.earlyReliable = [];
                session.earlyReliableBytes = 0;
                pending.forEach(function (packet) {
                    if (PurrRtc.live(session)) session.data(packet, 0);
                });
            }
        },

        createChannel: function (session, method, options) {
            var channel = session.pc.createDataChannel('purr-' + method, options);
            session.channels[method] = channel;
            channel.binaryType = 'arraybuffer';
            channel.onopen = function () {
                if (!PurrRtc.live(session) || session.connected) return;
                var methods = [0, 1, 2, 4];
                if (!methods.every(function (key) {
                    return session.channels[key] && session.channels[key].readyState === 'open';
                })) return;
                session.connected = true;
                clearTimeout(session.timer);
                session.opened();
            };
            channel.onmessage = function (event) { PurrRtc.receive(session, method, event); };
            channel.onclose = function () { PurrRtc.fail(session, 'WebRTC data channel closed.'); };
            channel.onerror = function () { PurrRtc.fail(session, 'WebRTC data channel error.'); };
        },

        negotiate: async function (session, endpoint) {
            if (!PurrRtc.live(session)) return;
            try {
                if (typeof RTCPeerConnection !== 'function' || typeof fetch !== 'function' ||
                    (typeof isSecureContext !== 'undefined' && !isSecureContext)) {
                    PurrRtc.fail(session);
                    return;
                }
                var pc = session.pc = new RTCPeerConnection({ iceServers: [], bundlePolicy: 'max-bundle' });
                var checkConnection = function () {
                    if (pc.connectionState === 'failed' || pc.connectionState === 'closed' ||
                        pc.iceConnectionState === 'failed' || pc.iceConnectionState === 'closed')
                        PurrRtc.fail(session, 'WebRTC connection failed.');
                };
                pc.onconnectionstatechange = checkConnection;
                pc.oniceconnectionstatechange = checkConnection;
                PurrRtc.createChannel(session, 0, { ordered: false });
                PurrRtc.createChannel(session, 1, { ordered: false, maxRetransmits: 0 });
                PurrRtc.createChannel(session, 2, { ordered: true });
                PurrRtc.createChannel(session, 4, { ordered: false, maxRetransmits: 0 });

                var offer = await pc.createOffer();
                if (!PurrRtc.live(session)) return;
                await pc.setLocalDescription(offer);
                if (!PurrRtc.live(session)) return;
                if (pc.iceGatheringState !== 'complete') {
                    await new Promise(function (resolve) {
                        pc.onicegatheringstatechange = function () {
                            if (pc.iceGatheringState === 'complete') {
                                pc.onicegatheringstatechange = null;
                                resolve();
                            }
                        };
                        // Gathering can finish between the check and handler installation.
                        pc.onicegatheringstatechange();
                    });
                }
                if (!PurrRtc.live(session)) return;
                session.abort = typeof AbortController === 'function' ? new AbortController() : null;
                var response = await fetch(endpoint, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ type: 'offer', sdp: pc.localDescription.sdp }),
                    signal: session.abort ? session.abort.signal : undefined,
                    credentials: 'omit',
                    cache: 'no-store'
                });
                if (!PurrRtc.live(session)) return;
                if (!response.ok) throw new Error('WebRTC negotiation failed (' + response.status + ').');
                var answer = await response.json();
                if (!PurrRtc.live(session)) return;
                if (!answer || answer.type !== 'answer' || typeof answer.sdp !== 'string')
                    throw new Error('Invalid WebRTC negotiation response.');
                await pc.setRemoteDescription({ type: 'answer', sdp: answer.sdp });
            } catch (error) {
                PurrRtc.fail(session, error && error.message ? error.message : 'WebRTC negotiation failed.');
            }
        },

        connect: function (endpoint, maxMessageSize, opened, closed, data, error, fallback) {
            var session = {
                id: PurrRtc.nextId++, active: true, connected: false,
                pc: null, abort: null, channels: {},
                maxMessageSize: Math.min(maxMessageSize, 65535),
                sendSequence: 0, receiveSequence: null,
                receivedOrdered: false, earlyReliable: [], earlyReliableBytes: 0,
                opened: opened, closed: closed, data: data, error: error, fallback: fallback
            };
            PurrRtc.sessions[session.id] = session;
            session.timer = setTimeout(function () { PurrRtc.fail(session, 'WebRTC connection timed out.'); }, PurrRtc.setupTimeout);
            // The managed instance must be registered before any callback can run.
            Promise.resolve().then(function () { PurrRtc.negotiate(session, endpoint); });
            return session.id;
        },

        send: function (id, bytes, method) {
            var session = PurrRtc.sessions[id];
            if (!session || !session.connected) return;
            if (method === 3) method = 2;
            var channel = session.channels[method];
            var unreliable = method === 1 || method === 4;
            if (!channel || channel.readyState !== 'open') {
                PurrRtc.fail(session, 'WebRTC delivery channel is unavailable.');
                return;
            }
            var wireLength = bytes.length + (method === 1 ? 4 : 0);
            var remoteLimit = session.pc.sctp && session.pc.sctp.maxMessageSize;
            if (bytes.length > session.maxMessageSize || wireLength > 65535 || (remoteLimit > 0 && wireLength > remoteLimit)) {
                PurrRtc.fail(session, 'WebRTC message exceeds the transport size limit.');
                return;
            }
            if (unreliable && channel.bufferedAmount + wireLength > PurrRtc.unreliableBufferLimit) return;
            if (!unreliable && channel.bufferedAmount + wireLength > PurrRtc.reliableBufferLimit) {
                PurrRtc.fail(session, 'WebRTC reliable send buffer is full.');
                return;
            }
            // WebAssembly memory and pooled C# buffers may change after this call.
            var packet = new Uint8Array(wireLength);
            if (method === 1) {
                new DataView(packet.buffer).setUint32(0, session.sendSequence, true);
                session.sendSequence = (session.sendSequence + 1) >>> 0;
                packet.set(bytes, 4);
            } else {
                packet.set(bytes);
            }
            try {
                channel.send(packet);
            } catch (error) {
                // Unreliable updates have no application-side retry queue.
                if (!unreliable || !error || error.name !== 'OperationError')
                    PurrRtc.fail(session, error && error.message ? error.message : 'WebRTC send failed.');
            }
        }
    },

    PurrRtc_Connect__deps: ['$UTF8ToString', '$stringToNewUTF8', 'malloc', 'free'],
    PurrRtc_Connect: function (endpointPtr, maxMessageSize, opened, closed, data, error, fallback) {
        var id = PurrRtc.connect(UTF8ToString(endpointPtr), maxMessageSize,
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
            function () { {{{ makeDynCall('vi', 'fallback') }}}(id); });
        return id;
    },

    PurrRtc_Disconnect: function (id) {
        var session = PurrRtc.sessions[id];
        if (session && PurrRtc.dispose(session)) session.closed();
    },

    PurrRtc_Send: function (id, pointer, offset, length, method) {
        PurrRtc.send(id, HEAPU8.subarray(pointer + offset, pointer + offset + length), method);
    }
};

autoAddDeps(PurrRtcLibrary, '$PurrRtc');
mergeInto(LibraryManager.library, PurrRtcLibrary);
