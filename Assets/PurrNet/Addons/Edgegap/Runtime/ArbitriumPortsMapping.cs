using System.Collections.Generic;

namespace PurrNet.Edgegap.Runtime
{
    public class ArbitriumPortsMapping
    {
        public Dictionary<string, PortMappingData> ports { get; private set; }

        internal static ArbitriumPortsMapping Parse(string json)
        {
            var result = new ArbitriumPortsMapping { ports = new Dictionary<string, PortMappingData>() };
            if (EdgegapJson.Parse(json) is Dictionary<string, object> root &&
                root.TryGetValue("ports", out var portsValue) && portsValue is Dictionary<string, object> ports)
            {
                foreach (var pair in ports)
                {
                    if (pair.Value is Dictionary<string, object> entry)
                        result.ports[pair.Key] = PortMappingData.Parse(entry);
                }
            }

            return result;
        }
    }
}
