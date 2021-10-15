using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiSEngine
{
    public class Config
    {
        internal static Config _instance;
        public static Config Instance { get { _instance ??= Load(); return _instance; } }
        public static string ConfigPath => Path.Combine(Environment.CurrentDirectory, "Config.json");
        public static Config Load()
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath));
            else
            {
                var confg = new Config()
                {
                    SwitchToDefaultServerOnJoin = true,
                    DefaultServer = "yfeil",
                    Servers = new()
                    {
                        new()
                        {
                            Visible = true,
                            IP = "127.0.0.1",
                            Port = 7777,
                            Name = "yfeil",
                        }
                    }
                };
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(confg, new JsonSerializerOptions() { WriteIndented = true }));
                return confg;
            }
        }

        public string ListenIP { get; set; } = "127.0.0.1";
        public int ListenPort { get; set; } = 7778;
        public string ServerName { get; set; } = "MultiSEngine";
        public int SwitchTimeOut { get; set; } = 5000;
        public bool SwitchToDefaultServerOnJoin { get; set; } = false;
        public bool RestoreDataWhenJoinNonSSC { get; set; } = true;
        [JsonIgnore]
        public Modules.DataStruct.ServerInfo DefaultServerInternal => Servers.FirstOrDefault(s => s.Name == DefaultServer);
        public string DefaultServer { get; set; } = string.Empty;
        public List<Modules.DataStruct.ServerInfo> Servers { get; set; } = new();
    }
}
