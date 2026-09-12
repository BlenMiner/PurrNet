using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class PlayerSpawnerLocalTransportTests
{
    static readonly FieldInfo DefaultSpawnRulesField =
        typeof(NetworkRules).GetField("_defaultSpawnRules", BindingFlags.Instance | BindingFlags.NonPublic);

    static readonly FieldInfo PlayerPrefabField =
        typeof(PlayerSpawner).GetField("_playerPrefab", BindingFlags.Instance | BindingFlags.NonPublic);

    readonly List<Object> _created = new();
    NetworkManager _manager;
    GameObject _prefab;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (_manager)
        {
            _manager.StopClient();
            _manager.StopServer();
            for (int i = 0; i < 10; i++)
                yield return null;
        }

        foreach (var identity in Object.FindObjectsByType<NetworkIdentity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (identity)
                Object.DestroyImmediate(identity.gameObject);
        }

        for (int i = _created.Count - 1; i >= 0; i--)
        {
            if (_created[i])
                Object.DestroyImmediate(_created[i]);
        }

        _created.Clear();
        _manager = null;
        _prefab = null;
    }

    void Setup(bool despawnIfOwnerDisconnects)
    {
        var go = new GameObject("TestNetworkManager");
        go.SetActive(false);
        _created.Add(go);

        var transport = go.AddComponent<LocalTransport>();
        _manager = go.AddComponent<NetworkManager>();
        _manager.startServerFlags = StartFlags.None;
        _manager.startClientFlags = StartFlags.None;

        var rules = ScriptableObject.CreateInstance<NetworkRules>();
        _created.Add(rules);
        var spawnRules = (SpawnRules)DefaultSpawnRulesField.GetValue(rules);
        spawnRules.despawnIfOwnerDisconnects = despawnIfOwnerDisconnects;
        DefaultSpawnRulesField.SetValue(rules, spawnRules);
        _manager.SetNetworkRules(rules);

        var provider = ScriptableObject.CreateInstance<NetworkPrefabs>();
        provider.autoGenerate = false;
        _created.Add(provider);
        _manager.SetPrefabProvider(provider);

        _prefab = CreateNetworkedPrefab("SpawnerTestPlayer");
        provider.AddRuntimePrefab("spawner-test-player", _prefab);

        _manager.transport = transport;
        go.SetActive(true);

        var spawnerGo = new GameObject("TestPlayerSpawner");
        spawnerGo.SetActive(false);
        var spawner = spawnerGo.AddComponent<PlayerSpawner>();
        PlayerPrefabField.SetValue(spawner, _prefab);
        _created.Add(spawnerGo);
        spawnerGo.SetActive(true);
    }

    GameObject CreateNetworkedPrefab(string name)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        go.AddComponent<NetworkIdentity>();
        _created.Add(go);
        return go;
    }

    IEnumerator WaitUntil(System.Func<bool> condition, float timeout = 10f)
    {
        var deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
    }

    int CountSpawnedPlayersOwnedBy(PlayerID? owner)
    {
        int count = 0;
        foreach (var identity in Object.FindObjectsByType<NetworkIdentity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (identity.gameObject != _prefab && identity.isSpawned && identity.name.StartsWith(_prefab.name) && identity.owner == owner)
                count++;
        }
        return count;
    }

    IEnumerator StartHostAndExpectOnePlayer()
    {
        _manager.StartHost();
        yield return WaitUntil(() => _manager.players.Count == 1 && CountSpawnedPlayersOwnedBy(_manager.players[0]) == 1);

        Assert.That(_manager.isServer && _manager.isClient, Is.True, "host did not start");
        Assert.That(_manager.players.Count, Is.EqualTo(1), "host player did not register");
        Assert.That(CountSpawnedPlayersOwnedBy(_manager.players[0]), Is.EqualTo(1), "PlayerSpawner did not spawn exactly one player for the host");
    }

    [UnityTest]
    public IEnumerator DespawnRuleDisabled_SpawnsHostPlayer()
    {
        Setup(despawnIfOwnerDisconnects: false);
        yield return StartHostAndExpectOnePlayer();
    }

    [UnityTest]
    public IEnumerator DespawnRuleDisabled_PlayerOwningAnotherObject_StillGetsPlayer()
    {
        Setup(despawnIfOwnerDisconnects: false);

        var other = CreateNetworkedPrefab("OtherOwnedObject");
        _manager.prefabProvider.AddRuntimePrefab("other-owned", other);

        void OnJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            if (!asServer) return;
            var inst = UnityProxy.Instantiate(other, Vector3.zero, Quaternion.identity, _manager.gameObject.scene);
            _manager.Spawn(inst);
            inst.GetComponent<NetworkIdentity>().GiveOwnership(player);
        }

        _manager.onPlayerJoined += OnJoined;
        try
        {
            yield return StartHostAndExpectOnePlayer();
        }
        finally
        {
            _manager.onPlayerJoined -= OnJoined;
        }
    }

    [UnityTest]
    public IEnumerator DespawnRuleDisabled_ClientReconnect_KeepsSinglePlayer()
    {
        Setup(despawnIfOwnerDisconnects: false);
        yield return StartHostAndExpectOnePlayer();

        _manager.StopClient();
        yield return WaitUntil(() => _manager.clientState == ConnectionState.Disconnected && _manager.players.Count == 0);
        for (int i = 0; i < 5; i++) yield return null;

        _manager.StartClient();
        yield return WaitUntil(() => _manager.players.Count == 1 && CountSpawnedPlayersOwnedBy(_manager.players[0]) == 1);
        for (int i = 0; i < 5; i++) yield return null;

        Assert.That(_manager.players.Count, Is.EqualTo(1), "player did not reconnect");
        Assert.That(CountSpawnedPlayersOwnedBy(_manager.players[0]), Is.EqualTo(1), "reconnected player does not own exactly one player object");
    }
}
