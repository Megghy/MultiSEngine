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
            using var sw = File.AppendText(LogName);
            sw.AutoFlush = true;
            while (true)
            {
                while (LogQueue.Count < 1)
                    Task.Delay(1).Wait();
                sw.WriteLine(LogQueue.Dequeue());
                if (!File.Exists(LogName))
                {
                    LogQueue = null;
                    return;
                }
            }
        }
        public static void LogAndSave(object message, string prefix = "[Log]", ConsoleColor color = DefaultColor, bool save = true)
        {
            if (LogQueue is null)
            {
                LogQueue = new();
                Task.Run(SaveLogTask);
            }
            Console.ForegroundColor = color;
            Console.WriteLine($"{prefix} {message}");
            if (save) LogQueue.Enqueue($"{DateTime.Now:yyyy-MM-dd-HH:mm:ss} - {prefix} {message}");
            Console.ForegroundColor = DefaultColor;
        }
    }
}
