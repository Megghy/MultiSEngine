using System.Reflection;
using System.Timers;
using MultiSEngine.DataStruct;
using Timer = System.Timers.Timer;

namespace MultiSEngine.Modules
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
            Console.Title = $"{Title}  {Data.Clients.Count} Online @{Config.Instance.ListenIP}:{Config.Instance.ListenPort} <V{Assembly.GetExecutingAssembly().GetName().Version}, for {Data.Convert(Config.Instance.ServerVersion)}{(Config.Instance.EnableCrossplayFeature ? " + Crossplay" : "")}>";
            Task.Delay(1000).Wait();
        }
    }
}
