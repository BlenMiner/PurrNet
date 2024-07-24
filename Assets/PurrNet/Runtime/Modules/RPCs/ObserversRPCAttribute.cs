using System;
using PurrNet.Transports;

namespace PurrNet
{
    public class ObserversRPCAttribute : Attribute
    {
        public ObserversRPCAttribute() { }
        
        public ObserversRPCAttribute(Channel channel = Channel.ReliableOrdered, bool runLocally = false, bool bufferLast = false, bool requireServer = true) { }
    }
}
