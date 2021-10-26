using MultiSEngine.Modules.DataStruct;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrProtocol.Models;

namespace MultiSEngine.Modules.Cmds
{
    internal class InternalCommand : Core.Command.CmdBase
    {
        public override string Name => "mse";

        public override bool Execute(ClientData client, string cmdName, List<string> parma)
        {
            if (client is null)
                client.SendErrorMessage("Unable to execute this command.");
            else if (parma.Any())
            {
                switch (parma.First().ToLower())
                {
                    case "tp":
                    case "to":
                    case "t":
                        if (parma.Count < 2)
                            client.SendInfoMessage($"{Localization.Get("Prompt_InvalidFormat")}{Environment.NewLine}{Localization.Get("Help_Tp")}");
                        else
                            SwitchServer(client, parma[1]);
                        break;
                    case "back":
                    case "b":
                        if (client.State == ClientData.ClientState.NewConnection)
                            client.SendInfoMessage($"{Localization.Get("Command_NotJoined")}");
                        else if (client.Server == Config.Instance.DefaultServerInternal)
                            client.SendErrorMessage(string.Format(Localization.Get("Command_AlreadyIn"), client.Server.Name));
                        else
                            client.Back();
                        break;
                    case "list":
                    case "l":
                        client.SendSuccessMessage($"{Localization.Get("Command_AviliableServer")}{Environment.NewLine + "- "}{string.Join(Environment.NewLine + "- ", (from server in Config.Instance.Servers let text = $"{server.Name} <{server.Online()}>" select text))}");
                        break;
                    case "password":
                    case "pass":
                    case "p":
                        if (parma.Count > 1)
                        {
                            if (client.State != ClientData.ClientState.RequestPassword)
                                client.SendDataToServer(new TrProtocol.Packets.SendPassword()
                                {
                                    Password = parma[1]
                                });
                            else
                                client.SendErrorMessage(Localization.Get("Command_NotJoined"));
                        }
                        else
                            client.SendInfoMessage($"{Localization.Get("Prompt_InvalidFormat")}{Environment.NewLine}{Localization.Get("Help_Password")}");
                        break;
#if DEBUG
                    case "let":
                        if (parma.Count < 3)
                            Console.Write("error /mse let name server");
                        else
                            Data.Clients.FirstOrDefault(c => c.Name.ToLower().StartsWith(parma[1].ToLower()))?.Join(Utils.GetServerInfoByName(parma[2]).FirstOrDefault());
                        break;
                    case "test":
                        Task.Run(() =>
                        {
                            for (int i = 0; i < 9; i++)
                            {
                                client.SendDataToClient(new TrProtocol.Packets.Modules.NetParticlesModule()
                                {
                                    ParticleType = (ParticleOrchestraType)i,
                                    Setting = new()
                                    {
                                        IndexOfPlayerWhoInvokedThis = client.Player.Index,
                                        MovementVector = new(1, -1),
                                        PackedShaderIndex = i,
                                        PositionInWorld = new(client.Player.X, client.Player.Y)
                                    }
                                });
                                client.SendDataToClient(new TrProtocol.Packets.Modules.NetParticlesModule()
                                {
                                    ParticleType = (ParticleOrchestraType)i,
                                    Setting = new()
                                    {
                                        IndexOfPlayerWhoInvokedThis = client.Player.Index,
                                        MovementVector = new(1, -1),
                                        PackedShaderIndex = i,
                                        PositionInWorld = new(client.Player.X + 16, client.Player.Y - 16)
                                    }
                                });
                                Task.Delay(1000).Wait();
                                Console.WriteLine(i);
                            }
                        });
                        break;
#endif
                    default:
                        SendHelpText();
                        break;
                }
            }
            else
                SendHelpText();
            return false;
            void SendHelpText()
            {
                client.SendInfoMessage($"{Localization.Get("Prompt_InvalidFormat")}\r\n" +
                    $"{Localization.Get("Help_Tp")}\r\n" +
                    $"{Localization.Get("Help_Back")}\r\n" +
                    $"{Localization.Get("Help_List")}\r\n" +
                    $"{Localization.Get("Help_Command")}"
                    );
            }
        }
        private static void SwitchServer(ClientData client, string serverName)
        {
            if (client.State > ClientData.ClientState.ReadyToSwitch && client.State < ClientData.ClientState.InGame)
            {
                client.SendErrorMessage(Localization.Get("Command_IsSwitching"));
                return;
            }
            if (Utils.GetServerInfoByName(serverName).FirstOrDefault() is { } server)
            {
                if (client.Server == server)
                    client.SendErrorMessage(string.Format(Localization.Get("Command_AlreadyIn"), server.Name));
                else
                {
                    client.SendInfoMessage(string.Format(Localization.Get("Command_Switch"), server.Name));
                    client.Join(server);
                }
            }
            else
                client.SendErrorMessage(string.Format(Localization.Get("Command_ServerNotFound"), serverName));
        }
    }
}
