using System;
using System.Collections;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;
using Channel = PurrNet.Transports.Channel;
using CompressionLevel = PurrNet.CompressionLevel;

public class IdentityServerRpcs : NetworkIdentity
{
    public static IdentityServerRpcs LocalInstance;
    public static int FireAndForgetReceivedCount;
    public static int DoneCount;
    public static int IEnumeratorRunCount;
    public static int IEnumeratorPayloadReceived;

    [Serializable]
    public struct TestPayload
    {
        public int id;
        public string label;
        public float weight;
    }

    [Serializable]
    public struct SimplePackedPayload : IPackedSimple
    {
        public bool success;
        public int integerVar;
        public float floatVar;

        public void Serialize(BitPacker packer)
        {
            Packer<bool>.Serialize(packer, ref success);
            Packer<int>.Serialize(packer, ref integerVar);
            Packer<float>.Serialize(packer, ref floatVar);
        }
    }

    [Serializable]
    public struct AsyncPayload : IAsyncPackable
    {
        public int seed;
        public int packStamp;
        public int unpackStamp;

        public async ValueTask<IAsyncPackable> PrepareForPackAsync()
        {
            await Task.Yield();
            packStamp = seed + 1;
            return this;
        }

        public async ValueTask<IAsyncPackable> PrepareAfterUnpackAsync()
        {
            await Task.Yield();
            unpackStamp = packStamp + 10;
            return this;
        }
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    [ServerRpc(requireOwnership: false)]
    public Task<int> Echo_Int(int x, RPCInfo info = default) => Task.FromResult(x);

    [ServerRpc(requireOwnership: false)]
    public Task<string> Echo_String(string s, RPCInfo info = default) => Task.FromResult(s);

    [ServerRpc(requireOwnership: false)]
    public Task<bool> Echo_Bool(bool b, RPCInfo info = default) => Task.FromResult(b);

    [ServerRpc(requireOwnership: false)]
    public UniTask<float> Echo_FloatUni(float f, RPCInfo info = default) => UniTask.FromResult(f);

    [ServerRpc(requireOwnership: false)]
    public async UniTask Echo_VoidUni(int dummy, RPCInfo info = default)
    {
        await UniTask.Yield();
    }

    [ServerRpc(requireOwnership: false)]
    public Task<T> Echo_Generic<T>(T value, RPCInfo info = default) => Task.FromResult(value);

    [ServerRpc(requireOwnership: false)]
    public Task<TestPayload> Echo_Struct(TestPayload p, RPCInfo info = default) => Task.FromResult(p);

    [ServerRpc(requireOwnership: false)]
    public Task<SimplePackedPayload> Echo_SimplePacked(int seed, RPCInfo info = default)
    {
        return Task.FromResult(new SimplePackedPayload { success = true, integerVar = seed, floatVar = 42.5f });
    }

    [ServerRpc(requireOwnership: false)]
    public UniTask<SimplePackedPayload> Echo_SimplePackedUni(int seed, RPCInfo info = default)
    {
        return UniTask.FromResult(new SimplePackedPayload { success = true, integerVar = seed, floatVar = 42.5f });
    }

    [ServerRpc(requireOwnership: false)]
    public Task<int> Echo_Sum(int a, int b, int c, RPCInfo info = default) => Task.FromResult(a + b + c);

    [ServerRpc(requireOwnership: false, channel: Channel.Unreliable)]
    public Task<int> Echo_Unreliable(int x, RPCInfo info = default) => Task.FromResult(x);

    [ServerRpc(requireOwnership: false, compressionLevel: CompressionLevel.None)]
    public Task<int> Echo_CompNone(int x, RPCInfo info = default) => Task.FromResult(x);

    [ServerRpc(requireOwnership: false, compressionLevel: CompressionLevel.Fast)]
    public Task<int> Echo_CompFast(int x, RPCInfo info = default) => Task.FromResult(x);

    [ServerRpc(requireOwnership: false, compressionLevel: CompressionLevel.Balanced)]
    public Task<int> Echo_CompBalanced(int x, RPCInfo info = default) => Task.FromResult(x);

    [ServerRpc(requireOwnership: false, compressionLevel: CompressionLevel.Best)]
    public Task<int> Echo_CompBest(int x, RPCInfo info = default) => Task.FromResult(x);

    [ServerRpc(requireOwnership: false)]
    public Task<ulong> Echo_WithInfo(int dummy, RPCInfo info = default)
    {
        return Task.FromResult(info.sender.id.value);
    }

    [ServerRpc(requireOwnership: false, deltaPacked: false)]
    public Task<int> Echo_DeltaOff(int x, RPCInfo info = default) => Task.FromResult(x);

    [ServerRpc(requireOwnership: false, deltaPacked: true)]
    public Task<int> Echo_DeltaOn(int x, RPCInfo info = default) => Task.FromResult(x);

    [ServerRpc(requireOwnership: false)]
    public Task<int[]> Echo_AsyncPackable(AsyncPayload p, RPCInfo info = default)
    {
        return Task.FromResult(new[] { p.seed, p.packStamp, p.unpackStamp });
    }

    [ServerRpc(requireOwnership: false)]
    public IEnumerator Echo_IEnumerator(int payload, RPCInfo info = default)
    {
        yield return null;
        yield return null;
        IEnumeratorPayloadReceived = payload;
        IEnumeratorRunCount++;
    }

    [ServerRpc(requireOwnership: false)]
    public Task<int> Echo_IEnumeratorRunCount(RPCInfo info = default) => Task.FromResult(IEnumeratorRunCount);

    [ServerRpc(requireOwnership: false)]
    public void FireAndForget(int x, RPCInfo info = default)
    {
        FireAndForgetReceivedCount++;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default)
    {
        DoneCount++;
    }
}
