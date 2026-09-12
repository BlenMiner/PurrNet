using PurrNet;

// Shared pickup item for DuckPickupScenario. Gets a NetworkTransform (syncParent on
// by default) added next to it at prefab-creation time. Two variants are spawned:
// one using the manager's ServerStrict rules and one with a per-identity rules
// override whose changeParentAuth includes Owner (the code defaults).
public class DuckPickupDuck : NetworkIdentity
{
    public bool ownerAuthParenting;
    public bool unsafeVariant;

    public static DuckPickupDuck StrictInstance;
    public static DuckPickupDuck OwnerAuthInstance;
    public static DuckPickupDuck UnsafeInstance;

    public static int SetupPlayerCount;
    public static ulong HolderAId;
    public static ulong HolderBId;
    public static bool SetupReceived;
    public static bool UnsafeSetupReceived;

    public static void ResetAll()
    {
        StrictInstance = null;
        OwnerAuthInstance = null;
        UnsafeInstance = null;
        SetupPlayerCount = 0;
        HolderAId = 0;
        HolderBId = 0;
        SetupReceived = false;
        UnsafeSetupReceived = false;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        if (unsafeVariant)
            UnsafeInstance = this;
        else if (ownerAuthParenting)
            OwnerAuthInstance = this;
        else
            StrictInstance = this;
    }

    protected override void OnDespawned()
    {
        if (StrictInstance == this) StrictInstance = null;
        if (OwnerAuthInstance == this) OwnerAuthInstance = null;
        if (UnsafeInstance == this) UnsafeInstance = null;
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    public void AnnounceSetup(int playerCount, ulong holderAId, ulong holderBId)
    {
        SetupPlayerCount = playerCount;
        HolderAId = holderAId;
        HolderBId = holderBId;
        SetupReceived = true;
    }

    // Separate buffered announce for DuckPickupUnsafeScenario so its gate can't be
    // satisfied by stale AnnounceSetup state left over from DuckPickupScenario.
    [ObserversRpc(runLocally: true, bufferLast: true)]
    public void AnnounceUnsafeSetup(int playerCount, ulong holderAId, ulong holderBId)
    {
        SetupPlayerCount = playerCount;
        HolderAId = holderAId;
        HolderBId = holderBId;
        UnsafeSetupReceived = true;
    }
}
