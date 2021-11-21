using System;
using System.Reflection;
using System.Threading.Tasks;

namespace MultiSEngine.Modules
{
    internal class ConsoleManager
    {
        public static readonly string Title = "[MultiSEngine]";
        public static void Init()
        {
            Task.Run(Loop);
        }
        private static void Loop()
        {
            while (true)
            {
                Console.Title = $"{Title}  {Data.Clients.Count} Online <V{Assembly.GetExecutingAssembly().GetName().Version}, for Terraria-1.4.3>";
                Task.Delay(1000).Wait();
            }
        }
    }
}
