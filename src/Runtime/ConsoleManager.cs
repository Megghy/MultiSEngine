using System.Reflection;
using System.Timers;
using Timer = System.Timers.Timer;

namespace MultiSEngine.Runtime
{
    internal class ConsoleManager
    {
        public static readonly string Title = "[MultiSEngine]";
        public static readonly string Version = $"v{Assembly.GetExecutingAssembly().GetName().Version}";
        private static readonly Timer ConsoleUpdateTimer = new()
        {
            Interval = 1000,
            AutoReset = true
        };
        [AutoInit]
        public static void Init()
        {
            ConsoleUpdateTimer.Elapsed += Loop;
            ConsoleUpdateTimer.Start();
        }
        private static void Loop(object sender, ElapsedEventArgs e)
        {
            Console.Title = $"{Title}  {RuntimeState.ClientRegistry.Count} Online @{Config.Instance.ListenIP}:{Config.Instance.ListenPort} <{Version}>, for {RuntimeState.Convert(Config.Instance.ServerVersion)}{(Config.Instance.EnableCrossplayFeature ? " + Crossplay" : "")}>";
        }
    }
}


