using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace MultiSEngine
{
    public class Config
    {
        public static readonly JsonSerializerOptions DefaultSerializerOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        };
        internal static Config _instance;
        public static Config Instance { get { _instance ??= Load(); return _instance; } }
        public static string ConfigPath => Path.Combine(Environment.CurrentDirectory, "Config.json");
        public static Config Load()
        {
            if (File.Exists(ConfigPath))
            {
                var config = CheckConfig(JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath)));
                config.Save();
                return config;
            }
            else
            {
                var config = new Config()
                {
                    SwitchToDefaultServerOnJoin = true,
                    DefaultServer = "yfeil",
                    Servers = new()
                    {
                        new()
                        {
                            Visible = true,
                            IP = "yfeil.top",
                            Port = 7777,
                            Name = "yfeil",
                        }
                    }
                };
                config.Save();
                return config;
            }
        }
        public static Config CheckConfig(Config config)
        {
            for (int i = 0; i < config.Servers.Count; i++)
            {
                var c = config.Servers[i];
                var sameNames = config.Servers.Where(s => s.Name == c.Name).ToList();
                if (sameNames.Count > 1)
                {
                    sameNames.RemoveAt(0);
                    sameNames.ForEach(s => config.Servers.Remove(s));
                    Logs.Warn($"A server with the same name was found in the config file: [{c.Name}], redundant items have been removed");
                }
            }
            if (config.Servers.Where(s => string.IsNullOrEmpty(s.Name)).ToArray() is { Length: > 0 } emptyNames)
            {
                emptyNames.ForEach(s => config.Servers.Remove(s));
                Logs.Warn($"Found [{emptyNames.Length}] servers with empty names in the configuration file, removed");
            }
            config.Servers.Where(s => Modules.Data.Convert(config.ServerVersion) == "Unknown")
                .ForEach(s => Logs.Warn($"The server [{s.Name}] specifies an unknown ServerVersion, which may cause some problems."));
            return config;
        }
        public void Save()
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, DefaultSerializerOptions));
        }
        public string ListenIP { get; set; } = "0.0.0.0";
        public int ListenPort { get; set; } = 7778;
        public string ServerName { get; set; } = "MultiSEngine";
        public int ServerVersion { get; set; } = 244;
        public string Token { get; set; } = "114514";
        public int SwitchTimeOut { get; set; } = 5000;
        public bool EnableCrossplayFeature { get; set; } = false;
        public bool EnableChatForward { get; set; } = true;
        public bool SwitchToDefaultServerOnJoin { get; set; } = false;
        public bool RestoreDataWhenJoinNonSSC { get; set; } = true;
        [JsonIgnore]
        public DataStruct.ServerInfo DefaultServerInternal => Servers.FirstOrDefault(s => s.Name == DefaultServer);
        public string DefaultServer { get; set; } = string.Empty;
        public List<DataStruct.ServerInfo> Servers { get; set; } = new();
    }
}
