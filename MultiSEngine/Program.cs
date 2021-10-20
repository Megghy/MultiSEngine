using System;
using System.Linq;
using System.Threading.Tasks;

namespace MultiSEngine
{
    internal class Program
    {
        public const bool DEBUG = true;
        static void Main(string[] args)
        {
            Init();
            while (!(Core.Command.HandleCommand(null, Console.ReadLine(), out var c, true) && !c))
                Task.Delay(1).Wait();
            Console.WriteLine("Bye!");
            Task.Delay(1000).Wait();
            Close();
        }
        public static void Init()
        {
            Logs.Info("Initializing the program...");
            Logs.Info("Loading all plugins");
            Core.PluginSystem.Load();
            Logs.Info($"{Core.PluginSystem.PluginList.Count} Plugin(s) loaded.");
            Modules.Data.Init();
            Modules.ConsoleManager.Init();
            Core.DataBridge.Init();
            Logs.Info($"Loaded all data.");
            Core.Command.InitAllCommands();
            Logs.Info($"Registered all commands.");
            Core.Net.Instance.Init(Config.Instance.ListenIP, Config.Instance.ListenPort);
            Logs.Info($"Opened socket server successfully, listening to port {Config.Instance.ListenPort}");
            Logs.Success($"MultiSEngine startted.");
            Console.WriteLine("-----------------------------------------------------------------------------");
        }
        public static void Close()
        {

        }
    }
}
