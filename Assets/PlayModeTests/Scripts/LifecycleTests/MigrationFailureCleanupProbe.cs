using PurrNet;

public sealed class MigrationFailureCleanupProbe : NetworkIdentity
{
    public const int ExpectedValue = 36501;
    private readonly SyncVar<int> _value = new(-1);
    public static MigrationFailureCleanupProbe clientInstance { get; private set; }
    public static int clientSpawns { get; private set; }
    public static int clientDespawns { get; private set; }
    public int value => _value.value;

    public static void ResetAll()
    {
        clientInstance = null;
        clientSpawns = clientDespawns = 0;
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer)
    {
        if (asServer)
            _value.value = ExpectedValue;
        else
        {
            clientInstance = this;
            clientSpawns++;
        }
    }

    protected override void OnDespawned(bool asServer)
    {
        if (asServer)
            return;
        if (clientInstance == this)
            clientInstance = null;
        clientDespawns++;
    }
}
