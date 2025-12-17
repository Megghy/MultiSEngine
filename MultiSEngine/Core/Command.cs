using System.Text;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;

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
            public abstract ValueTask<bool> Execute(ClientData client, string cmdName, string[] parma);
        }
        [AutoInit(postMsg: "Registed all commands.")]
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
        public static async ValueTask<(bool handled, bool continueSend)> HandleCommand(ClientData client, string text, bool fromConsole = false)
        {
            var continueSend = true;
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
                        list = [];
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
                            continueSend = await command.Execute(client, cmdName, list.ToArray()).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Logs.Info($"An exception occurred while executing the command: {command.Name}{Environment.NewLine}{ex}");
                            if (client is not null)
                                await client.SendErrorMessageAsync(Localization.Get("Prompt_CommandFailed")).ConfigureAwait(false);
                            else
                                Logs.Error(Localization.Get("Prompt_CommandFailed"));
                        }
                        return (true, continueSend);
                    }
                }
                else
                    return (false, continueSend);
            }
            return (false, continueSend);
        }
        static bool IsWhiteSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n';
        }
        static List<string> ParseParameters(string str)
        {
            List<string> list = [];
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
