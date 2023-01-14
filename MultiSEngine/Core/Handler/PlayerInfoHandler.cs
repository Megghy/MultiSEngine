using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Handler
{
    public class PlayerInfoHandler : BaseHandler
    {
        public PlayerInfoHandler(BaseAdapter parent) : base(parent)
        {
        }
        public override bool RecieveClientData(MessageID msgType, ref Span<byte> data)
        {
            switch (msgType)
            {
                case MessageID.SyncPlayer:
                    var syncPlayer = data.AsPacket<SyncPlayer>();
                    if (Client.State >= ClientState.SyncData && syncPlayer?.Name != Client.Name)
                    {
                        Client.Disconnect("You cannot change your name.");
                        return true;
                    }
                    else
                    {
                        Client.Player.UpdateData(syncPlayer, true);
                    }
                    break;
                case MessageID.SyncEquipment:
                case MessageID.PlayerHealth:
                case MessageID.PlayerMana:
                case MessageID.PlayerBuffs:
                case MessageID.PlayerControls:
                    Client.Player.UpdateData(data.AsPacket(), true);
                    break;
                case MessageID.ClientUUID:
                    var uuid = data.AsPacket<ClientUUID>();
                    if (Client.State >= ClientState.SyncData && uuid?.UUID != Client.Player.UUID)
                    {
                        Client.Disconnect("You cannot change your UUID.");
                    }
                    else
                    {
                        Client.Player.UUID = uuid.UUID;
                    }
                    return false;
            }
            return false;
        }
        public override bool RecieveServerData(MessageID msgType, ref Span<byte> data)
        {
            switch (msgType)
            {
                case MessageID.LoadPlayer:
                    var slot = data.AsPacket<LoadPlayer>();
                    if (Client.Player.Index != slot.PlayerSlot)
                        Logs.Text($"Update the index of [{Client.Name}]: {Client.Player.Index} => {slot.PlayerSlot}.");
                    Client.Player.Index = slot.PlayerSlot;
                    return false;
                case MessageID.WorldData:
                    var worldData = data.AsPacket<WorldData>();
                    Client.Player.UpdateData(worldData, false);
                    return false;
                case MessageID.SpawnPlayer:
                    var spawn = data.AsPacket<SpawnPlayer>();
                    Client.Player.SpawnX = spawn.Position.X;
                    Client.Player.SpawnY = spawn.Position.Y;
                    return false;
            }
            return false;
        }
    }
}
