using MultiSEngine.Core.Adapter;
using MultiSEngine.Modules;
using System;
using System.Net;
using System.Net.Sockets;
using System.Timers;

namespace MultiSEngine.DataStruct
{
    public class ClientData : IClientAdapter<FakeWorldAdapter>, IServerAdapter<VisualPlayerAdapter>
    {
        public enum ClientState
        {
            Disconnect,
            NewConnection,
            ReadyToSwitch,
            Switching,
            RequestPassword,
            FinishSendInventory,
            SyncData,
            InGame,
        }
        public ClientData()
        {
            TimeOutTimer = new()
            {
                Interval = Config.Instance.SwitchTimeOut,
                AutoReset = false
            };
            TimeOutTimer.Elapsed += OnTimeOut;
        }
        public FakeWorldAdapter CAdapter { get; set; }
        public VisualPlayerAdapter SAdapter { get; set; }
        internal VisualPlayerAdapter TempAdapter { get; set; }
        public Timer TimeOutTimer { get; init; }

        #region 客户端信息
        public ClientState State { get; set; } = ClientState.NewConnection;
        public string IP => (CAdapter?.Connection?.RemoteEndPoint as IPEndPoint)?.Address?.ToString();
        public int Port => (CAdapter?.Connection?.RemoteEndPoint as IPEndPoint)?.Port ?? -1;
        public string Address => $"{IP}:{Port}"; public bool Syncing { get; internal set; } = false;
        public bool Disposed { get; private set; } = false;
        #endregion

        #region 常用的玩家信息
        public short SpawnX => Server is { SpawnX: >= 0, SpawnY: >= 0 } ? Server.SpawnX : Player.WorldSpawnX;
        public short SpawnY => Server is { SpawnY: >= 0, SpawnY: >= 0 } ? Server.SpawnY : Player.WorldSpawnY;
        public ServerInfo Server { get; set; }
        public string Name => Player?.Name ?? Address;
        public byte Index => Player?.Index ?? 0;
        public PlayerInfo Player { get; private set; } = new();
        #endregion

        #region 方法
        protected void OnTimeOut(object sender, ElapsedEventArgs args)
        {
            if (State == ClientState.RequestPassword)
                this.SendErrorMessage(Localization.Instance["Prompt_PasswordTimeout"]);
            else if (State >= ClientState.Switching && State < ClientState.InGame)
                this.SendErrorMessage(Localization.Instance["Prompt_CannotConnect", (TempAdapter as VisualPlayerAdapter)?.TargetServer?.Name]);
            State = ClientState.ReadyToSwitch;

            Logs.Warn($"[{Name}] timeout when request is switch to: {TempAdapter.TargetServer?.Name}");

            TempAdapter.Stop(true);
            TempAdapter = null;
        }
        public override string ToString()
            => $"{Address}:{Name}_{Player.UUID}";
        public void Dispose()
        {
            Disposed = true;
            if (!Data.Clients.Remove(this))
                Logs.Warn($"Abnormal remove of client data.");
            State = ClientState.Disconnect;
            SAdapter?.Stop(true);
            CAdapter?.Stop(true);
            TempAdapter?.Stop(true);
            SAdapter = null;
            CAdapter = null;
            TempAdapter = null;
            Player = null;
            TimeOutTimer.Dispose();
            Server = null;
        }
        #endregion
    }
}
