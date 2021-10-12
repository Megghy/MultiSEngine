using System;
using System.Threading.Tasks;

namespace MultiSEngine
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Init();
            while (Console.ReadLine() is not "exit" or "stop")
                Task.Delay(1).Wait();
            Console.WriteLine("Bye!");
            Task.Delay(1000).Wait();
            Close();
        }
        public static void Init()
        {
            Logs.Info("Initializing the program...");
            Modules.Data.Init();
            Logs.Success($"Loaded all data.");
            Core.Command.InitAllCommands();
            Logs.Success($"Registered all commands.");
            Core.Net.Instance.Init(Config.Instance.ListenIP, Config.Instance.ListenPort);
            Logs.Success($"Opened socket server successfully, listening to port {Config.Instance.ListenPort}");
        }
        public static void Close()
        {

        }
    }
}
