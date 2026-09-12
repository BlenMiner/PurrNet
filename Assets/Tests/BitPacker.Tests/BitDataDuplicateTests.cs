using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;

/// <summary>
/// Reproduces the "PurrNet really dropped the ball" spawn OnDeserialize corruption.
///
/// BitData.Duplicate() copies the view via BitPacker.Write(BitData), which prepends a
/// Size length-prefix, but the returned BitData claims bitOrigin 0. Every consumer that
/// reads the duplicate through AutoScope (HierarchyV2.ProcessSpawnWhenLoadedAsync and
/// BeginAsyncRemoteSpawn feeding CompleteSpawnWithInstance) therefore reads the prefix
/// bits as user payload and every value after it is shifted garbage.
/// </summary>
public class BitDataDuplicateTests
{
    private const int Magic = 69;

    [SetUp]
    public void Setup()
    {
        NetworkManager.LoadOrGenerateHashes();
    }

    [Test]
    public void DuplicatePreservesBitLength()
    {
        using var packer = BitPackerPool.Get();
        Packer<int>.Write(packer, Magic);
        var original = new BitData(packer);

        var duplicate = original.Duplicate();
        try
        {
            Assert.That((int)duplicate.bitLength.value, Is.EqualTo((int)original.bitLength.value),
                "Duplicate() changed the payload length; it embedded the Size length-prefix into the view.");
        }
        finally
        {
            duplicate.Dispose();
        }
    }

    [Test]
    public void DuplicateRoundTripsPayload()
    {
        using var packer = BitPackerPool.Get();
        Packer<int>.Write(packer, Magic);
        Packer<Vector3>.Write(packer, new Vector3(1f, 2f, 3f));
        var original = new BitData(packer);

        var duplicate = original.Duplicate();
        try
        {
            using (duplicate.AutoScope())
            {
                int magic = 0;
                Packer<int>.Read(duplicate.packer, ref magic);
                Assert.That(magic, Is.EqualTo(Magic),
                    $"PurrNet really dropped the ball here {magic}.");

                Vector3 position = default;
                Packer<Vector3>.Read(duplicate.packer, ref position);
                Assert.That(position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            }
        }
        finally
        {
            duplicate.Dispose();
        }
    }

    [Test]
    public void DuplicateOfMidBufferViewRoundTripsPayload()
    {
        using var packer = BitPackerPool.Get();
        Packer<string>.Write(packer, "unrelated-leading-data");
        int origin = packer.positionInBits;
        Packer<int>.Write(packer, Magic);
        var original = new BitData(packer, origin, packer.positionInBits - origin);

        var duplicate = original.Duplicate();
        try
        {
            using (duplicate.AutoScope())
            {
                int magic = 0;
                Packer<int>.Read(duplicate.packer, ref magic);
                Assert.That(magic, Is.EqualTo(Magic));
            }
        }
        finally
        {
            duplicate.Dispose();
        }
    }

    /// <summary>
    /// Mirrors the full spawn custom-data pipeline:
    /// server SendSpawnPacket -> wire (Packer&lt;BitData&gt;) -> client deferred spawn
    /// (Duplicate to survive the await) -> CompleteSpawnWithInstance (AutoScope + read).
    /// The synchronous variant (no Duplicate) passes; the deferred variant corrupts.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void SpawnCustomDataPipeline(bool deferredSpawnPath)
    {
        // Server: TriggerOnSerialize into a fresh packer (SendSpawnPacket).
        using var serverData = BitPackerPool.Get();
        Packer<int>.Write(serverData, Magic);
        Packer<Vector3>.Write(serverData, new Vector3(4f, 5f, 6f));
        var customData = new BitData(serverData);

        // Wire: SpawnPacket serialization writes customData with a Size prefix.
        using var wire = BitPackerPool.Get();
        Packer<BitData>.Write(wire, customData);

        // Client: SpawnPacket deserialization wraps the receive stream.
        wire.ResetPositionAndMode(true);
        var received = Packer<BitData>.Read(wire);

        var toRead = received;
        BitData duplicate = default;
        if (deferredSpawnPath)
        {
            // ProcessSpawnWhenLoadedAsync / BeginAsyncRemoteSpawn copy the view
            // so it survives the async prefab load.
            duplicate = received.Duplicate();
            toRead = duplicate;
        }

        try
        {
            using (toRead.AutoScope())
            {
                int magic = 0;
                Packer<int>.Read(toRead.packer, ref magic);
                Assert.That(magic, Is.EqualTo(Magic),
                    $"PurrNet really dropped the ball here {magic}. (deferred={deferredSpawnPath})");

                Vector3 position = default;
                Packer<Vector3>.Read(toRead.packer, ref position);
                Assert.That(position, Is.EqualTo(new Vector3(4f, 5f, 6f)));
            }
        }
        finally
        {
            if (deferredSpawnPath)
                duplicate.Dispose();
        }
    }
}
