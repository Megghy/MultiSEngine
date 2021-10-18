using System;
using System.IO;
using System.Text.Json;

namespace MultiSEngine
{
    internal class Localization
    {
        public static string LocalizationPath => Path.Combine(Environment.CurrentDirectory, "Localization.json");
        private static JsonDocument _jsonData;
        public static JsonDocument JsonData
        {
            get
            {
                if (!File.Exists(LocalizationPath))
                    File.WriteAllText(LocalizationPath, Properties.Resources.DefaultLocallization);
                _jsonData ??= JsonSerializer.Deserialize<JsonDocument>(File.ReadAllText(LocalizationPath));
                return _jsonData;
            }
        }
        public static string Get(string key, object[] obj = null)
        {
            try
            {
                return string.Format(JsonData?.RootElement.GetProperty(key).GetString(), obj);
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
