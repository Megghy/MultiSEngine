﻿using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;
using System;
using System.Linq;
using System.Net.Sockets;
using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{
    public class ClientAdapter : AdapterBase
    {
        public ClientAdapter(ClientData client, Socket connection) : base(client, connection)
        {
            client.CAdapter = this;
        }
        public override PacketSerializer Serializer => Net.Instance.ServerSerializer;
        public override bool ListenningClient => true;
        public override void OnRecieveLoopError(Exception ex)
        {
            base.OnRecieveLoopError(ex);
            if (Client.State != ClientData.ClientState.Disconnect)
                Client.Disconnect();
        }
        public override bool GetPacket(Packet packet)
        {
            switch (packet)
            {
                case ClientHello hello: //使用fakeworld时不会使用这个
                    if (Client.State is ClientData.ClientState.NewConnection) //首次连接时默认进入主服务器
                    {
                        if (Config.Instance.DefaultServerInternal is { })
                        {
                            Client.ReadVersion(hello);
                            Client.Join(Config.Instance.DefaultServerInternal);
                        }
                        else
                            Client.Disconnect("No default server is set for the current server.");
                    }
                    return false;
                case SyncPlayer playerInfo:
                    Client.Player.UpdateData(playerInfo);
                    return true;
                case SyncEquipment:
                case PlayerHealth:
                case PlayerMana:
                case PlayerBuffs:
                case PlayerControls:
                    Client.Player.UpdateData(packet);
                    return !Client.Syncing;
                case ClientUUID uuid:
                    Client.Player.UUID = uuid.UUID;
                    return true;
                case SyncNPCName npcName:
                    Client.SendDataToServer(npcName, true);
                    return false; //特殊包
                case TrProtocol.Packets.Modules.NetTextModuleC2S modules:
                    if (!Hooks.OnChat(Client, modules, out _))
                    {
                        Logs.LogAndSave($"{Client.Name} <{Client.Server?.Name}>: {modules.Text}", "[Chat]");
                        if (modules.Command == "Say" && (Command.HandleCommand(Client, modules.Text, out var c) && !c))
                            return false;
                        else if (Client.State == ClientData.ClientState.NewConnection)
                        {
                            Client.SendInfoMessage($"{Localization.Instance["Command_NotEntered"]}\r\n" +
                                $"{Localization.Instance["Help_Tp"]}\r\n" +
                                $"{Localization.Instance["Help_Back"]}\r\n" +
                                $"{Localization.Instance["Help_List"]}\r\n" +
                                $"{Localization.Instance["Help_Command"]}"
                            );
                        }
                        else
                        {
                            if (Config.Instance.EnableChatForward && !modules.Text.StartsWith("/"))
                                Client.Broadcast($"[{Client.Server?.Name ?? "Not Join"}] {Client.Name}: {modules.Text}");
                            Client.SendDataToServer(modules, true);
                        }
                    }
                    return false;
                default:
                    return true;
            }
        }
        public override void SendPacket(Packet packet)
        {
            //bool shouldSerializeLikeClient = packet.GetType().GetProperties().Any(p => p.GetCustomAttributes(true)?.Any(a => a.GetType() == typeof(S2COnlyAttribute)) ?? false);
            if (!ShouldStop)
                Client.SendDataToServer(packet);
        }
    }
}

