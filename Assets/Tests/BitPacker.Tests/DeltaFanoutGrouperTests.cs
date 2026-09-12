using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Pooling;
using UnityEngine;

public class DeltaFanoutGrouperTests
{
    [OneTimeSetUp]
    public void Init()
    {
        NetworkManager.LoadOrGenerateHashes();
    }

    static PlayerID Player(int i) => new PlayerID((ulong)i, false);

    static RPCPacket Packet() => new RPCPacket
    {
        header = new NetworkIdentityRPCHeader { networkId = new NetworkID(42), rpcId = 3 }
    };

    static uint Hash<T>(RPCPacket packet, ulong offset)
    {
        return DeltaModule.GetKeyHash(new NetworkIdentityRpcHash<T, RPCPacket>(packet, offset));
    }

    static (int bits, byte[] bytes) WriteArgs(DeltaModule module, PlayerID player, uint posHash, uint hpHash,
        Vector3 pos, int hp)
    {
        using var packer = BitPackerPool.Get();
        PackedUInt cache = default;
        module.Write(packer, player, posHash, pos, ref cache);
        module.Write(packer, player, hpHash, hp, ref cache);

        int bits = packer.positionInBits;
        var data = packer.ToByteData();
        var bytes = new byte[data.length];
        data.span.CopyTo(bytes);
        int rem = bits & 7;
        if (rem != 0 && bytes.Length > 0)
            bytes[^1] &= (byte)((1 << rem) - 1);
        return (bits, bytes);
    }

    static DisposableList<PlayerID> Players(params int[] ids)
    {
        var list = DisposableList<PlayerID>.Create(ids.Length);
        foreach (var id in ids)
            list.Add(Player(id));
        return list;
    }

    static int GroupOf(DeltaFanoutGrouper grouper, int groupCount, PlayerID player)
    {
        for (int g = 0; g < groupCount; g++)
        {
            if (grouper.GetMembers(g).Contains(player))
                return g;
        }

        return -1;
    }

    [Test]
    public void GroupsByAckedBaseline_AndGroupPayloadMatchesEveryMember()
    {
        var module = new DeltaModule(null, null);
        var packet = Packet();
        uint posHash = Hash<Vector3>(packet, 0);
        uint hpHash = Hash<int>(packet, 1);

        // Two earlier sends give ids 1 and 2 for both keys.
        for (int p = 1; p <= 6; p++)
            WriteArgs(module, Player(p), posHash, hpHash, new Vector3(1, 2, 3), 100);
        for (int p = 1; p <= 6; p++)
            WriteArgs(module, Player(p), posHash, hpHash, new Vector3(2, 2, 3), 90);

        // 1,2 acked (1,1); 3 acked (2,2); 4 acked pos only; 5,6 acked nothing.
        foreach (var p in new[] { 1, 2 })
        {
            module.ConfirmDeliveryForTests<Vector3>(Player(p), posHash, new PackedUInt(1));
            module.ConfirmDeliveryForTests<int>(Player(p), hpHash, new PackedUInt(1));
        }

        module.ConfirmDeliveryForTests<Vector3>(Player(3), posHash, new PackedUInt(2));
        module.ConfirmDeliveryForTests<int>(Player(3), hpHash, new PackedUInt(2));
        module.ConfirmDeliveryForTests<Vector3>(Player(4), posHash, new PackedUInt(1));

        using var players = Players(1, 2, 3, 4, 5, 6);
        var grouper = DeltaFanoutGrouper.Begin(module, packet, false, players);
        grouper.Key(new Vector3(3, 2, 3));
        grouper.Key(80);
        using var reps = grouper.BuildRepresentatives();

        try
        {
            Assert.That(reps.Count, Is.EqualTo(4));
            Assert.That(GroupOf(grouper, reps.Count, Player(1)), Is.EqualTo(GroupOf(grouper, reps.Count, Player(2))));
            Assert.That(GroupOf(grouper, reps.Count, Player(5)), Is.EqualTo(GroupOf(grouper, reps.Count, Player(6))));
            Assert.That(grouper.GetMembers(GroupOf(grouper, reps.Count, Player(3))).Count, Is.EqualTo(1));
            Assert.That(grouper.GetMembers(GroupOf(grouper, reps.Count, Player(4))).Count, Is.EqualTo(1));

            var seen = new HashSet<PlayerID>();
            var payloads = new List<(int bits, byte[] bytes)>();

            for (int g = 0; g < reps.Count; g++)
            {
                var members = grouper.GetMembers(g);
                Assert.That(members[0], Is.EqualTo(reps[g]));

                var expected = WriteArgs(module, reps[g], posHash, hpHash, new Vector3(3, 2, 3), 80);
                payloads.Add(expected);

                foreach (var member in members)
                {
                    Assert.That(seen.Add(member), Is.True, $"{member} appears in two groups");
                    var actual = WriteArgs(module, member, posHash, hpHash, new Vector3(3, 2, 3), 80);
                    Assert.That(actual.bits, Is.EqualTo(expected.bits), $"bit length differs for {member}");
                    Assert.That(actual.bytes, Is.EqualTo(expected.bytes), $"payload differs for {member}");
                }
            }

            Assert.That(seen.Count, Is.EqualTo(6));

            for (int a = 0; a < payloads.Count; a++)
            for (int b = a + 1; b < payloads.Count; b++)
            {
                bool same = payloads[a].bits == payloads[b].bits &&
                            System.Linq.Enumerable.SequenceEqual(payloads[a].bytes, payloads[b].bytes);
                Assert.That(same, Is.False, $"groups {a} and {b} produced identical payloads");
            }
        }
        finally
        {
            grouper.End();
        }
    }

    [Test]
    public void ReliableChannel_KeepsEveryPlayerSeparate()
    {
        var module = new DeltaModule(null, null);
        using var players = Players(1, 2, 3);
        var grouper = DeltaFanoutGrouper.Begin(module, Packet(), true, players);
        grouper.Key(new Vector3(1, 1, 1));
        using var reps = grouper.BuildRepresentatives();
        Assert.That(reps.Count, Is.EqualTo(3));
        grouper.End();
    }

    [Test]
    public void UnsharedArgumentType_KeepsEveryPlayerSeparate()
    {
        var module = new DeltaModule(null, null);
        using var players = Players(1, 2, 3);
        var grouper = DeltaFanoutGrouper.Begin(module, Packet(), false, players);
        grouper.Key(new Vector3(1, 1, 1));
        grouper.Key("text");
        using var reps = grouper.BuildRepresentatives();
        Assert.That(reps.Count, Is.EqualTo(3));
        grouper.End();
    }

    [Test]
    public void ServerId_NeverShares()
    {
        var module = new DeltaModule(null, null);
        using var players = DisposableList<PlayerID>.Create(3);
        players.Add(PlayerID.Server);
        players.Add(Player(1));
        players.Add(Player(2));

        var grouper = DeltaFanoutGrouper.Begin(module, Packet(), false, players);
        grouper.Key(7);
        using var reps = grouper.BuildRepresentatives();

        Assert.That(reps.Count, Is.EqualTo(2));
        Assert.That(grouper.GetMembers(GroupOf(grouper, reps.Count, PlayerID.Server)).Count, Is.EqualTo(1));
        Assert.That(grouper.GetMembers(GroupOf(grouper, reps.Count, Player(1))).Count, Is.EqualTo(2));
        grouper.End();
    }

    [Test]
    public void NoRecipients_ProducesNoGroups()
    {
        var module = new DeltaModule(null, null);
        using var players = DisposableList<PlayerID>.Create(0);
        var grouper = DeltaFanoutGrouper.Begin(module, Packet(), false, players);
        grouper.Key(1);
        using var reps = grouper.BuildRepresentatives();
        Assert.That(reps.Count, Is.EqualTo(0));
        grouper.End();
    }

    [Test]
    public void PooledInstance_IsCleanOnReuse()
    {
        var module = new DeltaModule(null, null);

        using (var players = Players(1, 2))
        {
            var grouper = DeltaFanoutGrouper.Begin(module, Packet(), false, players);
            grouper.Key("unshared");
            using var reps = grouper.BuildRepresentatives();
            Assert.That(reps.Count, Is.EqualTo(2));
            grouper.End();
        }

        using (var players = Players(1, 2))
        {
            var grouper = DeltaFanoutGrouper.Begin(module, Packet(), false, players);
            grouper.Key(5);
            using var reps = grouper.BuildRepresentatives();
            Assert.That(reps.Count, Is.EqualTo(1));
            grouper.End();
        }
    }
}
