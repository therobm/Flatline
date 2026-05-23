using System;
using System.IO;
using System.Net;
using System.Text.Json;
using Flatline.Http;
using Flatline.Logging;

namespace Flatline
{
    public class FlatlineConfig
    {
        public int HttpPort;
        public int HttpsPort;
        public string BindAddress;

        public static FlatlineConfig LoadOrDefault(string configPath)
        {
            FlatlineConfig config = new FlatlineConfig();
            config.HttpPort = 5099;
            config.HttpsPort = 5443;
            config.BindAddress = "0.0.0.0";

            if (!File.Exists(configPath))
            {
                Log.Info("No flatline.json found at " + configPath + "; using defaults.");
                return config;
            }

            try
            {
                string jsonText = File.ReadAllText(configPath);
                FlatlineConfig parsed = JsonSerializer.Deserialize<FlatlineConfig>(jsonText, JsonOptions.Default);
                if (parsed != null)
                {
                    if (parsed.HttpPort > 0)
                    {
                        config.HttpPort = parsed.HttpPort;
                    }
                    if (parsed.HttpsPort > 0)
                    {
                        config.HttpsPort = parsed.HttpsPort;
                    }
                    if (!string.IsNullOrEmpty(parsed.BindAddress))
                    {
                        config.BindAddress = parsed.BindAddress;
                    }
                }
                Log.Info("Loaded config from " + configPath);
            }
            catch (Exception loadException)
            {
                Log.Warning("Failed to read " + configPath + "; using defaults: " + loadException.Message);
            }

            return config;
        }

        public IPAddress ResolveBindAddress()
        {
            IPAddress parsed;
            if (IPAddress.TryParse(BindAddress, out parsed))
            {
                return parsed;
            }
            Log.Warning("Could not parse BindAddress '" + BindAddress + "'; falling back to 0.0.0.0.");
            return IPAddress.Any;
        }
    }
}
