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
            Close();
        }
        public static void Init()
        {
            Logs.Info("Initializing the program...");
            Core.Net.Instance.Init(Config.Instance.ListenIP, Config.Instance.ListenPort);
            Logs.Success($"Opened socket server successfully, listening to port {Config.Instance.ListenPort}");
        }
        public static void Close()
        {

        }
    }
}
