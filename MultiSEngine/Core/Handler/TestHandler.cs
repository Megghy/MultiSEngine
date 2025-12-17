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

        public override async ValueTask<bool> RecieveServerDataAsync(HandlerPacketContext context)
        {
            if (Parent.IsDisposed)
                return true;
            var msgType = context.MessageId;
            var data = context.Data;
            switch (msgType)
            {
                case MessageID.Kick:
                    var kick = context.Packet as Kick ?? throw new Exception("[TestHandler] Kick packet not found");
                    var reason = kick.Reason.GetText();
                    await Parent.DisposeAsync(true).ConfigureAwait(false);
                    TestParent.IsSuccess = false;
                    TestParent.Log($"Kicked. Reason: {(string.IsNullOrEmpty(reason) ? "Unkown" : reason)}", false, ConsoleColor.Red);
                    TestParent.IsSuccess = false;
                    break;
                case MessageID.LoadPlayer:
                    var slot = context.Packet as LoadPlayer ?? throw new Exception("[TestHandler] LoadPlayer packet not found");
                    index = slot.PlayerSlot; // 保存玩家索引
                    TestParent.Log($"Player index: {index}");
                    TestParent.State = 2;
                    TestParent.Log($"Sending [PlayerInfo] packet");
                    await SendToServerDirectAsync(new SyncPlayer
                    {
                        PlayerSlot = index,
                        SkinVariant = 0,
                        Hair = 0,
                        Name = "MultiSEngine",
                        HairDye = 0,
                        Bit1 = new BitsByte(),
                        Bit2 = new BitsByte(),
                        HideMisc = 0,
                        HairColor = new Color(0x00, 0x00, 0xFF),
                        SkinColor = new Color(0x00, 0x00, 0xFF),
                        EyeColor = new Color(0x00, 0x00, 0xFF),
                        ShirtColor = new Color(0x00, 0x00, 0xFF),
                        UnderShirtColor = new Color(0x00, 0x00, 0xFF),
                        PantsColor = new Color(0x00, 0x00, 0xFF),
                        ShoeColor = new Color(0x00, 0x00, 0xFF),
                        Bit3 = new BitsByte(),
                        Bit4 = new BitsByte(),
                        Bit5 = new BitsByte()
                    }).ConfigureAwait(false);
                    TestParent.Log($"Sending [UUID] packet");
                    await SendToServerDirectAsync(new ClientUUID
                    {
                        UUID = "114514"
                    }).ConfigureAwait(false);
                    TestParent.Log($"Requesting world data");
                    await SendToServerDirectAsync(new RequestWorldInfo() { }).ConfigureAwait(false);
                    TestParent.State = 3;
                    break;
                case MessageID.WorldData:
                    if (!TestParent.IsSuccess.HasValue)
                    {
                        TestParent.State = 4;
                        TestParent.Log($"Requesting map data");
                        await SendToServerDirectAsync(new RequestTileData
                        {
                            Position = new(-1, -1)
                        }).ConfigureAwait(false);//请求物块数据
                        TestParent.Log($"Requesting spawn player");
                        await SendToServerDirectAsync(new SpawnPlayer
                        {
                            PlayerSlot = index,
                            Position = new ShortPosition(-1, -1),
                            Timer = 0,
                            DeathsPVE = 0,
                            DeathsPVP = 0,
                            Context = PlayerSpawnContext.SpawningIntoWorld
                        }).ConfigureAwait(false);
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
