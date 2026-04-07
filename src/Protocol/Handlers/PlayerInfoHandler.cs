
namespace MultiSEngine.Protocol.Handlers
{
    public class PlayerInfoHandler : BaseHandler
    {
        public PlayerInfoHandler(BaseAdapter parent) : base(parent)
        {
        }
        public override async ValueTask<bool> RecieveClientDataAsync(HandlerPacketContext context)
        {
            var msgType = context.MessageId;
            switch (msgType)
            {
                case MessageID.SyncPlayer:
                    if (context.Packet is not SyncPlayer syncPlayer)
                        throw new Exception("[PlayerInfoHandler] SyncPlayer packet not found");
                    if (Client.State >= ClientState.SyncData && syncPlayer.Name != Client.Name)
                    {
                        await Client.DisconnectAsync("You cannot change your name.");
                        return true;
                    }
                    else
                    {
                        Client.Player.UpdateData(syncPlayer, true);
                    }
                    break;
                case MessageID.SyncEquipment:
                case MessageID.SyncLoadout:
                case MessageID.PlayerHealth:
                case MessageID.PlayerMana:
                case MessageID.PlayerBuffs:
                case MessageID.PlayerControls:
                    if (context.Packet is INetPacket packet)
                        Client.Player.UpdateData(packet, true);
                    break;
                case MessageID.ClientUUID:
                    if (context.Packet is not ClientUUID uuid)
                        throw new Exception("[PlayerInfoHandler] ClientUUID packet not found");
                    if (Client.State >= ClientState.SyncData && uuid.UUID != Client.Player.UUID)
                    {
                        await Client.DisconnectAsync("You cannot change your UUID.");
                    }
                    else
                    {
                        Client.Player.UUID = uuid.UUID;
                    }
                    return false;
            }
            return false;
        }
        public override ValueTask<bool> RecieveServerDataAsync(HandlerPacketContext context)
        {
            var msgType = context.MessageId;
            switch (msgType)
            {
                case MessageID.LoadPlayer:
                    if (context.Packet is not LoadPlayer slot)
                        throw new Exception("[PlayerInfoHandler] LoadPlayer packet not found");
                    if (Client.Player.Index != slot.PlayerSlot)
                        Logs.Text($"Update the index of [{Client.Name}]: {Client.Player.Index} => {slot.PlayerSlot}.");
                    Client.Player.Index = slot.PlayerSlot;
                    return ValueTask.FromResult(false);
                case MessageID.WorldData:
                    if (context.Packet is not WorldData worldData)
                        throw new Exception("[PlayerInfoHandler] WorldData packet not found");
                    Client.Player.UpdateData(worldData, false);
                    return ValueTask.FromResult(false);
                case MessageID.SyncPlayer:
                case MessageID.SyncEquipment:
                case MessageID.SyncLoadout:
                case MessageID.PlayerHealth:
                case MessageID.PlayerMana:
                case MessageID.PlayerBuffs:
                case MessageID.PlayerControls:
                    if (context.Packet is INetPacket packet)
                        Client.Player.UpdateData(packet, false);
                    return ValueTask.FromResult(false);
                case MessageID.SpawnPlayer:
                    if (context.Packet is not SpawnPlayer spawn)
                        throw new Exception("[PlayerInfoHandler] SpawnPlayer packet not found");
                    Client.Player.SpawnX = spawn.Position.X;
                    Client.Player.SpawnY = spawn.Position.Y;
                    Client.Player.DeathsPVE = spawn.DeathsPVE;
                    Client.Player.DeathsPVP = spawn.DeathsPVP;
                    Client.Player.Timer = spawn.Timer;
                    Client.Player.Context = spawn.Context;
                    return ValueTask.FromResult(false);
            }
            return ValueTask.FromResult(false);
        }
    }
}


