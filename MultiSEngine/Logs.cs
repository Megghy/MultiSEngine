using MultiSEngine.DataStruct;
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
        private static StreamWriter logSW;
        internal static void Init()
        {
            if (!Directory.Exists(LogPath))
                Directory.CreateDirectory(LogPath);
            logSW = new(new FileStream(LogName, FileMode.OpenOrCreate));
        }
        public static void LogAndSave(object message, string prefix = "[Log]", ConsoleColor color = DefaultColor, bool save = true)
        {
            if (!File.Exists(LogName))
            {
                logSW?.Dispose();
                logSW = new(new FileStream(LogName, FileMode.OpenOrCreate)); ;
            }
            Console.ForegroundColor = color;
            Console.WriteLine($"{prefix} {message}");
            Console.ForegroundColor = DefaultColor;
            if (save)
                logSW.WriteLine($"{DateTime.Now:yyyy-MM-dd-HH:mm:ss} - {prefix} {message}");
        }
    }
}
