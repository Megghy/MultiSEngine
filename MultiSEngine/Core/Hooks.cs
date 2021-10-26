using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;
using System.IO;
using TrProtocol;
using TrProtocol.Models;

namespace MultiSEngine.Core
{
    public class Hooks
    {
        public interface IEventArgs
        {
            public ClientData Client { get; }
            public bool Handled { get; set; }
        }
        public class PlayerJoinEventArgs : IEventArgs
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
        public class PlayerLeaveEventArgs : IEventArgs
        {
            public PlayerLeaveEventArgs(ClientData client)
            {
                Client = client;
            }
            public ClientData Client { get; private set; }
            public bool Handled { get; set; } = false;
        }
        public class RecieveCustomPacketEventArgs : IEventArgs
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
        public class SwitchEventArgs : IEventArgs
        {
            public SwitchEventArgs(ClientData client, ServerInfo targetServer, bool isPreSwitch)
            {
                Client = client;
                TargetServer = targetServer;
                PreSwitch = isPreSwitch;
            }
            public ClientData Client { get; private set; }
            public ServerInfo TargetServer { get; private set; }
            public bool PreSwitch { get; }
            public bool Handled { get; set; } = false;
        }
        public class ChatEventArgs : IEventArgs
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
        public class SendPacketEventArgs : IEventArgs
        {
            public SendPacketEventArgs(ClientData client, Packet packet, bool toClient)
            {
                Client = client;
                Packet = packet;
                ToClient = toClient;
            }
            public ClientData Client { get; private set; }
            public Packet Packet { get; set; }
            public bool ToClient { get; }
            public bool ToServer => !ToClient;
            public bool Handled { get; set; } = false;
        }
        public class GetPacketEventArgs : IEventArgs
        {
            public GetPacketEventArgs(ClientData client, Packet packet, bool fromClient)
            {
                Client = client;
                Packet = packet;
                FromClient = fromClient;
            }
            public ClientData Client { get; private set; }
            public Packet Packet { get; set; }
            public bool FromClient { get; }
            public bool FromServer => !FromClient;
            public bool Handled { get; set; } = false;
        }

        public delegate void PlayerJoinEvent(PlayerJoinEventArgs args);
        public static event PlayerJoinEvent PlayerJoin;
        public delegate void PlayerLeaveEvent(PlayerLeaveEventArgs args);
        public static event PlayerLeaveEvent PlayerLeave;
        public delegate void RecieveCustomPacketEvent(RecieveCustomPacketEventArgs args);
        public static event RecieveCustomPacketEvent RecieveCustomData;
        public delegate void PreSwitchEvent(SwitchEventArgs args);
        public static event PreSwitchEvent PreSwitch;
        public delegate void PostSwitchEvent(SwitchEventArgs args);
        public static event PostSwitchEvent PostSwitch;
        public delegate void ChatEvent(ChatEventArgs args);
        public static event ChatEvent Chat;
        public delegate void SendPacketEvent(SendPacketEventArgs args);
        public static event SendPacketEvent SendPacket;
        public delegate void RecievePacketEvent(GetPacketEventArgs args);
        public static event RecievePacketEvent RecievePacket;
        internal static bool OnPlayerJoin(ClientData client, string ip, int port, string version, out PlayerJoinEventArgs args)
        {
            args = new(client, ip, port, version);
            PlayerJoin?.Invoke(args);
            if (!args.Handled)
                PluginSystem.OnEvent(args);
            return args.Handled;
        }
        internal static bool OnPlayerLeave(ClientData client, out PlayerLeaveEventArgs args)
        {
            args = new(client);
            PlayerLeave?.Invoke(args);
            if (!args.Handled)
                PluginSystem.OnEvent(args);
            return args.Handled;
        }
        internal static bool OnRecieveCustomData(ClientData client, Packet packet, BinaryReader reader, out RecieveCustomPacketEventArgs args)
        {
            var position = reader.BaseStream.Position;
            args = new(client, packet, reader);
            args.Reader.BaseStream.Position = 3L;
            RecieveCustomData?.Invoke(args);
            args.Reader.BaseStream.Position = position;
            if (!args.Handled)
                PluginSystem.OnEvent(args);
            return args.Handled;
        }
        internal static bool OnPreSwitch(ClientData client, ServerInfo targetServer, out SwitchEventArgs args)
        {
            args = new(client, targetServer, true);
            PreSwitch?.Invoke(args);
            if (!args.Handled)
                PluginSystem.OnEvent(args);
            return args.Handled;
        }
        internal static bool OnPostSwitch(ClientData client, ServerInfo targetServer, out SwitchEventArgs args)
        {
            args = new(client, targetServer, false);
            PostSwitch?.Invoke(args);
            if (!args.Handled)
                PluginSystem.OnEvent(args);
            return args.Handled;
        }
        internal static bool OnChat(ClientData client, TrProtocol.Packets.Modules.NetTextModuleC2S module, out ChatEventArgs args)
        {
            args = new(client, module.Text); 
            Chat?.Invoke(args); 
            if (!args.Handled)
                PluginSystem.OnEvent(args);
            return args.Handled;
        }
        internal static bool OnSendPacket(ClientData client, Packet packet, bool toClient, out SendPacketEventArgs args)
        {
            args = new(client, packet, toClient);
            SendPacket?.Invoke(args);
            if (!args.Handled)
                PluginSystem.OnEvent(args);
            return args.Handled;
        }
        internal static bool OnGetPacket(ClientData client, Packet packet, bool fromClient, out GetPacketEventArgs args)
        {
            args = new(client, packet, fromClient);
            RecievePacket?.Invoke(args);
            if (!args.Handled)
                PluginSystem.OnEvent(args);
            return args.Handled;
        }
    }
}
