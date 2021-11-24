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
        public static async void Text(object text, bool save = true)
        {
            await LogAndSave(text, "[Log]", DefaultColor, save);
        }
        public static async void Info(object text, bool save = true)
        {
            await LogAndSave(text, "[Info]", ConsoleColor.Yellow, save);
        }
        public static async void Error(object text, bool save = true)
        {
            await LogAndSave(text, "[Error]", ConsoleColor.Red, save);
        }
        public static async void Warn(object text, bool save = true)
        {
            await LogAndSave(text, "[Warn]", ConsoleColor.DarkYellow, save);
        }
        public static async void Success(object text, bool save = true)
        {
            await LogAndSave(text, "[Success]", ConsoleColor.Green, save);
        }
        private static StreamWriter logSW;
        internal static void Init()
        {
            if (!Directory.Exists(LogPath))
                Directory.CreateDirectory(LogPath);
            logSW = new(new FileStream(LogName, FileMode.OpenOrCreate));
        }
        public static async Task LogAndSave(object message, string prefix = "[Log]", ConsoleColor color = DefaultColor, bool save = true)
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
                await logSW.WriteLineAsync($"{DateTime.Now:yyyy-MM-dd-HH:mm:ss} - {prefix} {message}");
        }
    }
}
