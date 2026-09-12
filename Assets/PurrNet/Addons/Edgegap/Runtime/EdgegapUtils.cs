using System;
using System.Collections;
using UnityEngine;

namespace PurrNet.Edgegap.Runtime
{
    public static class EdgegapUtils
    {
        public static EdgegapArbitrium GetArbitrium()
        {
            var res = new EdgegapArbitrium();
            var envs = Environment.GetEnvironmentVariables();

            foreach (DictionaryEntry envEntry in envs)
            {
                if (!envEntry.Key.ToString().Contains("ARBITRIUM_"))
                    continue;

                string envKey = envEntry.Key.ToString();
                string envValue = envEntry.Value.ToString();

                if (envs.Contains("ARBITRIUM_ENV_DEBUG"))
                    Debug.Log($"{envKey}: {envValue}");

                if (envKey.Contains("PORTS_MAPPING"))
                {
                    res.arbitriumPortsMapping = ArbitriumPortsMapping.Parse(envValue);
                    break;
                }
            }

            return res;
        }
    }
}
