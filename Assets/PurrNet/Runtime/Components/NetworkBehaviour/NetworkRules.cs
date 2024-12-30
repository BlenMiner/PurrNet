using System;
using JetBrains.Annotations;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    [Serializable]
    public struct EditorRules
    {
        [UsedImplicitly]
        public bool stopPlayingOnDisconnect;
    }
    
    [Serializable]
    public struct VisibilityRules
    {
        [UsedImplicitly]
        public VisibilityMode visibilityMode;
    }
    
    [Serializable]
    public struct RpcRules
    {
        [UsedImplicitly]
        [Tooltip("This allows client to call any ObserversRpc or TargetRpc without the need to set requireServer to false")]
        public bool ignoreRequireServerAttribute;

        [UsedImplicitly]
        [Tooltip("This allows client to call any OwnerRpc without the need to set requireOwner to false")]
        public bool ignoreRequireOwnerAttribute;
    }
    
    [Serializable]
    public struct SpawnRules
    {
        public ConnectionAuth spawnAuth;
        public ActionAuth despawnAuth;

        [Tooltip("Who gains ownership upon spawning of the identity")]
        public DefaultOwner defaultOwner;

        [Tooltip("Propagate ownership to all children of the object")]
        public bool propagateOwnershipByDefault;

        [Tooltip("If owner disconnects, should the object despawn or stay in the scene?")]
        public bool despawnIfOwnerDisconnects;
    }

    [Serializable]
    public struct OwnershipRules
    {
        [Tooltip("Who can assign ownership to objects")]
        public ConnectionAuth assignAuth;
        
        [Tooltip("Who can transfer existing ownership from objects")]
        public ActionAuth transferAuth;
        
        [Tooltip("Who can remove ownership from objects")]
        public ActionAuth removeAuth;
        
        [Tooltip("If object already has an owner, should the new owner override the existing owner?")]
        public bool overrideWhenPropagating;
    }
    
    [Serializable]
    public struct NetworkSceneRules
    {
        public bool removePlayerFromSceneOnDisconnect;
    }

    [Serializable]
    public struct NetworkIdentityRules
    {
        public bool syncComponentActive;
        public ActionAuth syncComponentAuth;

        public bool syncGameObjectActive;
        public ActionAuth syncGameObjectActiveAuth;
        
        public bool receiveRpcsWhenDisabled;
    }

    [Serializable]
    public struct NetworkTransformRules
    {
        public bool syncParent;
        public ActionAuth changeParentAuth;
    }
    
    [CreateAssetMenu(fileName = "NetworkRules", menuName = "PurrNet/Network Rules", order = -201)]
    public class NetworkRules : ScriptableObject
    {
        [SerializeField] private EditorRules _editorRules = new EditorRules()
        {
            stopPlayingOnDisconnect = true
        };
        
        [SerializeField] private SpawnRules _defaultSpawnRules = new SpawnRules()
        {
            despawnAuth = ActionAuth.Server | ActionAuth.Owner,
            spawnAuth = ConnectionAuth.Server,
            defaultOwner = DefaultOwner.SpawnerIfClientOnly,
            propagateOwnershipByDefault = true,
            despawnIfOwnerDisconnects = true
        };
        
        [SerializeField] private RpcRules _defaultRpcRules = new RpcRules()
        {
            ignoreRequireServerAttribute = false,
            ignoreRequireOwnerAttribute = false
        };
        
        [PurrReadOnly]
        [SerializeField] private VisibilityRules _defaultVisibilityRules = new VisibilityRules()
        {
            visibilityMode = VisibilityMode.SpawnDespawn
        };
        
        [SerializeField] private OwnershipRules _defaultOwnershipRules = new OwnershipRules()
        {
            assignAuth = ConnectionAuth.Server,
            transferAuth = ActionAuth.Owner | ActionAuth.Server,
            overrideWhenPropagating = true
        };
        
        [SerializeField] private NetworkSceneRules _defaultSceneRules = new NetworkSceneRules()
        {
            removePlayerFromSceneOnDisconnect = false
        };
        
        [SerializeField] private NetworkIdentityRules _defaultIdentityRules = new NetworkIdentityRules()
        {
            syncComponentActive = true,
            syncComponentAuth = ActionAuth.Server | ActionAuth.Owner,
            syncGameObjectActive = true,
            syncGameObjectActiveAuth = ActionAuth.Server | ActionAuth.Owner,
            receiveRpcsWhenDisabled = true
        };
        
        [SerializeField] private NetworkTransformRules _defaultTransformRules = new NetworkTransformRules()
        {
            changeParentAuth = ActionAuth.Server | ActionAuth.Owner,
            syncParent = true
        };

        public bool HasDespawnAuthority(NetworkIdentity identity, PlayerID player, bool asServer)
        {
            return HasAuthority(_defaultSpawnRules.despawnAuth, identity, player, asServer);
        }
        
        [UsedImplicitly]
        public bool HasSpawnAuthority(NetworkManager manager, bool asServer)
        {
            return HasAuthority(_defaultSpawnRules.spawnAuth, asServer);
        }
        
        public bool HasSetActiveAuthority(NetworkIdentity identity, PlayerID? player, bool asServer)
        {
            return HasAuthority(_defaultIdentityRules.syncGameObjectActiveAuth, identity, player, asServer);
        }
        
        public bool HasSetEnabledAuthority(NetworkIdentity identity, PlayerID? player, bool asServer)
        {
            return HasAuthority(_defaultIdentityRules.syncComponentAuth, identity, player, asServer);
        }
        
        [UsedImplicitly]
        public bool ShouldSyncParent(NetworkIdentity identity, bool asServer)
        {
            return _defaultTransformRules.syncParent;
        }
        
        [UsedImplicitly]
        public bool ShouldSyncSetActive(NetworkIdentity identity, bool asServer)
        {
            return _defaultIdentityRules.syncGameObjectActive;
        }
        
        [UsedImplicitly]
        public bool ShouldSyncSetEnabled(NetworkIdentity identity, bool asServer)
        {
            return _defaultIdentityRules.syncComponentActive;
        }
        
        [UsedImplicitly]
        public bool HasPropagateOwnershipAuthority(NetworkIdentity identity, bool asServer)
        {
            return true;
        }
        
        public bool HasChangeParentAuthority(NetworkIdentity identity, PlayerID? player, bool asServer)
        {
            return HasAuthority(_defaultTransformRules.changeParentAuth, identity, player, asServer);
        }
        
        static bool HasAuthority(ConnectionAuth connAuth, bool asServer)
        {
            return connAuth == ConnectionAuth.Everyone || asServer;
        }
        
        static bool HasAuthority(ActionAuth action, NetworkIdentity identity, PlayerID? player, bool asServer)
        {
            if (action.HasFlag(ActionAuth.Server) && asServer)
                return true;
            
            if (action.HasFlag(ActionAuth.Owner) && player.HasValue && identity.owner == player)
                return true;
            
            return identity.owner != player && action.HasFlag(ActionAuth.Observer);
        }
        
        public bool HasTransferOwnershipAuthority(NetworkIdentity networkIdentity, PlayerID? localPlayer, bool asServer)
        {
            return HasAuthority(_defaultOwnershipRules.transferAuth, networkIdentity, localPlayer, asServer);
        }

        public bool HasGiveOwnershipAuthority(NetworkIdentity networkIdentity, bool asServer)
        {
            return HasAuthority(_defaultOwnershipRules.assignAuth, asServer);
        }
        
        public bool HasRemoveOwnershipAuthority(NetworkIdentity networkIdentity, PlayerID? localPlayer, bool asServer)
        {
            return HasAuthority(_defaultOwnershipRules.removeAuth, networkIdentity, localPlayer, asServer);
        }
        
        public bool ShouldPropagateToChildren()
        {
            return _defaultSpawnRules.propagateOwnershipByDefault;
        }

        public bool ShouldOverrideExistingOwnership(NetworkIdentity networkIdentity, bool asServer)
        {
            return _defaultOwnershipRules.overrideWhenPropagating;
        }

        public bool ShouldRemovePlayerFromSceneOnLeave()
        {
            return _defaultSceneRules.removePlayerFromSceneOnDisconnect;
        }
        
        public bool ShouldDespawnOnOwnerDisconnect()
        {
            return _defaultSpawnRules.despawnIfOwnerDisconnects;
        }

        public bool ShouldClientGiveOwnershipOnSpawn()
        {
            return _defaultSpawnRules.defaultOwner == DefaultOwner.SpawnerIfClientOnly;
        }

        public bool ShouldPlayRPCsWhenDisabled()
        {
            return _defaultIdentityRules.receiveRpcsWhenDisabled;
        }

        public bool ShouldIgnoreRequireServer()
        {
            return _defaultRpcRules.ignoreRequireServerAttribute;
        }
        
        public bool ShouldIgnoreRequireOwner()
        {
            return _defaultRpcRules.ignoreRequireOwnerAttribute;
        }

        public bool ShouldStopPlayingOnDisconnect()
        {
            return _editorRules.stopPlayingOnDisconnect;
        }
    }
}