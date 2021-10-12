using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;

namespace MultiSEngine.Core
{
    public class Command
    {
        public abstract class CmdBase
        {
            public abstract string Name { get; }
            public virtual bool ServerCommand { get; set; } = false;
            public virtual bool ContinueSend { get; set; } = false;
            public abstract void Execute(ClientData client, string cmdName, List<string> cmd);
        }
        public readonly static List<CmdBase> AllCommands = new();
        public static void InitAllCommands()
        {
            try
            {
                AppDomain
                    .CurrentDomain
                    .GetAssemblies()
                    .ForEach(assembly =>
                    {
                        assembly
                            .GetTypes()
                            .Where(t => t.BaseType == typeof(CmdBase))
                            .ForEach(t => AllCommands.Add((CmdBase)Activator.CreateInstance(t)));
                    });
            }
            catch { }
        }
        public static bool HandleCommand(ClientData client, string text, out bool continueSend)
        {
            continueSend = true;
            if (text?.StartsWith("/") ?? false)
            {
                text = text.Remove(0, 1);
                int num = -1;
                for (int i = 0; i < text.Length; i++)
                {
                    if (IsWhiteSpace(text[i]))
                    {
                        num = i;
                        break;
                    }
                }
                if (num != 0)
                {
                    var cmdName = string.Empty;
                    if (num < 0)
                    {
                        cmdName = text.ToLower();
                    }
                    else
                    {
                        cmdName = text.Substring(0, num).ToLower();
                    }
                    List<string> list;
                    if (num < 0)
                    {
                        list = new List<string>();
                    }
                    else
                    {
                        list = ParseParameters(text.Substring(num));
                    }
                    List<CmdBase> aviliableCommands = AllCommands.FindAll(c => c.Name.Contains(cmdName));
                    if (aviliableCommands.FirstOrDefault() is { } command)
                    {
                        try
                        {
                            continueSend = command.ContinueSend;
                            command.Execute(client, cmdName, list);
                        }
                        catch (Exception ex)
                        {
                            Logs.Info($"An exception occurred while executing the command: {command.Name}{Environment.NewLine}{ex.Message}");
                            client.SendErrorMessage($"Excute command failed.");
                        }
                        return true;
                    }
                }
                else
                    return false;
            }
            return false;
        }
        static bool IsWhiteSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n';
        }
        static List<string> ParseParameters(string str)
        {
            List<string> list = new List<string>();
            StringBuilder stringBuilder = new StringBuilder();
            bool flag = false;
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                if (c == '\\' && ++i < str.Length)
                {
                    if (str[i] != '"' && str[i] != ' ' && str[i] != '\\')
                    {
                        stringBuilder.Append('\\');
                    }
                    stringBuilder.Append(str[i]);
                }
                else if (c == '"')
                {
                    flag = !flag;
                    if (!flag)
                    {
                        list.Add(stringBuilder.ToString());
                        stringBuilder.Clear();
                    }
                    else if (stringBuilder.Length > 0)
                    {
                        list.Add(stringBuilder.ToString());
                        stringBuilder.Clear();
                    }
                }
                else if (IsWhiteSpace(c) && !flag)
                {
                    if (stringBuilder.Length > 0)
                    {
                        list.Add(stringBuilder.ToString());
                        stringBuilder.Clear();
                    }
                }
                else
                {
                    stringBuilder.Append(c);
                }
            }
            if (stringBuilder.Length > 0)
            {
                list.Add(stringBuilder.ToString());
            }
            return list;
        }
    }
}
