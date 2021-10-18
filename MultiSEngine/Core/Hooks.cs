using Delphinus;
using MultiSEngine.Modules.DataStruct;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria;

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
            public int Port {  get; private set; }
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
        internal static bool OnChat(ClientData client, string message, out ChatEventArgs args)
        {
            args = new(client, message);
            Chat?.Invoke(args);
            return args.Handled;
        }
        internal static bool OnSendPacket(ClientData client, Packet packet, out PacketEventArgs args)
        {
            args = new(client, packet);
            SendPacket?.Invoke(args);
            return args.Handled;
        }
        internal static bool OnRecievePacket(ClientData client, Packet packet, out PacketEventArgs args)
        {
            args = new(client, packet);
            RecievePacket?.Invoke(args);
            return args.Handled;
        }
    }
}
