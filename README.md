<p align="center">
    <img height="170" alt="purrnet-icon-text-horizontal-orange" src="https://github.com/user-attachments/assets/0e9a07c9-5486-4d6b-9dc2-fa9f59c662ad" />
</p>

<h3 align="center">Networking that feels like Unity, not like netcode.</h3>

<p align="center">
    <a href="https://openupm.com/packages/dev.purrnet.purrnet/"><img alt="openupm" src="https://img.shields.io/npm/v/dev.purrnet.purrnet?label=openupm&registry_uri=https://package.openupm.com&color=orange"></a>
    <a href="https://github.com/PurrNet/PurrNet/stargazers"><img alt="stars" src="https://img.shields.io/github/stars/PurrNet/PurrNet?color=orange"></a>
    <a href="https://discord.gg/HnNKdkq9ta"><img alt="discord" src="https://img.shields.io/discord/1288872904272121957?label=discord&color=orange"></a>
    <a href="https://assetstore.unity.com/packages/tools/network/purrnet-297320"><img alt="asset store" src="https://img.shields.io/badge/asset%20store-PurrNet-orange"></a>
    <a href="LICENSE"><img alt="license" src="https://img.shields.io/badge/license-MIT-orange"></a>
</p>

<p align="center">
    <a href="https://purrnet.dev/">Website</a> ·
    <a href="https://purrnet.dev/docs">Docs</a> ·
    <a href="https://discord.gg/HnNKdkq9ta">Discord</a> ·
    <a href="https://purrnet.dev/pricing">Support us</a> ·
    <a href="https://purrnet.dev/studios">For studios</a>
</p>

---

PurrNet is our attempt at the purrfect networking solution for Unity. The framework is **MIT licensed and open source**, with no seat licenses, no CCU tax and no revenue share. Ship a commercial game with it and we ask nothing in return.

You write Unity code. PurrNet makes it multiplayer.

```csharp
// This is a networked spawn. That's the whole API.
_player = Instantiate(playerPrefab);
```

No `NetworkServer.Spawn()`, no manual registration, no serializer boilerplate, no ID plumbing. PurrNet's IL post-processor generates all of it at compile time.

## Install

Requires Unity **2022** or newer. Install through Unity's Package Manager with one of these Git URLs:

**Release** (stable, recommended):

```bash
https://github.com/PurrNet/PurrNet.git?path=/Assets/PurrNet#release
```

**Dev** (more up to date, more prone to breaking changes):

```bash
https://github.com/PurrNet/PurrNet.git?path=/Assets/PurrNet#dev
```

Or via OpenUPM:

```bash
openupm add dev.purrnet.purrnet
```

You can also grab it from the [Asset Store](https://assetstore.unity.com/packages/tools/network/purrnet-297320), though those versions lag behind.

Coming from another solution? We have [migration guides](https://purrnet.dev/docs).

## Why PurrNet

[![IMAGE ALT TEXT HERE](https://img.youtube.com/vi/JJZY9cI2VqE/0.jpg)](https://www.youtube.com/watch?v=JJZY9cI2VqE)

|                              |                                                                                                          |
| ---------------------------- | -------------------------------------------------------------------------------------------------------- |
| **Unity-native spawning**    | `Instantiate()` and `Destroy()` just work. Drag prefabs into the scene and they spawn too.                 |
| **Zero-ceremony RPCs**       | Mark a method with an attribute. Static, generic, awaitable and coroutine RPCs all supported.               |
| **Network Rules**            | Per-object policy for who may spawn, despawn, own, observe and call. Server-strict or client-convenient, your call, no code changes. |
| **Network Modules**          | Compose networked behaviour out of nestable, reusable, generic modules. Every built-in feature is one.      |
| **Client-side prediction**   | [PurrDiction](https://purrnet.dev/docs) gives you rollback prediction with optional determinism.            |
| **Reconnection built in**    | Cookie-based identity so players come back to their own state instead of a fresh spawn.                    |
| **Real serialization**       | Compile-time generated packers, delta compression, and a hand-drivable `BitPacker` when you want the bytes. |
| **Cross-platform**           | Desktop, mobile, WebGL and consoles.                                                                       |

## Quick Introduction

### Transports

- UDP
- WebSockets
- Steam
- Unity Transport (UTP)
- Nakama
- Local (no socket)
- PurrTransport (our relay: free for development, and self-hostable for free if you'd rather run it yourself), with optional direct WebRTC P2P
- Composite (allows multiple transports at once)

There's also an Edgegap addon if you want managed server deployment.

### Spawning and Despawning

```csharp
[SerializeField] GameObject playerPrefab;

private GameObject _player;

void SpawnPlayer()
{
    _player = Instantiate(playerPrefab);
}

void DespawnPlayer()
{
    Destroy(_player);
}
```

Yes, you are done! PurrNet will handle the rest for you.
The best part is that if you want to allow flexibility over security, you can even have clients spawn and despawn their own objects depending on which NetworkRules you pick. With no changes to this code.

Bonus, you can also drag and drop prefabs into the scene and have them spawn automatically. As long as they have a NetworkIdentity component attached to them and are part of your NetworkPrefabs list.

### RPCs

You have `TargetRPC`s, `ServerRPC`s, and `ObserverRPC`s.
Depending on your network rules, these can all be called directly by clients too!

```csharp
[ServerRPC]
void DoSomethingOnServer()
{
    Debug.Log("Doing something on the server!");
}
```

Static RPCs are also supported.

```csharp
[ServerRPC]
static void DoSomethingOnServer()
{
    Debug.Log("Doing something on the server!");
}
```

Awaitable RPCs are also supported.

```csharp
[ServerRPC]
static Task<int> GetMyNumber()
{
    return Task.FromResult(42);
}
```

UniTask integration is also supported.

```csharp
[ServerRPC]
static UniTask<int> GetMyNumber()
{
    return UniTask.FromResult(42);
}
```

Why not Coroutine RPCs? We have that too!

```csharp
[ServerRPC]
static IEnumerator DoSomethingOnServer()
{
    yield return new WaitForSeconds(1);
    Debug.Log("Doing something on the server!");
}
```

Generic RPCs are also supported.

```csharp
[ServerRPC]
static void DoSomethingOnServer<T>(T value)
{
    Debug.Log($"Doing something on the server with {value}!");
}
```

All of these can be combined. For example, you can have a static RPC that returns a value and is awaitable and generic.

### Network Modules

Network Modules are a way to extend PurrNet with your own custom logic.
SyncVars are built using Network Modules, and you can create your own Network Modules to add custom logic to your networked objects.
This opens up a whole new world of possibilities for modularity and extensibility.

You can also nest these modules inside each other.
So for this next example we could have used a `SyncVar<int>` (another `NetworkModule`) but for demonstration purposes we won't.

```csharp
[Serializable]
public class PlayerHealthModule : NetworkModule
{
    [SerializeField] int _health;
    
    [ServerRPC(requireOwnership: true)]
    public void TakeDamage(int damage)
    {
        _health -= damage;
    }
}
```

The example above shows a simple health module that can be attached to any networked object.
Note that any of the mentioned RPCs can be used in Network Modules.

Here is how you would use it:

```csharp
class SomeIdentity : NetworkIdentity
{
    [SerializeField] PlayerHealthModule _healthModule;
    
    void TakeDamage(int damage)
    {
        _healthModule.TakeDamage(damage);
    }
}
```

This is just a simple example, but you can create much more complex modules with multiple RPCs and SyncVars.
All our built-in features are implemented using Network Modules, so you can be sure that they are powerful and flexible.

Don't forget they can also be generic!

Out of the box you get `SyncVar`, `SyncList`, `SyncDictionary`, `SyncHashSet`, `SyncArray`, `SyncQueue`, `SyncEvent`, `SyncTimer`, `SyncInput`, `SyncAsset`, `SyncFile` and more.

### Network Rules

Network Rules are a way to define how your networked objects behave.
You can define who can spawn, despawn, and call RPCs on your objects.
You can also define who can observe your objects and how they are synchronized.
Almost everything is customizable, and every object can have its own set of rules.

![image](https://github.com/user-attachments/assets/aa702bc4-ad6b-4cd4-841b-700d21f28d3e)

### Plug and play components

Drop these on a GameObject and they synchronize themselves:

`NetworkTransform` · `NetworkRigidbody` · `NetworkAnimator` · `NetworkAudioSource` · `NetworkBones` · `NetworkStateMachine` · `NetworkVisibility` · `NetworkOwnershipToggle` · `NetworkServerToggle` · `ColliderRollback` · `NetworkLOD` . and more.

### Serialization

PurrNet uses a custom serialization system that is both fast and flexible.
I will keep this short as you shouldn't have to worry about it.

Just want to mention some of the features:

```csharp
// sending an RPC with an object
// PurrNet will automatically serialize it for you and resolve it's type
[ServerRPC]
void DoSomethingOnServer(object someValue)
{
    Debug.Log($"Doing something on the server with {someValue}!");
}
```

You can also use the BitPacker directly if you want to send custom data.
This avoids creating garbage and is much faster than using the object serialization.
It also allows you to send data that might not be able to be represented by a type.

```csharp
void SendSomething()
{
    using var writer = BitPackerPool.Get();
    
    writer.Write(42);
    writer.Write("Hello, World!");
    
    DoSomethingOnServer(writer);
}

[ServerRPC]
void DoSomethingOnServer(BitPacker data)
{
    int value = default;
    string message = default;
    
    Packer<int>.Read(data, ref value);
    Packer<string>.Read(data, ref message);
    
    Debug.Log($"Doing something on the server with {value} and '{message}'!");
    
    data.Dispose();
}
```

If you use Unity.Mathematics, the Mathematics addon adds packers for its types so `float3`, `quaternion` and friends serialize just as efficiently as the built-in ones.

## Support PurrNet

The framework is MIT and stays that way. Memberships are how people who want it to keep getting better help pay for that, and they get early access to new features while they're at it.

| Tier | Price | What you get |
| ---- | ----- | ------------ |
| 💝 **One-time donation** | Pay what you want | Donator Discord role |
| 🐱 **House Cat** | $20/mo | Early access to features, supporter channels, House Cat Discord role |
| 👑 **Royal British** | $100/mo | Everything above, plus hands-on project support and eternal gratitude |

[**Become a member →**](https://purrnet.dev/pricing)

## For studios

If you're shipping a multiplayer game and netcode is on the critical path, we'll work inside your project, not from a ticket queue.

Studio plans start at **$500/mo** and cover:

- Priority support with direct access to the team
- Hands-on project access for debugging your netcode
- Architecture reviews and migrations from other solutions
- Custom features and studio-exclusive packages

Scope and price are set per team, so talk to us before assuming it does or doesn't fit.

Already trusted by studios shipping real games: Scythe Studios, Resolute Games and others.

[**Talk to us about a studio plan →**](https://purrnet.dev/studios)

## Community

The fastest place to get an answer.

<a href="https://discord.gg/HnNKdkq9ta" target="_blank">
    <img src="https://discord.com/api/guilds/1288872904272121957/widget.png?style=banner2" alt="Discord Banner">
</a>

## Links

- Website: https://purrnet.dev/
- Docs: https://purrnet.dev/docs
- Docs source: https://github.com/PurrNet/PurrDocs
- Changelog: [CHANGELOG.md](Assets/PurrNet/CHANGELOG.md)

## License

MIT. See [LICENSE](LICENSE).
