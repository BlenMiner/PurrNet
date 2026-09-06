using System.Collections.Generic;

namespace PurrNet.Edgegap.Runtime
{
    public class PortMappingData
    {
        public string name { get; private set; }

        public int internalPort { get; private set; }

        public int externalPort { get; private set; }

        public string protocol { get; private set; }

        internal static PortMappingData Parse(Dictionary<string, object> json)
        {
            return new PortMappingData
            {
                name = EdgegapJson.GetString(json, "name"),
                internalPort = EdgegapJson.GetInt(json, "internal"),
                externalPort = EdgegapJson.GetInt(json, "external"),
                protocol = EdgegapJson.GetString(json, "protocol")
            };
        }
    }
}
