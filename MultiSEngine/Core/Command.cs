using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MultiSEngine.Core
{
    public class Command
    {
        public abstract class CmdBase
        {
            public abstract string Name { get; }
            public virtual bool ServerCommand { get; } = false;
            /// <summary>
            /// 返回是否继续将执行这条命令的消息发送到服务器
            /// </summary>
            /// <param name="client"></param>
            /// <param name="parma"></param>
            /// <returns></returns>
            public abstract bool Execute(ClientData client, string cmdName, List<string> parma);
        }
        public static void InitAllCommands()
        {
            AppDomain.CurrentDomain.GetAssemblies().ForEach(assembly =>
            {
                try
                {
                    assembly
                           .GetTypes()
                           .Where(t => t.BaseType == typeof(CmdBase))
                           .ForEach(t => Data.Commands.Add((CmdBase)Activator.CreateInstance(t)));
                }
                catch { }
            });
        }
        public static bool HandleCommand(ClientData client, string text, out bool continueSend, bool fromConsole = false)
        {
            continueSend = true;
            if (fromConsole && (!text?.StartsWith("/") ?? false))
                text = "/" + text;
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
                        cmdName = text[..num].ToLower();
                    }
                    List<string> list;
                    if (num < 0)
                    {
                        list = new List<string>();
                    }
                    else
                    {
                        list = ParseParameters(text[num..]);
                    }
                    List<CmdBase> aviliableCommands;
                    if (fromConsole)
                        aviliableCommands = Data.Commands.FindAll(c => c.ServerCommand && (c.Name.ToLower() == cmdName.ToLower() || cmdName.Contains(c.Name)));
                    else
                        aviliableCommands = Data.Commands.FindAll(c => c.Name.ToLower() == cmdName.ToLower() && !c.ServerCommand);
                    if (aviliableCommands.FirstOrDefault() is { } command)
                    {
                        try
                        {
                            continueSend = command.Execute(client, cmdName, list);
                        }
                        catch (Exception ex)
                        {
                            Logs.Info($"An exception occurred while executing the command: {command.Name}{Environment.NewLine}{ex.Message}");
                            client.SendErrorMessage(Localization.Get("Prompt_CommandFailed"));
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
            List<string> list = new();
            StringBuilder stringBuilder = new();
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
