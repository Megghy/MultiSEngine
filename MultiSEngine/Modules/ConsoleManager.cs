using System;
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
                Console.Title = $"{Title} {Data.Clients.Count} Online";
                Task.Delay(1000).Wait();
            }
        }
    }
}
