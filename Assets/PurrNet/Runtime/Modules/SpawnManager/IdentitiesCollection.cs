using System.Collections.Generic;

namespace PurrNet
{
    public class IdentitiesCollection
    {
        readonly Dictionary<int, NetworkIdentity> _identities = new ();
        
        private int _nextId = 0;
        
        public bool TryGetIdentity(int id, out NetworkIdentity identity)
        {
            return _identities.TryGetValue(id, out identity);
        }
        
        public void RegisterIdentity(NetworkIdentity identity)
        {
            _identities.Add(identity.id, identity);
        }
        
        public bool UnregisterIdentity(NetworkIdentity identity)
        {
            return _identities.Remove(identity.id);
        }

        public int GetNextId()
        {
            return _nextId++;
        }
    }
}