using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using TrProtocol;
using TrProtocol.Models;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{
    public class FakeWorldAdapter : ClientAdapter, IStatusChangeable
    {
        public FakeWorldAdapter(Net.NetSession connection) : this(null, connection)
        {
        }
        public FakeWorldAdapter(ClientData client, Net.NetSession connection) : base(client, connection)
        {
        }
        public const int Width = 8400;
        public const int Height = 2400;
        public bool RunningAsNormal { get; set; } = false;
        public bool IsEnterWorld = false;
        public void ChangeProcessState(bool asNormal)
        {
            if (asNormal)
                IsEnterWorld = false;
            RunningAsNormal = true;
        }
        public void BackToThere()
        {
            Logs.Info($"[{Client.Name}] now in FakeWorld");
            Client.State = ClientData.ClientState.ReadyToSwitch;
            Client.Player.ServerData = new();
            Client.Server = null;
            Client.SAdapter?.Stop(true);
            Client.Sync(null);
            Client.TP(Width / 2, Height / 2);
            Client.SendDataToClient(Data.StaticSpawnSquareData);
            Client.SendDataToClient(Data.StaticDeactiveAllPlayer); //隐藏所有玩家
        }
        public override bool GetData(ref Span<byte> buf)
        {
            var msgType = (MessageID)buf[2];
#if DEBUG
            Console.WriteLine($"[Recieve CLIENT] {msgType}");
#endif
            if (msgType is MessageID.ClientHello
                or MessageID.RequestWorldInfo
                or MessageID.RequestTileData
                or MessageID.SpawnPlayer
                )
            {
                if (RunningAsNormal)
                    return base.GetData(ref buf);
                switch (msgType)
                {
                    case MessageID.ClientHello:
                        {
                            using var reader = new BinaryReader(new MemoryStream(buf.ToArray()));
                            var hello = Net.DefaultServerSerializer.Deserialize(reader) as ClientHello;
                            if (!Hooks.OnPlayerJoin(Client, Client.IP, Client.Port, hello.Version, out var joinEvent))
                            {
                                Client.ReadVersion(joinEvent.Version);
                                if (Client.Player.VersionNum < 269 || (Client.Player.VersionNum != Config.Instance.ServerVersion && !Config.Instance.EnableCrossplayFeature))
                                    Client.Disconnect(Localization.Instance["Prompt_VersionNotAllowed", $"{Data.Convert(Client.Player.VersionNum)} ({Client.Player.VersionNum})"]);
                                else
                                    InternalSendPacket(new LoadPlayer() { PlayerSlot = 0, ServerWantsToRunCheckBytesInClientLoopThread = true });
                            }
                            return true;
                        }
                    case MessageID.RequestWorldInfo:
                        var bb = new BitsByte();
                        bb[6] = true;
                        Client.Player.OriginData.WorldData = new WorldData()
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
                        Client.SendDataToClient(Client.Player.OriginData.WorldData);
                        return true;
                    case MessageID.RequestTileData:
                        InternalSendPacket(Data.StaticSpawnSquareData);
                        Client.SendDataToClient(new StartPlaying());
                        return true;
                    case MessageID.SpawnPlayer:
                        IsEnterWorld = true;
                        RunningAsNormal = true;
                        Client.Player.SpawnX = BitConverter.ToInt16(buf.Slice(4, 2));
                        Client.Player.SpawnY = BitConverter.ToInt16(buf.Slice(6, 2));
                        Client.SendDataToClient(new FinishedConnectingToServer());
                        Client.SendMessage(Data.Motd, false);
                        Data.Clients.Where(c => c.Server is null && c != Client).ForEach(c => c.SendMessage($"{Client.Name} has join."));
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
            return base.GetData(ref buf);
        }
    }
}
