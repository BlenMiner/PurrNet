using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using K4os.Compression.LZ4;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;

public class SyncBigDataPoolResetTests
{
    [Test]
    public void PooledFirstChunkMayReuseOldStreamIdWithDifferentLength()
    {
        var sync = new SyncBigData();
        var oldBytes = new byte[4096];
        new Random(123).NextBytes(oldBytes);
        var oldCompressed = LZ4Pickler.Pickle(oldBytes);
        var oldParts = (int)Math.Ceiling(oldCompressed.Length / 768d);
        Assert.That(oldParts, Is.GreaterThan(1));
        ReceiveFirst(sync, 7, new ByteData(oldCompressed, 0, 768), oldParts, oldCompressed.Length);
        Assert.That(sync.syncStatus.isDone, Is.False);

        sync.OnPoolReset();

        var updates = 0;
        sync.onSyncStatusChanged += _ => updates++;
        var replacement = new byte[] { 9, 8, 7, 6 };
        var compressed = LZ4Pickler.Pickle(replacement);
        ReceiveFirst(sync, 7, new ByteData(compressed, 0, compressed.Length), 1, compressed.Length);
        Assert.That(sync.syncStatus.isDone, Is.True);
        CollectionAssert.AreEqual(replacement, sync.data);
        Assert.That(updates, Is.EqualTo(1));
        sync.OnPoolReset();
    }

    [Test]
    public void PoolResetDropsOldAcknowledgementsPayloadAndListeners()
    {
        var sync = new SyncBigData(ownerAuth: true);
        var payload = new byte[] { 1, 3, 5, 7 };
        var compressed = LZ4Pickler.Pickle(payload);
        var updates = 0;
        sync.onSyncStatusChanged += _ => updates++;
        ReceiveFirst(sync, 3, new ByteData(compressed, 0, compressed.Length), 1, compressed.Length);
        var pending = (List<BigDataState>)typeof(SyncBigData)
            .GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sync);
        var state = new BigDataState
        {
            id = 3,
            player = PlayerID.Server,
            sentPartsCount = 4,
            confirmedParts = DisposableList<int>.Create(),
            requestedParts = DisposableList<int>.Create()
        };
        state.confirmedParts.Add(0);
        state.requestedParts.Add(2);
        pending.Add(state);
        typeof(SyncBigData).GetField("_nextId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(sync, 99U);
        typeof(SyncBigData).GetField("_partsCounter", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(sync, 7.5f);

        sync.OnPoolReset();

        Assert.That(pending.Count, Is.Zero);
        Assert.That(sync.isDataReady, Is.True);
        Assert.That(sync.data.Count, Is.Zero);
        Assert.That(sync.compressedData.Count, Is.Zero);
        Assert.That(typeof(SyncBigData).GetField("_nextId", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(sync), Is.EqualTo(0U));
        Assert.That(typeof(SyncBigData).GetField("_partsCounter", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(sync), Is.EqualTo(0f));
        Assert.That(updates, Is.EqualTo(1), "Pool reset must not notify the previous lifetime's subscribers.");
        ReceiveFirst(sync, 3, new ByteData(compressed, 0, compressed.Length), 1, compressed.Length);
        Assert.That(updates, Is.EqualTo(1), "The previous lifetime's subscription must be removed.");
        sync.OnPoolReset();
        sync.onSyncStatusChanged += _ => updates++;
        ReceiveFirst(sync, 3, new ByteData(compressed, 0, compressed.Length), 1, compressed.Length);
        Assert.That(updates, Is.EqualTo(2), "The next lifetime's subscription receives the payload.");
        sync.OnPoolReset();
    }

    private static void ReceiveFirst(SyncBigData sync, uint id, ByteData data, int totalParts, int totalLength)
    {
        typeof(SyncBigData).GetMethod("HandleFirstPart", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(sync, new object[] { new PackedUInt(id), data, totalParts, totalLength });
    }

    [Test]
    public void PooledFileClearsCachedContentAndSubscribers()
    {
        var sync = new SyncRawFile();
        var payload = new byte[] { 2, 4, 6 };
        var compressed = LZ4Pickler.Pickle(payload);
        var changes = 0;
        sync.onDataChanged += _ => changes++;
        ReceiveFirst(sync, 2, new ByteData(compressed, 0, compressed.Length), 1, compressed.Length);
        CollectionAssert.AreEqual(payload, sync.content);

        sync.OnPoolReset();

        Assert.That(sync.content, Is.Null);
        Assert.That(sync.data.Count, Is.Zero);
        Assert.That(changes, Is.EqualTo(1));
        ReceiveFirst(sync, 2, new ByteData(compressed, 0, compressed.Length), 1, compressed.Length);
        CollectionAssert.AreEqual(payload, sync.content);
        Assert.That(changes, Is.EqualTo(1), "The previous lifetime's subscriber was removed.");
        sync.OnPoolReset();
    }

    [Test]
    public void PooledAssetStopsExposingThePreviousLifetimeAsset()
    {
        var texture = new UnityEngine.Texture2D(1, 1);
        var sync = new SyncTextureAsset();
        try
        {
            typeof(SyncAsset<UnityEngine.Texture2D>).GetField("_content", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(sync, texture);

            sync.OnPoolReset();

            Assert.That(sync.asset, Is.Null);
            Assert.That(texture, Is.Not.Null, "Pool reset must not destroy an asset held by application code.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PooledFileReloadsTheSamePathWithoutRepeatedReadsAfterLoading(bool emptyReplacement)
    {
        var managerObject = new UnityEngine.GameObject("Pooled file manager");
        managerObject.SetActive(false);
        var manager = managerObject.AddComponent<NetworkManager>();
        var identityObject = new UnityEngine.GameObject("Pooled file owner");
        var identity = identityObject.AddComponent<NetworkIdentity>();
        var sync = new SyncRawFile(ownerAuth: true);
        var filePath = Path.GetTempFileName();
        try
        {
            var player = new PlayerID(2, false);
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.networkManager)).SetValue(identity, manager);
            typeof(NetworkIdentity).GetField("_isSpawnedClient", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(identity, true);
            typeof(NetworkIdentity).GetField("_localPlayer", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(identity, (PlayerID?)player);
            typeof(NetworkIdentity).GetField("_cachedHasConnectedOwner", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(identity, true);
            identity.internalOwnerClient = player;
            sync.SetComponentParent(identity, 0, "file");
            Assert.That(sync.IsController(true), Is.True);

            var previous = new byte[] { 1, 3, 5, 7 };
            File.WriteAllBytes(filePath, previous);
            sync.filePath = filePath;
            CollectionAssert.AreEqual(previous, sync.content);
            sync.OnPoolReset();
            Assert.That(sync.filePath, Is.EqualTo(filePath), "Pooling preserves the configured source path.");
            Assert.That(sync.data.Count, Is.Zero);

            var replacement = emptyReplacement ? Array.Empty<byte>() : new byte[] { 8, 6, 4 };
            File.WriteAllBytes(filePath, replacement);
            var changes = 0;
            sync.onDataChanged += _ => changes++;
            sync.filePath = filePath;
            CollectionAssert.AreEqual(replacement, sync.data);
            Assert.That(sync.syncStatus.isDone, Is.True);
            Assert.That(changes, Is.EqualTo(1));

            // A successfully loaded empty file is different from a cleared pool lifetime.
            File.WriteAllBytes(filePath, new byte[] { 99 });
            sync.filePath = filePath;
            CollectionAssert.AreEqual(replacement, sync.data);
            Assert.That(changes, Is.EqualTo(1), "Unchanged paths retain the normal no-reload behavior once loaded.");
        }
        finally
        {
            sync.OnPoolReset();
            typeof(NetworkIdentity).GetField("_isSpawnedClient", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(identity, false);
            UnityEngine.Object.DestroyImmediate(identityObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
            File.Delete(filePath);
        }
    }
}
