using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;
using System.IO;
using TrProtocol;
using TrProtocol.Models;

namespace MultiSEngine.Core
{
    public class Hooks
    {
        public class PlayerJoinEventArgs
        {
            public PlayerJoinEventArgs(ClientData client, string ip, int port, string version)
            {
                Client = client;
                IP = ip;
                Port = port;
                Version = version;
            }
            public ClientData Client { get; private set; }
            public string IP { get; private set; }
            public int Port { get; private set; }
            public string Version { get; set; }
            public bool Handled { get; set; } = false;
        }
        public class RecieveCustomPacketEventArgs
        {
            public RecieveCustomPacketEventArgs(ClientData client, Packet p, BinaryReader reader)
            {
                Client = client;
                Reader = reader;
                Packet = p;
            }
            public ClientData Client { get; private set; }
            public Packet Packet { get; private set; }
            public BinaryReader Reader { get; private set; }
            public bool Handled { get; set; } = false;
        }
        public class SwitchEventArgs
        {
            public SwitchEventArgs(ClientData client, ServerInfo targetServer)
            {
                Client = client;
                TargetServer = targetServer;
            }
            public ClientData Client { get; private set; }
            public ServerInfo TargetServer { get; private set; }
            public bool Handled { get; set; } = false;
        }
        public class ChatEventArgs
        {
            public ChatEventArgs(ClientData client, string message)
            {
                Client = client;
                Message = message;
            }
            public ClientData Client { get; private set; }
            public string Message { get; set; }
            public bool Handled { get; set; } = false;
        }
        public class PacketEventArgs
        {
            public PacketEventArgs(ClientData client, Packet packet)
            {
                Client = client;
                Packet = packet;
            }
            public ClientData Client { get; private set; }
            public Packet Packet { get; set; }
            public bool Handled { get; set; } = false;
        }

        public delegate void PlayerJoinEvent(PlayerJoinEventArgs args);
        public static event PlayerJoinEvent PlayerJoin;
        public delegate void RecieveCustomPacketEvent(RecieveCustomPacketEventArgs args);
        public static event RecieveCustomPacketEvent RecieveCustomData;
        public delegate void PreSwitchEvent(SwitchEventArgs args);
        public static event PreSwitchEvent PreSwitch;
        public delegate void PostSwitchEvent(SwitchEventArgs args);
        public static event PostSwitchEvent PostSwitch;
        public delegate void ChatEvent(ChatEventArgs args);
        public static event ChatEvent Chat;
        public delegate void SendPacketEvent(PacketEventArgs args);
        public static event SendPacketEvent SendPacket;
        public delegate void RecievePacketEvent(PacketEventArgs args);
        public static event RecievePacketEvent RecievePacket;
        internal static bool OnPlayerJoin(ClientData client, string ip, int port, string version, out PlayerJoinEventArgs args)
        {
            args = new(client, ip, port, version);
            PlayerJoin?.Invoke(args);
            return args.Handled;
        }
        internal static bool OnRecieveCustomData(ClientData client, Packet packet, BinaryReader reader, out RecieveCustomPacketEventArgs args)
        {
            var position = reader.BaseStream.Position;
            args = new(client, packet, reader);
            args.Reader.BaseStream.Position = 3L;
            RecieveCustomData?.Invoke(args);
            args.Reader.BaseStream.Position = position;
            return args.Handled;
        }
        internal static bool OnPreSwitch(ClientData client, ServerInfo targetServer, out SwitchEventArgs args)
        {
            args = new(client, targetServer);
            PreSwitch?.Invoke(args);
            return args.Handled;
        }
        internal static bool OnPostSwitch(ClientData client, ServerInfo targetServer, out SwitchEventArgs args)
        {
            args = new(client, targetServer);
            PostSwitch?.Invoke(args);
            return args.Handled;
        }
        internal static bool OnChat(ClientData client, TrProtocol.Packets.Modules.NetTextModuleC2S module, out ChatEventArgs args)
        {
            args = new(client, module.Text); 
            Chat?.Invoke(args);
            if (!args.Handled)
            {
                Logs.LogAndSave($"{client.Name} <{client.Server?.Name}>: {module.Text}", "[Chat]");
                if (module.Command == "Say" && (Command.HandleCommand(client, module.Text, out var c) && !c))
                    return false;
                else if (client.State == ClientData.ClientState.NewConnection)
                {
                    client.SendInfoMessage($"{Localization.Instance["Command_NotEntered"]}\r\n" +
                        $"{Localization.Instance["Help_Tp"]}\r\n" +
                        $"{Localization.Instance["Help_Back"]}\r\n" +
                        $"{Localization.Instance["Help_List"]}\r\n" +
                        $"{Localization.Instance["Help_Command"]}"
                    );
                }
                else
                {
                    if (Config.Instance.EnableChatForward)
                        client.Broadcast($"[{client.Server?.Name ?? "Not Join"}] {client.Name}: {module.Text}");
                    client.SendDataToGameServer(module, true);
                }
            }
            return args.Handled;
        }
        internal static bool OnSendPacket(ClientData client, Packet packet, out PacketEventArgs args)
        {
            args = new(client, packet);
            SendPacket?.Invoke(args);
            return args.Handled;
        }
        internal static bool OnGetPacket(ClientData client, Packet packet, out PacketEventArgs args)
        {
            args = new(client, packet);
            RecievePacket?.Invoke(args);
            return args.Handled;
        }
    }
}
