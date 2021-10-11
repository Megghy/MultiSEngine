using System.Text.Json;

namespace MultiSEngine
{
    internal class Localization
    {
        public static JsonDocument JsonData;
        public static string Get(string key)
        {
            try
            {
                return JsonData?.RootElement.GetProperty(key).GetString();
            }
            catch
            {
                return "Unknown";
            }
        }
    }
}
