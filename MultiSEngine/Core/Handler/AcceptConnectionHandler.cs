using System;
using System.Linq;
using System.Threading.Tasks;
using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using TrProtocol;
using TrProtocol.Models;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Handler
{
    public class AcceptConnectionHandler : BaseHandler
    {
        public AcceptConnectionHandler(BaseAdapter parent) : base(parent)
        {
        }

        public const int Width = 8400;
        public const int Height = 2400;

        public bool IsEntered { get; private set; }

        public override bool RecieveClientData(MessageID msgType, byte[] data)
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
                                    Client.SendDataToClient(new LoadPlayer() { PlayerSlot = 0, ServerWantsToRunCheckBytesInClientLoopThread = true });
                            }
                            return true;
                        }
                    case MessageID.RequestWorldInfo:
                        var bb = new BitsByte();
                        bb[6] = true;
                        Client.Player.OriginCharacter.WorldData = new WorldData()
                        {
                            EventInfo1 = bb,
                            SpawnX = Width / 2,
                            SpawnY = Height / 2,
                            MaxTileX = Width,
                            MaxTileY = Height,
                            GameMode = 0,
                            WorldName = Config.Instance.ServerName,
                            WorldUniqueID = Guid.Empty
                        };
                        Client.SendDataToClient(Client.Player.OriginCharacter.WorldData);
                        return true;
                    case MessageID.RequestTileData:
                        Client.SendDataToClient(Data.StaticSpawnSquareData);
                        Client.SendDataToClient(new StartPlaying());
                        return true;
                    case MessageID.SpawnPlayer:
                        Parent.DeregisteHander(this); //移除假世界处理器
                        Client.Player.SpawnX = BitConverter.ToInt16(data, 4);
                        Client.Player.SpawnY = BitConverter.ToInt16(data, 6);
                        Client.SendDataToClient(new FinishedConnectingToServer());
                        Client.SendMessage(Data.Motd, false);
                        Data.Clients.Where(c => c.CurrentServer is null && c != Client)
                            .ForEach(c => c.SendMessage($"{Client.Name} has join."));
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
                                Client.SendInfoMessage(Localization.Instance["Prompt_DefaultServerNotFound", new[] { Config.Instance.DefaultServer }]);
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
