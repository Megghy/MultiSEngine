using System.IO;
using TrProtocol;

namespace MultiSEngine.DataStruct.EventArgs
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
    /*public class SendDataEventArgs : IEventArgs
    {
        public SendDataEventArgs(ClientData client, Packet packet, bool toClient)
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
    public ref struct GetDataEventArgs : IEventArgs
    {
        public GetDataEventArgs(ClientData client, Packet packet, bool fromClient)
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
    }*/
}