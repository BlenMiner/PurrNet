using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

// Replicates the Discord "duck pickup" report: one shared networked item, one avatar
// per player each with a holding point, a player picks the item up (ownership + parent
// under their hand) and every peer must see the item in the *holder's* hand — never in
// the local player's own hand.
//
// Two ducks are spawned to cover both rule configurations:
//  - strict duck: uses the manager's ServerStrict rules (changeParentAuth = Server).
//  - ownerAuth duck: per-identity NetworkRules override with the code defaults
//    (changeParentAuth = Server | Owner).
//
// Phase 1: server-auth pickup — server gives the strict duck to holder A and reparents
//          it under holder A's "Hand" (a child with its own NetworkIdentity).
// Phase 2: owner-local pickup under ServerStrict — holder B owns the strict duck and
//          locally SetParent()s it under its own "BareHand", exactly like the reported
//          HoldObject code. changeParentAuth is Server-only, so the engine must log an
//          error and revert the local change to the replicated parent; no peer (holder
//          B included) may end up seeing the new parent.
// Phase 3: owner-auth pickup — holder B owns the ownerAuth duck and locally
//          SetParent()s it under its own "BareHand" (plain child transform, exercises
//          path-based parenting). Must sync to every peer.
// Phase 4: unauthorized pickup — holder A (not the owner) locally SetParent()s the
//          ownerAuth duck under its own hand, replicating the reported bug setup where
//          ownership was never actually acquired. Remote peers must be unaffected and
//          the offender's local change must be reverted.
public class DuckPickupScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _verifyTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;
    [SerializeField] private float _settleSeconds = 1.5f;

    private const int BarrierSetup = 9100;
    private const int BarrierServerAuth = 9101;
    private const int BarrierStrictOwner = 9102;
    private const int BarrierOwnerAuth = 9103;
    private const int BarrierUnauthorized = 9104;
    private const int BarrierEnd = 9105;

    private const float LocalPositionEpsilon = 0.25f;

    private DuckPickupPlayer _playerPrefab;
    private DuckPickupDuck _strictDuckPrefab;
    private DuckPickupDuck _ownerAuthDuckPrefab;
    private NetworkRules _ownerAuthRules;

    private void CreatePrefabs()
    {
        var playerGo = new GameObject(nameof(DuckPickupPlayer));
        playerGo.SetActive(false);
        _playerPrefab = playerGo.AddComponent<DuckPickupPlayer>();

        var handGo = new GameObject("Hand");
        handGo.transform.SetParent(playerGo.transform);
        handGo.transform.localPosition = new Vector3(0f, 1.5f, 0.5f);
        handGo.AddComponent<NetworkIdentity>();

        var bareHandGo = new GameObject("BareHand");
        bareHandGo.transform.SetParent(playerGo.transform);
        bareHandGo.transform.localPosition = new Vector3(0f, 1.5f, -0.5f);

        _strictDuckPrefab = CreateDuckPrefab(nameof(DuckPickupDuck), null);

        // Fresh NetworkRules uses the code defaults: changeParentAuth = Server | Owner.
        _ownerAuthRules = ScriptableObject.CreateInstance<NetworkRules>();
        _ownerAuthDuckPrefab = CreateDuckPrefab(nameof(DuckPickupDuck) + "OwnerAuth", _ownerAuthRules);
        _ownerAuthDuckPrefab.ownerAuthParenting = true;

        DuckPickupPlayer.ResetAll();
        DuckPickupDuck.ResetAll();
    }

    private static DuckPickupDuck CreateDuckPrefab(string name, NetworkRules rulesOverride)
    {
        var duckGo = new GameObject(name);
        duckGo.SetActive(false);
        var duck = duckGo.AddComponent<DuckPickupDuck>();
        duckGo.AddComponent<NetworkTransform>();

        if (rulesOverride)
        {
            foreach (var nid in duckGo.GetComponents<NetworkIdentity>())
                nid.SetNetworkRules(rulesOverride);
        }

        return duck;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefabs();
        manager.prefabProvider.AddRuntimePrefab(_playerPrefab.name, _playerPrefab.gameObject);
        manager.prefabProvider.AddRuntimePrefab(_strictDuckPrefab.name, _strictDuckPrefab.gameObject);
        manager.prefabProvider.AddRuntimePrefab(_ownerAuthDuckPrefab.name, _ownerAuthDuckPrefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        var failures = new List<string>();

        if (ctx.isServer)
        {
            var holders = PickHolders(ctx);
            if (holders.Count < 2)
                return ScenarioResult.Fail($"need at least 2 external clients, got {holders.Count}");

            DuckPickupDuck strictDuck, ownerAuthDuck;
            HierarchyV2.SupressAutoOwner();
            try
            {
                strictDuck = Instantiate(_strictDuckPrefab, new Vector3(0f, 0.5f, 0f), Quaternion.identity);
                ownerAuthDuck = Instantiate(_ownerAuthDuckPrefab, new Vector3(0f, 0.5f, 3f), Quaternion.identity);
            }
            finally
            {
                HierarchyV2.ResumeAutoOwner();
            }

            var players = ctx.networkManager.players;
            var avatars = new List<DuckPickupPlayer>();
            for (int i = 0; i < players.Count; i++)
                avatars.Add(Instantiate(_playerPrefab, new Vector3(10f * (i + 1), 0f, 0f), Quaternion.identity));

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => strictDuck.isSpawned && ownerAuthDuck.isSpawned && avatars.TrueForAll(a => a.isSpawned),
                    _spawnTimeoutSeconds, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail("server-side spawn timeout for ducks/avatars");
            }

            for (int i = 0; i < players.Count; i++)
                avatars[i].GiveOwnership(players[i]);

            strictDuck.AnnounceSetup(players.Count, holders[0].id.value, holders[1].id.value);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(SetupComplete, _spawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"setup timeout: announced={DuckPickupDuck.SetupReceived}, " +
                $"strictDuck={(bool)DuckPickupDuck.StrictInstance}, " +
                $"ownerAuthDuck={(bool)DuckPickupDuck.OwnerAuthInstance}, " +
                $"players={DuckPickupPlayer.Instances.Count}/{DuckPickupDuck.SetupPlayerCount}, " +
                $"holderA={(bool)DuckPickupPlayer.FindByOwner(DuckPickupDuck.HolderAId)}, " +
                $"holderB={(bool)DuckPickupPlayer.FindByOwner(DuckPickupDuck.HolderBId)}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierSetup, _barrierTimeoutSeconds);

        var strict = DuckPickupDuck.StrictInstance;
        var ownerAuth = DuckPickupDuck.OwnerAuthInstance;
        ulong holderAId = DuckPickupDuck.HolderAId;
        ulong holderBId = DuckPickupDuck.HolderBId;

        // ---- Phase 1: server-auth pickup into holder A's identity-carrying Hand ----

        if (ctx.isServer)
        {
            var holderAPlayer = DuckPickupPlayer.FindByOwner(holderAId);
            strict.GiveOwnership(new PlayerID(holderAId, false));
            strict.transform.SetParent(holderAPlayer.hand);
        }

        if (IsLocalPlayer(ctx, holderAId))
        {
            // The picking player snaps the item into the holding point, like the
            // reported HoldObject code does with DOLocalMove -> localPosition = zero.
            // Gate on ownership too, and re-assert until the snap sticks: zeroing
            // before the ownership packet arrives means holder A is not the NT
            // controller yet, so in-flight interpolation from the server's stream
            // overwrites the zero — and once holder A becomes controller it streams
            // the overwritten position to every peer (seen on CI as the duck frozen
            // ~2 units from the hand on all clients).
            int stableFrames = 0;
            await WaitOrFail(() =>
            {
                if (!strict || !strict.isOwner || !IsParentedTo(strict, holderAId, useHand: true))
                    return false;

                if (strict.transform.localPosition.magnitude >= LocalPositionEpsilon)
                {
                    strict.transform.localPosition = Vector3.zero;
                    stableFrames = 0;
                    return false;
                }

                return ++stableFrames >= 30;
            }, _verifyTimeoutSeconds, ctx, failures, "phase1 holderA could not settle duck in own hand");
        }

        await VerifyPickup(ctx, strict, holderAId, useHand: true,
            "phase1(server-auth reparent, ServerStrict rules)", failures);
        await ScenarioBarrier.Wait(ctx, BarrierServerAuth, _barrierTimeoutSeconds);

        // ---- Phase 2: owner-local pickup under ServerStrict (must NOT propagate) ----

        if (ctx.isServer)
            strict.GiveOwnership(new PlayerID(holderBId, false));

        if (IsLocalPlayer(ctx, holderBId))
        {
            bool ownerOk = await WaitOrFail(
                () => strict && strict.isOwner,
                _verifyTimeoutSeconds, ctx, failures, "phase2 holderB never became owner of strict duck");

            if (ownerOk)
            {
                var self = DuckPickupPlayer.FindByOwner(holderBId);
                if (!self || !self.bareHand)
                {
                    failures.Add($"phase2 holderB could not resolve own bare hand (avatar={(bool)self})");
                }
                else
                {
                    strict.transform.SetParent(self.bareHand);
                    LogReparentDiagnostics(ctx, strict, "phase2-strict-holderB");
                }
            }
        }

        await UniTask.WaitForSeconds(_settleSeconds, cancellationToken: ctx.cancellationToken);

        // The unauthorized local reparent must not propagate to any peer, and on the
        // offending client the engine reverts it to the replicated parent (with an error
        // log) instead of leaving a silent local-only desync.
        if (!IsParentedTo(strict, holderAId, useHand: true))
        {
            failures.Add(
                $"phase2(ServerStrict): owner's local SetParent was not " +
                $"{(IsLocalPlayer(ctx, holderBId) ? "reverted" : "contained")}; " +
                $"strict duck is under: {DescribeParent(strict)}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierStrictOwner, _barrierTimeoutSeconds);

        // ---- Phase 3: owner-auth pickup with Owner-permissive rules (must sync) ----

        if (ctx.isServer)
            ownerAuth.GiveOwnership(new PlayerID(holderBId, false));

        if (IsLocalPlayer(ctx, holderBId))
        {
            bool ownerOk = await WaitOrFail(
                () => ownerAuth && ownerAuth.isOwner,
                _verifyTimeoutSeconds, ctx, failures, "phase3 holderB never became owner of ownerAuth duck");

            if (ownerOk)
            {
                var self = DuckPickupPlayer.FindByOwner(holderBId);
                if (!self || !self.bareHand)
                {
                    failures.Add($"phase3 holderB could not resolve own bare hand (avatar={(bool)self})");
                }
                else
                {
                    ownerAuth.transform.SetParent(self.bareHand);
                    ownerAuth.transform.localPosition = Vector3.zero;
                    LogReparentDiagnostics(ctx, ownerAuth, "phase3-ownerAuth-holderB");
                }
            }
        }

        await VerifyPickup(ctx, ownerAuth, holderBId, useHand: false,
            "phase3(owner-auth reparent, Owner-permissive rules)", failures);
        await ScenarioBarrier.Wait(ctx, BarrierOwnerAuth, _barrierTimeoutSeconds);

        // ---- Phase 4: unauthorized pickup (the reported bug setup) ----

        if (IsLocalPlayer(ctx, holderAId))
        {
            var self = DuckPickupPlayer.FindByOwner(holderAId);
            if (self && self.hand)
                ownerAuth.transform.SetParent(self.hand);
            else
                failures.Add($"phase4 offender could not resolve own hand (avatar={(bool)self})");
        }

        await UniTask.WaitForSeconds(_settleSeconds, cancellationToken: ctx.cancellationToken);

        if (!IsParentedTo(ownerAuth, holderBId, useHand: false))
        {
            failures.Add(
                $"phase4(unauthorized): non-owner's local SetParent was not " +
                $"{(IsLocalPlayer(ctx, holderAId) ? "reverted" : "contained")}; " +
                $"ownerAuth duck is under: {DescribeParent(ownerAuth)}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierUnauthorized, _barrierTimeoutSeconds);

        // ---- Cleanup ----

        if (ctx.isServer)
        {
            if (strict) Destroy(strict.gameObject);
            if (ownerAuth) Destroy(ownerAuth.gameObject);
            for (int i = DuckPickupPlayer.Instances.Count - 1; i >= 0; i--)
            {
                var instance = DuckPickupPlayer.Instances[i];
                if (instance) Destroy(instance.gameObject);
            }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => !DuckPickupDuck.StrictInstance && !DuckPickupDuck.OwnerAuthInstance
                      && DuckPickupPlayer.Instances.Count == 0,
                _despawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"cleanup timeout: strict={(bool)DuckPickupDuck.StrictInstance}, " +
                $"ownerAuth={(bool)DuckPickupDuck.OwnerAuthInstance}, " +
                $"players={DuckPickupPlayer.Instances.Count}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok("duck always ended up in the holder's hand on every peer");
    }

    private static void LogReparentDiagnostics(ScenarioContext ctx, DuckPickupDuck duck, string label)
    {
        var nt = duck.GetComponent<NetworkTransform>();
        var ignoringField = typeof(NetworkTransform).GetField("_isIgnoringParentChanges",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        object ignoring = ignoringField != null && nt ? ignoringField.GetValue(nt) : "<?>";
        var localPlayer = ctx.networkManager.localPlayer;

        Debug.Log(
            $"[DuckDiag/{label}] trsParent={duck.transform.parent?.name ?? "<null>"} " +
            $"identityParent={(duck.parent ? duck.parent.gameObject.name : "<null>")} " +
            $"ntIgnoringParentChanges={ignoring} " +
            $"duckOwner={(duck.owner.HasValue ? duck.owner.Value.id.value.ToString() : "none")} " +
            $"ntOwner={(nt && nt.owner.HasValue ? nt.owner.Value.id.value.ToString() : "none")} " +
            $"ntSpawned={(nt && nt.isSpawned)} ntSyncParent={(nt && nt.syncParent)} " +
            $"duckAuth={duck.HasChangeParentAuthority(localPlayer, false)} " +
            $"ntAuth={(nt && nt.HasChangeParentAuthority(localPlayer, false))}");
    }

    private static bool SetupComplete()
    {
        if (!DuckPickupDuck.SetupReceived)
            return false;
        if (!DuckPickupDuck.StrictInstance || !DuckPickupDuck.StrictInstance.isSpawned)
            return false;
        if (!DuckPickupDuck.OwnerAuthInstance || !DuckPickupDuck.OwnerAuthInstance.isSpawned)
            return false;
        if (DuckPickupPlayer.Instances.Count < DuckPickupDuck.SetupPlayerCount)
            return false;
        return DuckPickupPlayer.FindByOwner(DuckPickupDuck.HolderAId)
               && DuckPickupPlayer.FindByOwner(DuckPickupDuck.HolderBId);
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
