using System.Reflection;
using System.Timers;
using Timer = System.Timers.Timer;

namespace MultiSEngine.Runtime
{
    internal class ConsoleManager
    {
        public static readonly string Title = "[MultiSEngine]";
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
            Console.Title = $"{Title}  {RuntimeState.Clients.Count} Online @{Config.Instance.ListenIP}:{Config.Instance.ListenPort} <V{Assembly.GetExecutingAssembly().GetName().Version}, for {RuntimeState.Convert(Config.Instance.ServerVersion)}{(Config.Instance.EnableCrossplayFeature ? " + Crossplay" : "")}>";
        }
    }
}


