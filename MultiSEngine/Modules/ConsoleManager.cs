using MultiSEngine.DataStruct;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Timers;

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
            Console.Title = $"{Title}  {Data.Clients.Count} Online @{Config.Instance.ListenIP}:{Config.Instance.ListenPort} <V{Assembly.GetExecutingAssembly().GetName().Version}, for Terraria-1.4.3>";
            Task.Delay(1000).Wait();
        }
    }
}
