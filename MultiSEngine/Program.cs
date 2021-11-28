using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using System;
using System.Collections.Generic;
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
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
            var auto = new Dictionary<MethodInfo, AutoInitAttribute>();
            Assembly.GetExecutingAssembly()
                        .GetTypes()
                        .ForEach(t => t.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                        .ForEach(m =>
                        {
                            if (m.GetCustomAttribute<AutoInitAttribute>() is { } attr)
                                auto.Add(m, attr);
                        }));
            auto.OrderBy(a => a.Value.Order).ForEach(kv =>
            {
                try
                {
#if DEBUG
                    Console.WriteLine($"[{kv.Key.DeclaringType.Name}.{kv.Key.Name}] => Initialzing");
#endif
                    var attr = kv.Value;
                    if (attr.PreInitMessage is { } pre)
                        Logs.Info(pre);
                    kv.Key.Invoke(null, null);
                    if (attr.PostInitMessage is { } post)
                        Logs.Info(post);
                }
                catch (Exception ex)
                {
                    Logs.Error($"An error occurred while initilizing: [{kv.Key.DeclaringType.Name}.{kv.Key.Name}]{Environment.NewLine}{ex}");
                }
            });
            Logs.Success($"MultiSEngine startted.");
        }
        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Logs.Error(e.ExceptionObject as Exception is { } error ? string.Format("Application UnhandledException:{0};\nStackTrace:{1}", error.Message, error.StackTrace) : string.Format("Application UnhandledError:{0}", e));
        }
        public static void Close()
        {
            Core.PluginSystem.Unload();
            Data.Clients.ToArray().ForEach(c => c.Disconnect("Server closed."));
            Logs.Info("Server closed." + Environment.NewLine);
        }
    }
}
