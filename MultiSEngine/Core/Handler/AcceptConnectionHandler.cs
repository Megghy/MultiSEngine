using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using Terraria;

namespace MultiSEngine.Core.Handler
{
    public sealed class AcceptConnectionHandler : BaseHandler
    {
        public AcceptConnectionHandler(BaseAdapter parent) : base(parent)
        {
        }

        public const int Width = 8400;
        public const int Height = 2400;

        public bool IsEntered { get; private set; }

        public override bool RecieveClientData(MessageID msgType, Span<byte> data)
        {
            if (msgType is MessageID.ClientHello
                or MessageID.RequestWorldInfo
                or MessageID.RequestTileData
                or MessageID.SpawnPlayer
                )
            {
                switch (msgType)
                {
                    case MessageID.ClientHello:
                        {
                            var hello = data.AsPacket<ClientHello>();
                            if (!Hooks.OnPlayerJoin(Client, Client.IP, Client.Port, hello.Version, out var joinEvent))
                            {
                                Client.ReadVersion(joinEvent.Version);
                                if (Client.Player.VersionNum < 269 || (Client.Player.VersionNum != Config.Instance.ServerVersion && !Config.Instance.EnableCrossplayFeature))
                                    Client.Disconnect(Localization.Instance["Prompt_VersionNotAllowed", $"{Data.Convert(Client.Player.VersionNum)} ({Client.Player.VersionNum})"]);
                                else
                                    Client.SendDataToClient(new LoadPlayer(0, true));
                            }
                            return true;
                        }
                    case MessageID.RequestWorldInfo:
                        var bb = new BitsByte();
                        bb[6] = true;
                        Client.Player.OriginCharacter.WorldData = new WorldData(
                            Width,
                            Height,
                            Width / 2,
                            Height / 2,
                            Config.Instance.ServerName,
                            Guid.Empty.ToByteArray(),
                            _EventInfo1: bb,
                            _WorldSurface: Height / 2, _GameMode: 0);
                        Client.SendDataToClient(Client.Player.OriginCharacter.WorldData);
                        return true;
                    case MessageID.RequestTileData:
                        Client.SendDataToClient(Data.StaticSpawnSquareData);
                        Client.SendDataToClient(new StartPlaying());
                        return true;
                    case MessageID.SpawnPlayer:
                        Parent.DeregisteHander(this); //移除假世界处理器
                        Client.Player.SpawnX = BitConverter.ToInt16(data[4..6]);
                        Client.Player.SpawnY = BitConverter.ToInt16(data[6..8]);
                        Client.SendDataToClient(new FinishedConnectingToServer());
                        Client.SendMessage(Data.Motd, false);
                        Data.Clients.Where(c => c.CurrentServer is null && c != Client)
                            .ForEach(c => c.SendMessage($"{Client.Name} has join."));
                        Logs.Info($"[{Client.Name}] has join.");
                        if (Config.Instance.SwitchToDefaultServerOnJoin)
                        {
                            if (Config.Instance.DefaultServerInternal is { })
                            {
                                Client.SendInfoMessage(Localization.Instance["Command_Switch", Config.Instance.DefaultServerInternal.Name]);
                                Task.Run(() =>
                                {
                                    Client.Join(Config.Instance.DefaultServerInternal);
                                });
                            }
                            else
                                Client.SendInfoMessage(Localization.Instance["Prompt_DefaultServerNotFound", [Config.Instance.DefaultServer]]);
                        }
                        else
                            Logs.Text($"[{Client.Name}] is temporarily transported in FakeWorld");
                        return true;
                }
            }
            return false;
        }
    }
}
