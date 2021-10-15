using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new Config()
                {
                    MainServer = new()
                    {
                        IP = "127.0.0.1",
                        Port = 7777,
                        Name = "main",
                    },
                    Servers = new()
                }, new JsonSerializerOptions() { WriteIndented = true }));
            return new();
        }

        public string ListenIP { get; set; } = "127.0.0.1";
        public int ListenPort { get; set; } = 7778;
        public string ServerName { get; set; } = "MultiSEngine";
        public int SwitchTimeOut { get; set; } = 5000;
        public bool SwitchToMainServerOnJoin { get; set; } = false;
        public bool RestoreDataWhenJoinNonSSC { get; set; } = true;
        public Modules.DataStruct.ServerInfo MainServer { get; set; }
        public List<Modules.DataStruct.ServerInfo> Servers { get; set; }
    }
}
