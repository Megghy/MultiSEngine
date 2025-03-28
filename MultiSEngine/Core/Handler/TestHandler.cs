using Microsoft.Xna.Framework;
using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;

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

        byte index = 0;

        public override bool RecieveServerData(MessageID msgType, Span<byte> data)
        {
            if (Parent.IsDisposed)
                return true;
            switch (msgType)
            {
                case MessageID.Kick:
                    var kick = data.AsPacket<Kick>();
                    var reason = kick.Reason.GetText();
                    Parent.Dispose(true);
                    TestParent.IsSuccess = false;
                    TestParent.Log($"Kicked. Reason: {(string.IsNullOrEmpty(reason) ? "Unkown" : reason)}", false, ConsoleColor.Red);
                    TestParent.IsSuccess = false;
                    break;
                case MessageID.LoadPlayer:
                    var slot = data.AsPacket<LoadPlayer>();
                    index = slot.PlayerSlot; // 保存玩家索引
                    TestParent.Log($"Player index: {index}");
                    TestParent.State = 2;
                    TestParent.Log($"Sending [PlayerInfo] packet");
                    SendToServerDirect(new SyncPlayer(index, 0, 0, "MultiSEngine", 0, 0, 0, 0, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, Color.Blue, 0, 0, 0));
                    TestParent.Log($"Sending [UUID] packet");
                    SendToServerDirect(new ClientUUID("114514"));
                    TestParent.Log($"Requesting world data");
                    SendToServerDirect(new RequestWorldInfo() { });
                    TestParent.State = 3;
                    break;
                case MessageID.WorldData:
                    if (!TestParent.IsSuccess.HasValue)
                    {
                        TestParent.State = 4;
                        TestParent.Log($"Requesting map data");
                        SendToServerDirect(new RequestTileData(new(-1, -1)));//请求物块数据
                        TestParent.Log($"Requesting spawn player");
                        SendToServerDirect(new SpawnPlayer(index, new(-1, -1), 0, 0, 0, Terraria.PlayerSpawnContext.SpawningIntoWorld));
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
