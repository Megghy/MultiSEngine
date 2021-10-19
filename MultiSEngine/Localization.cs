using System;
using System.IO;
using System.Text.Json;

namespace MultiSEngine
{
    internal class Localization
    {
        internal static Localization _instance;
        public static Localization Instance { get { _instance ??= new(); return _instance; } }
        public static string LocalizationPath => Path.Combine(Environment.CurrentDirectory, "Localization.json");
        internal JsonDocument _jsonData;
        public JsonDocument JsonData
        {
            get
            {
                if (!File.Exists(LocalizationPath))
                    File.WriteAllText(LocalizationPath, Properties.Resources.DefaultLocallization);
                _jsonData ??= JsonSerializer.Deserialize<JsonDocument>(File.ReadAllText(LocalizationPath));
                return _jsonData;
            }
        }
        public string this[string key] { get { return Get(key); } }
        public string this[string key, object[] obj] { get { return Get(key, obj); } }
        public string this[string key, string arg1] { get { return Get(key, new[] {arg1}); } }
        public string this[string key, string arg1, string arg2] { get { return Get(key, new[] { arg1, arg2 }); } }
        public string this[string key, string arg1, string arg2, string arg3] { get { return Get(key, new[] { arg1, arg2, arg3 }); } }
        public string this[string key, string arg1, string arg2, string arg3, string arg4] { get { return Get(key, new[] { arg1, arg2, arg3, arg4 }); } }
        public static string Get(string key, object[] obj = null)
        {
            try
            {
                return obj is null ? Instance.JsonData?.RootElement.GetProperty(key).GetString() : string.Format(Instance.JsonData?.RootElement.GetProperty(key).GetString(), obj);
            }
            catch (JsonException) { return key; }
            catch (Exception ex)
            {
                Logs.Error(ex);
                return key;
            }
        }
    }
}
