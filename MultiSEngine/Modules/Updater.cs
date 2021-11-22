using MultiSEngine.DataStruct;
using System;
using System.Net.Http;
using System.Reflection;
using System.Timers;

namespace MultiSEngine.Modules
{
    class Updater
    {
        public const string UpdateURL = "https://api.suki.club/v1/myapi/version?program=multisengine";
        private static readonly HttpClient httpClient = new();
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
        private static void CheckUpdate(object sender, ElapsedEventArgs e)
        {
            try
            {
                var version = Version.Parse(httpClient.GetStringAsync(UpdateURL).Result);
                if (version > Assembly.GetExecutingAssembly().GetName().Version)
                    Logs.LogAndSave($"New version found: {version}, please go to [https://github.com/Megghy/MultiSEngine/releases] to download.", "[Updater]", ConsoleColor.DarkYellow);
            }
            catch { }
        }
    }
}
