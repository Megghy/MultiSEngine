using System;
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
            Modules.Data.Init();
            Modules.ConsoleManager.Init();
            Core.DataBridge.Init();
            Logs.Success($"Loaded all data.");
            Core.Command.InitAllCommands();
            Logs.Success($"Registered all commands.");
            Core.Net.Instance.Init(Config.Instance.ListenIP, Config.Instance.ListenPort);
            Logs.Success($"Opened socket server successfully, listening to port {Config.Instance.ListenPort}");
            Console.WriteLine("-----------------------------------------------------------------------------");
        }
        public static void Close()
        {

        }
    }
}
