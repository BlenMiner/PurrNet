using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

// Replicates the Discord "duck pickup" report under the Unsafe rules the reporter was
// actually using: spawnAuth=Everyone, defaultOwner=SpawnerIfClientOnly,
// changeParentAuth=-1 (everyone), transferAuth/assignAuth permissive.
//
// Unlike DuckPickupScenario (server-spawned avatars), here EACH CLIENT spawns its own
// avatar — the natural pattern with Unsafe rules and the reporter's likely setup. The
// duck is a single server-spawned object. The pickup then happens entirely client-side,
// exactly like the reported HoldObject code: GiveOwnership(local) followed by a local
// SetParent(holdingPoint).
//
// Phase U1: holder A picks up the duck into its "Hand" (child with NetworkIdentity).
// Phase U2: holder B takes it into its "BareHand" (plain child, path-based parenting).
// Every peer must see the duck under the *holder's* avatar, never the local player's.
//
// Manager rules cannot be swapped while connected, and the server validates
// client-initiated spawns against the MANAGER rules only (HierarchyV2 ~1232), so:
// - _managerRules (spawnAuth=Everyone, same asset ClientSpawnScenario uses) is applied
//   to the manager in Setup, before any connection starts.
// - _unsafeRules (the shipped Unsafe asset) is applied per-identity to the avatar and
//   duck prefabs, which is what the parenting/ownership authority checks consult.
public class DuckPickupUnsafeScenario : Scenario
{
    [Tooltip("Applied to the NetworkManager in Setup. Must have spawnAuth=Everyone so " +
             "clients can spawn their own avatars (server validates spawns against manager rules).")]
    [SerializeField] private NetworkRules _managerRules;

    [Tooltip("Per-identity override for the avatar and duck prefabs; the shipped Unsafe rules " +
             "(changeParentAuth/transferAuth for everyone), matching the reporter's setup.")]
    [SerializeField] private NetworkRules _unsafeRules;

    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _verifyTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierSetup = 9201;
    private const int BarrierPickupA = 9202;
    private const int BarrierPickupB = 9203;
    private const int BarrierEnd = 9204;

    private const float LocalPositionEpsilon = 0.25f;

    private DuckPickupPlayer _playerPrefab;
    private DuckPickupDuck _duckPrefab;
    private NetworkManager _manager;

    private void CreatePrefabs()
    {
        var playerGo = new GameObject(nameof(DuckPickupPlayer) + "Unsafe");
        playerGo.SetActive(false);
        _playerPrefab = playerGo.AddComponent<DuckPickupPlayer>();

        var handGo = new GameObject("Hand");
        handGo.transform.SetParent(playerGo.transform);
        handGo.transform.localPosition = new Vector3(0f, 1.5f, 0.5f);
        handGo.AddComponent<NetworkIdentity>();

        var bareHandGo = new GameObject("BareHand");
        bareHandGo.transform.SetParent(playerGo.transform);
        bareHandGo.transform.localPosition = new Vector3(0f, 1.5f, -0.5f);

        var duckGo = new GameObject(nameof(DuckPickupDuck) + "Unsafe");
        duckGo.SetActive(false);
        _duckPrefab = duckGo.AddComponent<DuckPickupDuck>();
        _duckPrefab.unsafeVariant = true;
        duckGo.AddComponent<NetworkTransform>();

        foreach (var nid in playerGo.GetComponentsInChildren<NetworkIdentity>(true))
            nid.SetNetworkRules(_unsafeRules);
        foreach (var nid in duckGo.GetComponents<NetworkIdentity>())
            nid.SetNetworkRules(_unsafeRules);
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        _manager = manager;
        CreatePrefabs();

        // Reset here (pre-connection), NOT in RunScenario: the server starts spawning as
        // soon as it issues the scenario start signal, so a client-side reset inside
        // RunScenario can race the incoming duck spawn and wipe it permanently.
        DuckPickupPlayer.ResetAll();
        DuckPickupDuck.ResetAll();

        // Same manager-rules pattern (and asset) as ClientSpawnScenario: applied before
        // any connection exists, so client-initiated spawns pass the server's check.
        if (_managerRules)
            manager.SetNetworkRules(_managerRules);

        manager.prefabProvider.AddRuntimePrefab(_playerPrefab.name, _playerPrefab.gameObject);
        manager.prefabProvider.AddRuntimePrefab(_duckPrefab.name, _duckPrefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (!_unsafeRules || !_managerRules)
            return ScenarioResult.Fail("_unsafeRules / _managerRules not assigned");

        var failures = new List<string>();

        if (ctx.isServer)
        {
            var holders = PickHolders(ctx);
            if (holders.Count < 2)
                return ScenarioResult.Fail($"need at least 2 external clients, got {holders.Count}");

            DuckPickupDuck duckInstance;
            HierarchyV2.SupressAutoOwner();
            try
            {
                duckInstance = Instantiate(_duckPrefab, new Vector3(0f, 0.5f, 0f), Quaternion.identity);
            }
            finally
            {
                HierarchyV2.ResumeAutoOwner();
            }

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => duckInstance.isSpawned, _spawnTimeoutSeconds, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail("server-side duck spawn timeout");
            }

            duckInstance.AnnounceUnsafeSetup(
                ctx.networkManager.players.Count, holders[0].id.value, holders[1].id.value);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => DuckPickupDuck.UnsafeSetupReceived
                      && DuckPickupDuck.UnsafeInstance && DuckPickupDuck.UnsafeInstance.isSpawned,
                _spawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"announce timeout: announced={DuckPickupDuck.UnsafeSetupReceived}, " +
                $"duck={(bool)DuckPickupDuck.UnsafeInstance}");
        }

        // Every client spawns its OWN avatar — the natural pattern under Unsafe rules
        // (spawnAuth=Everyone) and the key difference from DuckPickupScenario.
        if (ctx.isClient)
        {
            var localPlayer = ctx.networkManager.localPlayer;
            var avatar = Instantiate(_playerPrefab,
                new Vector3(10f * (localPlayer.id.value + 1), 0f, 0f), Quaternion.identity);

            bool spawned = await WaitOrFail(
                () => avatar.isSpawned, _spawnTimeoutSeconds, ctx, failures,
                "client-side avatar spawn timeout");

            // SpawnerIfClientOnly covers plain clients; the host's local player spawns
            // as server+client, so claim ownership explicitly to make it deterministic.
            if (spawned && (!avatar.owner.HasValue || avatar.owner.Value != localPlayer))
                avatar.GiveOwnership(localPlayer);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(SetupComplete, _verifyTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"setup timeout: avatars={DuckPickupPlayer.Instances.Count}/{DuckPickupDuck.SetupPlayerCount}, " +
                $"holderA={(bool)DuckPickupPlayer.FindByOwner(DuckPickupDuck.HolderAId)}, " +
                $"holderB={(bool)DuckPickupPlayer.FindByOwner(DuckPickupDuck.HolderBId)}, " +
                $"owners=[{DescribeAvatarOwners()}]");
        }

        await ScenarioBarrier.Wait(ctx, BarrierSetup, _barrierTimeoutSeconds);

        var duck = DuckPickupDuck.UnsafeInstance;
        ulong holderAId = DuckPickupDuck.HolderAId;
        ulong holderBId = DuckPickupDuck.HolderBId;

        // ---- Phase U1: holder A picks up, entirely client-side (the reported flow) ----

        await ClientSidePickup(ctx, duck, holderAId, useHand: true, "phaseU1", failures);
        await VerifyPickup(ctx, duck, holderAId, useHand: true,
            "phaseU1(unsafe rules, client-spawned avatars, pickup into Hand)", failures);
        await ScenarioBarrier.Wait(ctx, BarrierPickupA, _barrierTimeoutSeconds);

        // ---- Phase U2: holder B takes it, into the identity-less BareHand ----

        await ClientSidePickup(ctx, duck, holderBId, useHand: false, "phaseU2", failures);
        await VerifyPickup(ctx, duck, holderBId, useHand: false,
            "phaseU2(unsafe rules, hand-off between client-spawned avatars)", failures);
        await ScenarioBarrier.Wait(ctx, BarrierPickupB, _barrierTimeoutSeconds);

        // ---- Cleanup ----

        if (ctx.isServer)
        {
            if (duck) Destroy(duck.gameObject);
            for (int i = DuckPickupPlayer.Instances.Count - 1; i >= 0; i--)
            {
                var instance = DuckPickupPlayer.Instances[i];
                if (instance) Destroy(instance.gameObject);
            }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => !DuckPickupDuck.UnsafeInstance && DuckPickupPlayer.Instances.Count == 0,
                _despawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"cleanup timeout: duck={(bool)DuckPickupDuck.UnsafeInstance}, " +
                $"players={DuckPickupPlayer.Instances.Count}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        return failures.Count == 0
            ? ScenarioResult.Ok("client-side pickup with client-spawned avatars synced correctly on every peer")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    // The reported HoldObject flow: give yourself ownership, then locally reparent.
    private async UniTask ClientSidePickup(ScenarioContext ctx, DuckPickupDuck duck, ulong holderId,
        bool useHand, string label, List<string> failures)
    {
        if (!IsLocalPlayer(ctx, holderId))
            return;

        duck.GiveOwnership(ctx.networkManager.localPlayer);

        bool ownerOk = await WaitOrFail(
            () => duck && duck.isOwner, _verifyTimeoutSeconds, ctx, failures,
            $"{label} holder never became owner (client-side GiveOwnership under unsafe rules)");
        if (!ownerOk)
            return;

        var self = DuckPickupPlayer.FindByOwner(holderId);
        var target = self ? (useHand ? self.hand : self.bareHand) : null;
        if (!target)
        {
            failures.Add($"{label} holder could not resolve own holding point (avatar={(bool)self})");
            return;
        }

        duck.transform.SetParent(target);
        duck.transform.localPosition = Vector3.zero;
    }

    private static bool SetupComplete()
    {
        if (DuckPickupPlayer.Instances.Count < DuckPickupDuck.SetupPlayerCount)
            return false;
        return DuckPickupPlayer.FindByOwner(DuckPickupDuck.HolderAId)
               && DuckPickupPlayer.FindByOwner(DuckPickupDuck.HolderBId);
    }

    private static string DescribeAvatarOwners()
    {
        var parts = new List<string>();
        foreach (var instance in DuckPickupPlayer.Instances)
        {
            if (!instance) continue;
            parts.Add(instance.owner.HasValue ? instance.owner.Value.id.value.ToString() : "none");
        }
        return string.Join(",", parts);
    }

    private async UniTask VerifyPickup(ScenarioContext ctx, DuckPickupDuck duck, ulong holderId, bool useHand,
        string label, List<string> failures)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IsParentedTo(duck, holderId, useHand)
                      && duck.transform.localPosition.magnitude < LocalPositionEpsilon,
                _verifyTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            string detail = duck ? DescribeParent(duck) : "<no duck>";
            string bugHint = string.Empty;

            if (duck && ctx.networkManager.isLocalPlayerReady)
            {
                ulong localId = ctx.networkManager.localPlayer.id.value;
                if (localId != holderId)
                {
                    var localAvatar = DuckPickupPlayer.FindByOwner(localId);
                    if (localAvatar && duck.transform.parent
                        && duck.transform.parent.IsChildOf(localAvatar.transform))
                        bugHint = " [REPORTED BUG: duck ended up in the LOCAL player's hand]";
                }
            }

            failures.Add($"{label}: duck never settled under holder {holderId}'s "
                         + $"{(useHand ? "Hand" : "BareHand")}; actual: {detail}{bugHint}");
        }
    }

    private static bool IsParentedTo(DuckPickupDuck duck, ulong holderId, bool useHand)
    {
        var holder = DuckPickupPlayer.FindByOwner(holderId);
        if (!duck || !holder)
            return false;

        var expected = useHand ? holder.hand : holder.bareHand;
        return expected && duck.transform.parent == expected;
    }

    private async UniTask<bool> WaitOrFail(Func<bool> condition, float timeout, ScenarioContext ctx,
        List<string> failures, string message)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(condition, timeout, ctx.cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            failures.Add(message);
            return false;
        }
    }

    private static string DescribeParent(DuckPickupDuck duck)
    {
        var parent = duck.transform.parent;
        if (!parent)
            return $"<unparented, ownerId={(duck.owner.HasValue ? duck.owner.Value.id.value.ToString() : "none")}>";

        var chain = parent.name;
        var avatar = parent.GetComponentInParent<DuckPickupPlayer>(true);
        if (avatar)
            chain += $" of avatar ownerId={(avatar.owner.HasValue ? avatar.owner.Value.id.value.ToString() : "none")}";
        return $"{chain}, localPos={duck.transform.localPosition}, "
               + $"duckOwnerId={(duck.owner.HasValue ? duck.owner.Value.id.value.ToString() : "none")}";
    }

    private static bool IsLocalPlayer(ScenarioContext ctx, ulong playerId)
    {
        return ctx.networkManager.isLocalPlayerReady
               && ctx.networkManager.localPlayer.id.value == playerId;
    }

    private static List<PlayerID> PickHolders(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;
        var hostLocal = manager.isLocalPlayerReady && ctx.role == NetworkRole.Host
            ? manager.localPlayer
            : (PlayerID?)null;

        var eligible = new List<PlayerID>();
        var players = manager.players;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p.isServer) continue;
            if (hostLocal.HasValue && hostLocal.Value == p) continue;
            eligible.Add(p);
        }

        eligible.Sort((a, b) => a.id.value.CompareTo(b.id.value));
        return eligible;
    }
}
