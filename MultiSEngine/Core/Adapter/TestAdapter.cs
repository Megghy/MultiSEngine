using MultiSEngine.DataStruct;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{
    internal class TestAdapter : ClientAdapter, IDisposable
    {
        public TestAdapter(ServerInfo server, bool showDetails) : base(null, new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            this.server = server;
            ShowDetails = showDetails;
        }
        private bool ShowDetails { get; init; }
        public int State { get; private set; } = 0;
        public bool? IsSuccess { get; private set; }
        public ServerInfo server { get; private set; }
        private void Log(string msg, bool isDetail = true, ConsoleColor color = ConsoleColor.Blue)
        {
            if (isDetail && !ShowDetails)
                return;
            Logs.LogAndSave(msg, $"[TEST] <{server.Name}> {(IsSuccess.HasValue ? ((bool)IsSuccess) ? "SUCCESS" : "FAILED" : "TESTING")}: {State} -", color, false);
        }
        public override BaseAdapter Start()
        {
            Task.Run(StartTest);
            return this;
        }
        private async Task StartTest()
        {
            Log($"Start connecting to [{server.Name}]<{server.IP}:{server.Port}>");
            await Connection.ConnectAsync(server.IP, server.Port);
            base.Start();
            State = 1;
            Log($"Sending [ConnectRequest] packet");
            InternalSendPacket(new ClientHello()
            {
                Version = $"Terraria{server.VersionNum}"
            });  //发起连接请求   
        }
        protected override void OnRecieveLoopError(Exception ex)
        {
            throw ex;
        }
        public override bool GetPacket(Packet packet)
        {
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
                    Log($"Server authentication completed, allow client to start running", true, ConsoleColor.Green);
                    break;
            }
            return false;
        }
        public void Dispose()
        {
            base.Stop(true);
            server = null;
        }
    }
}
