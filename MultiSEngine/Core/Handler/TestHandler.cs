using System;
using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Handler
{
    public class TestHandler : BaseHandler
    {
        public TestHandler(BaseAdapter parent) : base(parent)
        {
            if (parent is not TestAdapter)
            {
                throw new Exception($"Cannot create TestAdapter for {parent.GetType().FullName}");
            }
        }
        private TestAdapter TestParent
            => (TestAdapter)Parent;
        public override bool RecieveServerData(MessageID msgType, ref Span<byte> data)
        {
            if (Parent.IsDisposed)
                return true;
            switch (msgType)
            {
                case MessageID.Kick:
                    var kick = data.AsPacket<Kick>();
                    var reason = kick.Reason.GetText();
                    Parent.Stop(true);
                    TestParent.IsSuccess = false;
                    TestParent.Log($"Kicked. Reason: {(string.IsNullOrEmpty(reason) ? "Unkown" : reason)}", false, ConsoleColor.Red);
                    TestParent.IsSuccess = false;
                    break;
                case MessageID.LoadPlayer:
                    TestParent.State = 2;
                    TestParent.Log($"Sending [PlayerInfo] packet");
                    SendToServerDirect(new SyncPlayer()
                    {
                        Name = "MultiSEngine"
                    });
                    TestParent.Log($"Sending [UUID] packet");
                    SendToServerDirect(new ClientUUID()
                    {
                        UUID = "114514"
                    });
                    TestParent.Log($"Requesting world data");
                    SendToServerDirect(new RequestWorldInfo() { });
                    TestParent.State = 3;
                    break;
                case MessageID.WorldData:
                    if (!TestParent.IsSuccess.HasValue)
                    {
                        TestParent.State = 4;
                        TestParent.Log($"Requesting map data");
                        SendToServerDirect(new RequestTileData()
                        {
                            Position = new(-1, -1)
                        });//请求物块数据
                        TestParent.Log($"Requesting spawn player");
                        SendToServerDirect(new SpawnPlayer()
                        {
                            Position = new(-1, -1)
                        });
                    }
                    break;
                case MessageID.RequestPassword:
                    TestParent.IsSuccess = false;
                    TestParent.Log($"Target server request password", false, ConsoleColor.Red);
                    break;
                case MessageID.StartPlaying:
                    TestParent.State = 10;
                    TestParent.IsSuccess = true;
                    TestParent.Log($"Server authentication completed, allow client to start playing", true, ConsoleColor.Green);
                    break;
            }
            return true;
        }
    }
}
