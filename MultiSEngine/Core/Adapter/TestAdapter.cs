using System;
using System.IO;
using System.Threading.Tasks;
using MultiSEngine.DataStruct;
using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{
    internal class TestAdapter : ServerAdapter, IDisposable
    {
        public TestAdapter(ServerInfo server, bool showDetails) : base(null, server)
        {
            ShowDetails = showDetails;
        }
        private bool ShowDetails { get; init; }
        public int State { get; private set; } = 0;
        public bool? IsSuccess { get; private set; }
        private void Log(string msg, bool isDetail = true, ConsoleColor color = ConsoleColor.Blue)
        {
            if (isDetail && !ShowDetails)
                return;
            Logs.LogAndSave(msg, $"[TEST] <{TargetServer.Name}> {(IsSuccess.HasValue ? ((bool)IsSuccess) ? "SUCCESS" : "FAILED" : "TESTING")}: {State} -", color, false);
        }
        public async Task StartTest()
        {
            if (State != 0)
                return;
            Log($"Start connecting to [{TargetServer.Name}]<{TargetServer.IP}:{TargetServer.Port}>");
            await Connect()
                .ContinueWith(task =>
                {
                    State = 1;
                    Log($"Sending [ConnectRequest] packet");
                    InternalSendPacket(new ClientHello()
                    {
                        Version = $"Terraria{TargetServer.VersionNum}"
                    });  //发起连接请求   
                });
        }
        protected override void OnRecieveLoopError(Exception ex)
        {
            throw ex;
        }
        public override bool GetData(ref Span<byte> buf)
        {
            var msgType = (MessageID)buf[2];
#if DEBUG
            Console.WriteLine($"[TEST RECIEVE] {msgType}");
#endif
            if (msgType is MessageID.Kick
                or MessageID.LoadPlayer
                or MessageID.WorldData
                or MessageID.RequestPassword
                or MessageID.StartPlaying
                )
            {
                using var reader = new BinaryReader(new MemoryStream(buf.ToArray()));
                var packet = Net.DefaultServerSerializer.Deserialize(reader);
                switch (packet)
                {
                    case Kick kick:
                        var reason = kick.Reason.GetText();
                        Stop(true);
                        IsSuccess = false;
                        Log($"Kicked. Reason: {(string.IsNullOrEmpty(reason) ? "Unkown" : reason)}", false, ConsoleColor.Red);
                        break;
                    case LoadPlayer:
                        State = 2;
                        Log($"Sending [PlayerInfo] packet");
                        InternalSendPacket(new SyncPlayer()
                        {
                            Name = "MultiSEngine"
                        });
                        Log($"Sending [UUID] packet");
                        InternalSendPacket(new ClientUUID()
                        {
                            UUID = "114514"
                        });
                        Log($"Requesting world data");
                        InternalSendPacket(new RequestWorldInfo() { });
                        State = 3;
                        break;
                    case WorldData:
                        if (!IsSuccess.HasValue)
                        {
                            State = 4;
                            Log($"Requesting map data");
                            InternalSendPacket(new RequestTileData()
                            {
                                Position = new(-1, -1)
                            });//请求物块数据
                            Log($"Requesting spawn player");
                            InternalSendPacket(new SpawnPlayer()
                            {
                                Position = new(-1, -1)
                            });
                        }
                        break;
                    case RequestPassword:
                        IsSuccess = false;
                        Log($"Target server request password", false, ConsoleColor.Red);
                        break;
                    case StartPlaying:
                        State = 10;
                        IsSuccess = true;
                        Log($"Server authentication completed, allow client to start playing", true, ConsoleColor.Green);
                        break;
                }
            }
            return true;
        }
        public void Dispose()
        {
            base.Stop(true);
        }
    }
}
