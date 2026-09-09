# Network correctness runs

The `Network Tests` workflow builds or downloads one Linux player per server/host job.
With an empty `scenario` input, each job uses that player for every batch in
[`network-scenarios.json`](network-scenarios.json):

| Batch | Purpose |
| --- | --- |
| Full active suite | All authored active scenarios, including bootstrap readiness during migration |
| `OrdinaryJoinSceneReconciliationScenario` | Different starting scenes, authoritative load order, and retained client scene instances |
| `OrdinaryJoinDuplicateScenesScenario` | Separate local instances of the same scene asset |
| `OrdinaryJoinAddressableSceneScenario` | Ordinary joining with preloaded Addressable scenes |
| `PromotedServerTransferScenario` | World retention and replication after another client becomes the server |
| `UnexpectedHostLossMigrationScenario` | Surviving clients elect a replacement after the runner kills the authority, then migrate to a different port |
| `MigrationFailureCleanupScenario` | Timed-out and canceled migration attempts clean up before a successful normal reconnect |

The six isolated scenarios stay inactive in `Bootstrap.unity`. The runner selects
each explicitly with `-scenario`, which includes inactive scenario components.
Every batch starts fresh processes and receives its own empty results directory;
pre-connection setup and terminal host migration therefore cannot affect the
other scenarios. Both server and host modes run every batch. Use at least three
total players so host mode has two external clients for promotion and transfer.

The full suite currently requires the manifest's exact ordered list of 119 scenarios per peer. This
list includes `BootstrapSceneTransferReadinessScenario` and the terminal
`SinglePromotedServerTransferScenario`. Each isolated
batch requires exactly `Bootstrap` followed by the selected scenario. Every expected peer must
produce valid, successful results matching that exact list;
missing results or logs, missing or duplicated scenarios, truncated results, process failures, and fatal
network/Unity logs fail the job. This includes Unity's warning that a referenced
script on a Behaviour is missing, even if scenario results otherwise pass.
Update the manifest's `expectedScenarios` list when the authored active suite
changes, or add another batch for a scenario that requires its own processes.

The host-loss batch is the only exception to requiring authority results. The
runner waits for its exact ready marker while the authority is alive, verifies
the Unity process belongs to its own launch, and sends a hard kill to that
process. Only after confirming the kill does it write the injection receipt
required by the surviving clients. An early authority exit, missing marker,
unsuccessful injection, or missing/failed survivor results fails the batch. The
runner supplies distinct available UDP ports for the original and replacement
servers. Readiness markers, injection receipts, and failure-cleanup metadata are
retained as artifacts but are excluded from peer result tables.

The crash scenario preserves state synchronously in the disconnect callback,
then elects a survivor and changes endpoints after a preparation delay. The
retained object's owner reconnects later than the other followers, so local
transfer completion cannot stand in for every player being connected. Assertions
cover the original scene and object instances, network IDs, ownership, replicated
state, fresh spawning/despawning, and a subsequent scene load/unload cycle.
Before reconnecting, the owner deliberately changes its disconnected SyncVar,
SyncList, and root transform state. The test requires the chosen host's values
and position to replace those writes, then verifies that subsequent
owner-authoritative writes and movement replicate normally.
The reused root and child must each run one normal network despawn and spawn,
with the host's values, list, and pose applied by `OnSpawned`. The scenario checks
`isMigratingServer` inside these callbacks and checks that it clears afterward.

Promotion resumes from the chosen client's existing state. It uses the existing
`PromoteToServer` callback for server-specific setup and the existing ownership
callbacks for controller changes. Reconnecting clients accept that host's state;
there is no merge of their old values. Their compatible Unity objects survive,
but their network lifecycle restarts: despawn, ordinary pool reset, and the same
spawn/baseline/ownership flow used for late joining. Missing objects spawn normally
and extra objects are removed. There is no per-module transfer callback.

Ownership catch-up follows the normal observer state callbacks. Built-in modules
use their ordinary initialization, pool reset, and authority rules. The read-only
`isMigratingServer` property is available on the manager, identity, and module so
user callbacks can skip optional cleanup during promotion or transfer. Core
synchronization does not depend on user scripts checking it. Unity objects that
user despawn callbacks destroy cannot be retained and must spawn again.

Scene reconciliation uses the same initial scene inventory that the server sends
on a normal connection. Migration adds no separate scene request/response exchange.
The client waits for any already-submitted Single load before reconciling that
inventory, and applies later scene changes afterward.

The failure scenario exercises transfer cancellation, transfer timeout, promotion
cancellation, promotion timeout, and explicit release of abandoned preservation.
A test transport keeps connection establishment pending for the first four
cases; each must return false, remove retained objects/modules, and permit a
normal reconnect. Cancellation during later baseline reconciliation and external
relay/lobby election services are outside these scenarios' current coverage.

Setting `scenario` explicitly runs only that scenario and the connection setup,
preserving targeted diagnosis. Each peer has a bounded runtime: 20 minutes for
the full suite and 5 minutes for an isolated scenario. The workflow's existing
job timeout also covers build time and all batches.

Results and logs are grouped under `test-results/<batch>/` and
`test-logs/<batch>/` in the existing artifacts and job summary. The benchmark
workflow's optional correctness run uses the same batches and reuses its player
artifact. CI triggers are unchanged; no new scheduled or push-triggered runs are
created by this configuration.

Before a player build, both the network-test and benchmark workflows reset
`Library/BuildCache` and `Library/com.unity.addressables` when the Library cache
restore is not an exact key hit. A fallback cache can carry old per-scene
MonoScript dependencies into newly shared Addressable scene content, producing
missing-script warnings even when compilation succeeds. Exact cache hits keep
their content caches; a cache miss is harmless. `Library/Bee` and all other
Library data remain available for incremental builds. The shared cleanup helper
validates both cache paths inside the explicit Unity workspace before deleting
either and refuses symlinked or non-directory targets.

For a Linux player already built locally:

```bash
MODE=host CLIENT_COUNT=3 BIN="$PWD/build/StandaloneLinux64/PurrNetTests" \
  bash .github/scripts/network-tests.sh
```

For a Windows player, PowerShell 7 runs every batch in both modes by default:

```powershell
pwsh -File .github/scripts/network-tests.ps1 -PlayerPath 'C:\build\PurrNetTests.exe' -OutputDirectory 'C:\test-results\migration-run-1'
```

Use `-Scenario UnexpectedHostLossMigrationScenario` for one batch, `-Mode host`
or `-Mode server` for one mode, and `-TotalPlayers 3` to change the player count.
The Windows runner uses the same manifest and validates the same process and
result contracts. Its own process checks run with
`pwsh -File .github/scripts/test-network-tests.ps1`.

Use fresh output directories on subsequent runs, for example by setting
`RESULTS_ROOT` and `LOGS_ROOT`. The runner rejects existing batch directories
to prevent stale results or readiness markers from passing a new run.

`bash .github/scripts/test-network-tests.sh` exercises mock players,
including missing, truncated, reordered, failed, and stale results, a missing
expected scenario list, missing logs, unexpected exceptions, assertions, and Unity's
missing-script warning. The workflow
runs these checks before building or downloading Unity players.

`python3 .github/scripts/test-host-loss.py` runs 17 Linux process checks for
successful host/server crashes, missing/wrong/stale readiness, early authority
exit, an ineffective kill, foreign PID refusal, and missing/failed/reordered
survivor results. One check uses the real `xvfb-run` wrapper to verify child PID
ownership and signal exit propagation. CI runs these before using a Unity player.

`bash .github/scripts/test-reset-addressable-build-caches.sh` runs 10 temporary
fixture checks for the cache cleanup boundaries, retained Bee/outside data,
missing caches, and refused symlinks. Both build workflows run these checks.
