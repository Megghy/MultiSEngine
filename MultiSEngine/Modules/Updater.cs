using System.Reflection;
using System.Timers;
using MultiSEngine.DataStruct;
using Timer = System.Timers.Timer;

namespace MultiSEngine.Modules
{
    class Updater
    {
        public const string UpdateURL = "https://api.suki.club/v1/myapi/version?program=multisengine";
        private static readonly HttpClient httpClient = new()
        {
            Timeout = new(0, 0, 0, 5)
        };
        [AutoInit]
        public static void Init()
        {
            UpdateTimer.Elapsed += CheckUpdate;
            UpdateTimer.Start();
            CheckUpdate(null, null);
        }
        private static readonly Timer UpdateTimer = new()
        {
            Interval = 1000 * 60 * 5,
            AutoReset = true
        };
        internal static async void CheckUpdate(object sender, ElapsedEventArgs e)
        {
            try
            {
                var version = await GetNewestVersion();
                if (version > Assembly.GetExecutingAssembly().GetName().Version)
                    Logs.LogAndSave($"New version found: {version}, please download at [https://github.com/Megghy/MultiSEngine/releases] or [https://github.com/Megghy/MultiSEngine/actions].", "[Updater]", ConsoleColor.DarkYellow);
            }
            catch { }
        }
        internal static async Task<Version> GetNewestVersion()
        {
            return Version.TryParse(await httpClient.GetStringAsync(UpdateURL), out var version)
                ? version
                : new(0, 0);
        }
    }
}
