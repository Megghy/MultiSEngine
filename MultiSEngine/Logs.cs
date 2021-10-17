using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;

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
        private static Queue LogQueue;
        internal static void SaveLogTask()
        {
            LogQueue = new();
            Task.Run(() =>
            {
                while (true)
                {
                    if (!Directory.Exists(LogPath))
                        Directory.CreateDirectory(LogPath);
                    while (LogQueue.Count < 1)
                        Task.Delay(1).Wait();
                    File.AppendAllText(LogName, LogQueue.Dequeue()?.ToString());
                }
            });
        }
        public static void LogAndSave(object message, string prefix = "[Log]", ConsoleColor color = DefaultColor, bool save = true)
        {
            if (LogQueue is null) SaveLogTask();
            Console.ForegroundColor = color;
            Console.WriteLine($"{prefix} {message}");
            if (save) LogQueue.Enqueue($"{DateTime.Now:yyyy-MM-dd-HH:mm:ss} - {prefix} {message}{Environment.NewLine}");
            Console.ForegroundColor = DefaultColor;
        }
    }
}
