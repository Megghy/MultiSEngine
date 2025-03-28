using System.Collections.Concurrent;
using MultiSEngine.DataStruct;

namespace MultiSEngine
{
    public class Logs
    {
        public static string LogPath => Path.Combine(Environment.CurrentDirectory, "Logs");
        public static string LogName => Path.Combine(LogPath, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
        public const ConsoleColor DefaultColor = ConsoleColor.Gray;
        public static void Text(object text, bool save = true)
        {
            LogAndSave(text, "[Log]", DefaultColor, save);
        }
        public static void Info(object text, bool save = true)
        {
            LogAndSave(text, "[Info]", ConsoleColor.Yellow, save);
        }
        public static void Error(object text, bool save = true)
        {
            LogAndSave(text, "[Error]", ConsoleColor.Red, save);
        }
        public static void Warn(object text, bool save = true)
        {
            LogAndSave(text, "[Warn]", ConsoleColor.DarkYellow, save);
        }
        public static void Success(object text, bool save = true)
        {
            LogAndSave(text, "[Success]", ConsoleColor.Green, save);
        }
        internal static void Init()
        {
            if (!Directory.Exists(LogPath))
                Directory.CreateDirectory(LogPath);
        }
        private static readonly ConcurrentQueue<string> _queue = new();
        [AutoInit]
        private static void SaveLogTask()
        {
            Task.Run(() =>
            {
                while (true)
                {
                    if (_queue.TryDequeue(out var text))
                        File.AppendAllText(LogName, text + Environment.NewLine);
                    else
                        Thread.Sleep(1);
                }
            });
        }
        public static void LogAndSave(object message, string prefix = "[Log]", ConsoleColor color = DefaultColor, bool save = true)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {prefix} {message}");
            Console.ForegroundColor = DefaultColor;
            if (save)
                _queue.Enqueue($"{DateTime.Now:HH:mm:ss} - {prefix} {message}");
        }
    }
}
