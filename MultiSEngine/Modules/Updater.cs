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
            _updaterTask = RunUpdaterLoopAsync();
        }
        private static Task _updaterTask;
        private static async Task RunUpdaterLoopAsync()
        {
            // 立即检查一次
            await SafeCheckOnceAsync().ConfigureAwait(false);
            // 周期性检查
            while (true)
            {
                try { await Task.Delay(TimeSpan.FromMinutes(5)).ConfigureAwait(false); }
                catch { }
                await SafeCheckOnceAsync().ConfigureAwait(false);
            }
        }
        private static async Task SafeCheckOnceAsync()
        {
            try
            {
                var version = await GetNewestVersion().ConfigureAwait(false);
                if (version > Assembly.GetExecutingAssembly().GetName().Version)
                    Logs.LogAndSave($"New version found: {version}, please download at [https://github.com/Megghy/MultiSEngine/releases] or [https://github.com/Megghy/MultiSEngine/actions].", "[Updater]", ConsoleColor.DarkYellow);
            }
            catch { }
        }
        internal static async Task<Version> GetNewestVersion()
        {
            return Version.TryParse(await httpClient.GetStringAsync(UpdateURL).ConfigureAwait(false), out var version)
                ? version
                : new(0, 0);
        }
    }
}
