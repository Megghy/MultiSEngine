using System.Net;
using MultiSEngine.Core.Adapter;
using MultiSEngine.Modules;

namespace MultiSEngine.DataStruct
{
    public class ClientData
    {
        public ClientData()
        {
        }
        public BaseAdapter Adapter { get; set; }
        internal PreConnectAdapter TempAdapter { get; set; } = null;

        #region 客户端信息
        public ClientState State { get; set; } = ClientState.NewConnection;
        public string IP => (Adapter?.ClientConnection?.RemoteEndPoint as IPEndPoint)?.Address?.ToString();
        public int Port => (Adapter?.ClientConnection?.RemoteEndPoint as IPEndPoint)?.Port ?? -1;
        public string Address => $"{IP}:{Port}"; public bool Syncing { get; internal set; } = false;
        public bool Disposed { get; private set; } = false;


        #endregion

        #region 常用的玩家信息
        public short SpawnX => CurrentServer is { SpawnX: >= 0, SpawnY: >= 0 } ? CurrentServer.SpawnX : Player.WorldSpawnX;
        public short SpawnY => CurrentServer is { SpawnY: >= 0, SpawnY: >= 0 } ? CurrentServer.SpawnY : Player.WorldSpawnY;
        public ServerInfo CurrentServer { get; set; } = null;
        public string Name => Player?.Name ?? Address;
        public byte Index => Player?.Index ?? 0;
        public PlayerInfo _originPlayer = new(true);
        public PlayerInfo Player
        {
            get
            {
                return field ?? _originPlayer;
            }
            set
            {
                field = value;
            }
        }
        public ServerInfo LastServer { get; set; } = null;
        #endregion

        #region 方法
        public override string ToString()
            => $"{Address}:{Name}_{Player.UUID}";
        public void Dispose()
        {
            if (Disposed)
                return;
            Disposed = true;
            lock (Data.Clients)
            {
                if (!Data.Clients.Remove(this))
                    Logs.Warn($"Abnormal remove of client data.");
            }
            State = ClientState.Disconnect;
            Adapter?.Dispose(true);
            TempAdapter?.Dispose(true);
        }
        #endregion
    }
}
