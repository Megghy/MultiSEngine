using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using TrProtocol;
using TrProtocol.Models;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{
    public class ClientAdapter : BaseAdapter
    {
        public ClientAdapter(ClientData client, Net.NetSession connection) : base(client)
        {
            _clientConnection = connection;
        }
        protected override void OnRecieveLoopError(Exception ex)
        {
            base.OnRecieveLoopError(ex);
            if (ex is EndOfStreamException or IOException or SocketException)
                Client.Disconnect();
        }
        public bool IsEntered { get; protected set; } = false;
        public override bool ListenningClient => true;
        internal Net.NetSession _clientConnection { get; set; }
        public override bool GetData(ref Span<byte> buf)
        {
            var msgType = (MessageID)buf[2];
            if (msgType is MessageID.ClientHello
                or MessageID.ClientUUID
                or MessageID.NetModules
                or MessageID.SpawnPlayer
                or MessageID.SyncPlayer
                or MessageID.SyncEquipment
                or MessageID.PlayerMana
                or MessageID.PlayerBuff
                or MessageID.PlayerControls
                )
            {
                using var reader = new BinaryReader(new MemoryStream(buf.ToArray()));
                var packet = Net.DefaultServerSerializer.Deserialize(reader);
                switch (packet)
                {
                    case ClientHello hello: //使用fakeworld时不会使用这个
                        {
                            if (Client.State is ClientData.ClientState.NewConnection) //首次连接时默认进入主服务器
                            {
                                if (Config.Instance.DefaultServerInternal is { })
                                {
                                    Client.ReadVersion(hello);
                                    Task.Run(() => Client.Join(Config.Instance.DefaultServerInternal));
                                }
                                else
                                    Client.Disconnect("No default server is set for the current server.");
                            }
                            return true;
                        }
                    case SyncPlayer:
                    case SyncEquipment:
                    case PlayerHealth:
                    case PlayerMana:
                    case PlayerBuffs:
                    case PlayerControls:
                        Client.Player.UpdateData(packet, true);
                        return false;
                    case ClientUUID uuid:
                        Client.Player.UUID = uuid.UUID;
                        return false;
                    case SpawnPlayer:
                        IsEntered = true;
                        break;
                    /*case SyncNPCName npcName:
                        Client.SendDataToServer(npcName, true);
                        return false; //特殊包*/
                    case TrProtocol.Packets.Modules.NetTextModuleC2S modules:
                        if (!Hooks.OnChat(Client, modules, out _))
                        {
                            Logs.LogAndSave($"{Client.Name} <{Client.Server?.Name}>: {modules.Text}", "[Chat]");
                            if (modules.Command == "Say" && (Command.HandleCommand(Client, modules.Text, out var c) && !c))
                                return true;
                            else if (Client.State == ClientData.ClientState.NewConnection)
                            {
                                Client.SendInfoMessage($"{Localization.Instance["Command_NotEntered"]}\r\n" +
                                    $"{Localization.Instance["Help_Tp"]}\r\n" +
                                    $"{Localization.Instance["Help_Back"]}\r\n" +
                                    $"{Localization.Instance["Help_List"]}\r\n" +
                                    $"{Localization.Instance["Help_Command"]}"
                                );
                                return true;
                            }
                            else
                            {
                                if (Config.Instance.EnableChatForward && !modules.Text.StartsWith("/"))
                                {
                                    Data.Clients.Where(c => c.Server != Client.Server)
                                        .ForEach(c => c.SendMessage(Config.Instance.ChatFormat.Replace("{servername}", Client.Server?.Name ?? "Not Join")
                                        .Replace("username", Client.Name)
                                        .Replace("{message}", modules.Text)));
                                }
                                if (Client.Server is null)
                                    Client.SendDataToClient(new TrProtocol.Packets.Modules.NetTextModuleS2C()
                                    {
                                        Text = NetworkText.FromLiteral(modules.Text),
                                        PlayerSlot = Client.Index,
                                        Color = Color.White,
                                    });
                                //Client.SendDataToServer(modules, true);
                            }
                        }
                        return false;
                }
            }
            return false;
        }

        public override void SendData(ref Span<byte> data)
        {
            if (!ShouldStop)
                Client.SendDataToServer(ref data);
        }
    }
}

