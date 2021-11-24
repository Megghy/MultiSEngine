using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace MultiSEngine
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Logs.Init();
            AutoInit();
            while (!(Core.Command.HandleCommand(null, Console.ReadLine(), out var c, true) && !c))
                Thread.Sleep(1);
            Close();
            Console.WriteLine("Bye!");
            Task.Delay(1000).Wait();
        }
        public static void AutoInit()
        {
#if DEBUG
            Logs.Warn($"> MultiSEngine IS IN DEBUG MODE <");
#endif
            Logs.Info("Initializing the program...");
            Assembly.GetExecutingAssembly()
                        .GetTypes()
                        .ForEach(t => t.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                        .Where(m => m.GetCustomAttribute<AutoInitAttribute>() is { }).ForEach(m =>
                        {
                            try
                            {
                                var a = m.GetCustomAttribute<AutoInitAttribute>();
                                if (a.PreInitMessage is { } pre)
                                    Logs.Info(pre);
                                m.Invoke(null, null);
                                if (a.PostInitMessage is { } post)
                                    Logs.Info(post);
                            }
                            catch (Exception ex)
                            {
                                Logs.Error($"An error occurred while initilizing: [{m.DeclaringType.Name}.{m.Name}]{Environment.NewLine}{ex}");
                            }
                        }));
            Logs.Success($"MultiSEngine startted.");
        }
        public static void Close()
        {
            Core.PluginSystem.Unload();
            Data.Clients.ToArray().ForEach(c => c.Disconnect("Server closed."));
            Logs.Info("Server closed." + Environment.NewLine);
        }
    }
}
