using System.Reflection;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;

namespace MultiSEngine
{
    internal class Program
    {
        // 使用异步 Main 以避免阻塞主线程，提升整体响应性
        static async Task Main(string[] args)
        {
            ShowLogo();
            AutoInit();
            while (true)
            {
                var line = await Console.In.ReadLineAsync();
                var (handled, continueSend) = await Core.Command.HandleCommand(null, line, true).ConfigureAwait(false);
                if (handled && !continueSend)
                    break;
            }
            await CloseAsync().ConfigureAwait(false);
            Console.WriteLine("Bye!");
        }
        private static void ShowLogo()
        {
            Console.WriteLine(@"
        __  ___      ____  _ _____ ______            _          
       /  |/  /_  __/ / /_(_) ___// ____/___  ____ _(_)___  ___ 
      / /|_/ / / / / / __/ /\__ \/ __/ / __ \/ __ `/ / __ \/ _ \
     / /  / / /_/ / / /_/ /___/ / /___/ / / / /_/ / / / / /  __/
    /_/  /_/\__,_/_/\__/_//____/_____/_/ /_/\__, /_/_/ /_/\___/ 
                                       /____/               
");
            Console.WriteLine($"-> V{Assembly.GetExecutingAssembly().GetName().Version}");
            Console.WriteLine();
        }
        private static void AutoInit()
        {
#if DEBUG
            Logs.Warn($"> MultiSEngine IS IN DEBUG MODE <");
#endif
            Logs.Init();
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
        public static async Task CloseAsync()
        {
            Core.PluginSystem.Unload();
            foreach (var c in Data.Clients.ToArray())
            {
                await c.DisconnectAsync("Server closed.").ConfigureAwait(false);
            }
            Logs.Info("Server closed." + Environment.NewLine);
        }
    }
}
