using System.Buffers.Binary;

namespace MultiSEngine.Protocol.Handlers
{
    public sealed class AcceptConnectionHandler(BaseAdapter parent) : BaseHandler(parent)
    {
        public const int Width = 8400;
        public const int Height = 2400;

        public bool IsEntered { get; private set; }

        public override async ValueTask<bool> RecieveClientDataAsync(HandlerPacketContext context)
        {
            var msgType = context.MessageId;
            var data = context.Data;
            switch (msgType)
            {
                case MessageID.ClientHello:
                    {
                        if (context.Packet is ClientHello hello && !Hooks.OnPlayerJoin(Client, Client.IP, Client.Port, hello.Version, out var joinEvent))
                        {
                            Client.ReadVersion(joinEvent.Version);
                            if (Client.Player.VersionNum < 269 || (Client.Player.VersionNum != Config.Instance.ServerVersion && !Config.Instance.EnableCrossplayFeature))
                                await Client.DisconnectAsync(Localization.Instance["Prompt_VersionNotAllowed", $"{RuntimeState.Convert(Client.Player.VersionNum)} ({Client.Player.VersionNum})"]);
                            else
                                await SendToClientDirectAsync(new LoadPlayer { PlayerSlot = 0, ServerWantsToRunCheckBytesInClientLoopThread = true }).ConfigureAwait(false);
                        }
                        return true;
                    }
                case MessageID.RequestWorldInfo:
                    var bb = new BitsByte();
                    bb[6] = true;
                    var worldData = new WorldData
                    {
                        MaxTileX = Width,
                        MaxTileY = Height,
                        SpawnX = Width / 2,
                        SpawnY = Height / 2,
                        WorldSurface = 0,
                        RockLayer = 0,
                        WorldName = Config.Instance.ServerName,
                        WorldUniqueID = Guid.Empty,
                        EventInfo1 = bb,
                        GameMode = 0,
                    };
                    Client.Player.OriginCharacter.WorldData = worldData;
                    await SendToClientDirectAsync(Client.Player.OriginCharacter.WorldData).ConfigureAwait(false);
                    return true;
                case MessageID.RequestTileData:
                    await SendToClientDirectAsync(RuntimeState.SpawnSquarePacket).ConfigureAwait(false);
                    await SendToClientDirectAsync(new StartPlaying()).ConfigureAwait(false);
                    return true;
                case MessageID.SpawnPlayer:
                    Parent.DeregisterHandler(this); //移除假世界处理器

                    RuntimeState.Clients.Add(Client);

                    var span = data.Span;
                    Client.Player.SpawnX = BinaryPrimitives.ReadInt16LittleEndian(span[4..6]);
                    Client.Player.SpawnY = BinaryPrimitives.ReadInt16LittleEndian(span[6..8]);
                    await SendToClientDirectAsync(new FinishedConnectingToServer()).ConfigureAwait(false);
                    await Client.SendMessageAsync(RuntimeState.Motd, Utils.Rgb(255, 255, 255), false).ConfigureAwait(false);

                    foreach (var c in RuntimeState.Clients.Where(c => c.CurrentServer is null && c != Client))
                    {
                        await c.SendMessageAsync($"{Client.Name} has join.", Utils.Rgb(255, 255, 255), true).ConfigureAwait(false);
                    }
                    Logs.Info($"[{Client.Name}] has join.");
                    if (Config.Instance.SwitchToDefaultServerOnJoin)
                    {
                        if (Config.Instance.DefaultServerInternal is { })
                        {
                            await Client.SendMessageAsync(Localization.Instance["Command_Switch", Config.Instance.DefaultServerInternal.Name], Utils.Rgb(220, 220, 130)).ConfigureAwait(false);
                            await Client.Join(Config.Instance.DefaultServerInternal).ConfigureAwait(false);
                        }
                        else
                            await Client.SendMessageAsync(Localization.Instance["Prompt_DefaultServerNotFound", [Config.Instance.DefaultServer]], Utils.Rgb(220, 220, 130)).ConfigureAwait(false);
                    }
                    else
                        Logs.Text($"[{Client.Name}] is temporarily transported in FakeWorld");
                    return true;
            }
            return false;
        }
    }
}


