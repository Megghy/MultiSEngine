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
        public string this[string key, params string[] args] { get { return Get(key, args); } }
        public static string Get(string key, object[] obj = null)
        {
            try
            {
                return obj is null ? Instance.JsonData?.RootElement.GetProperty(key).GetString() : string.Format(Instance.JsonData?.RootElement.GetProperty(key).GetString(), obj);
            }
            catch
            {
                return key;
            }
        }
    }
}
