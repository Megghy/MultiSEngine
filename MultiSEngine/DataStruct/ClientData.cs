using MultiSEngine.Core.Adapter;
using MultiSEngine.Modules;
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
        public ClientData(ClientAdapter ca = null)
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
        internal Socket TempConnection { get; set; }
        internal VisualPlayerAdapter TempAdapter { get; set; }

        public ClientState State { get; set; } = ClientState.NewConnection;
        public string IP { get; set; }
        public int Port { get; set; }
        public string Address => $"{IP}:{Port}";
        public short SpawnX => Server is { SpawnX: >= 0, SpawnY: >= 0 } ? Server.SpawnX : Player.WorldSpawnX;
        public short SpawnY => Server is { SpawnY: >= 0, SpawnY: >= 0 } ? Server.SpawnY : Player.WorldSpawnY;
        public ServerInfo Server { get; set; }
        public string Name => Player?.Name ?? Address;
        public MSEPlayer Player { get; set; } = new();

        public Timer TimeOutTimer { get; set; }
        public bool Syncing { get; internal set; } = false;
        public bool Disposed { get; private set; } = false;
        public byte Index => Player?.Index ?? 0;

        protected void OnTimeOut(object sender, ElapsedEventArgs args)
        {
            if (State == ClientState.RequestPassword)
                this.SendErrorMessage(Localization.Instance["Prompt_PasswordTimeout"]);
            else if (State >= ClientState.Switching && State < ClientState.InGame)
                this.SendErrorMessage(Localization.Instance["Prompt_CannotConnect", (TempAdapter as VisualPlayerAdapter)?.TempServer?.Name]);
            State = ClientState.ReadyToSwitch;

            if (TempAdapter is VisualPlayerAdapter vpa)
            {
                Logs.Warn($"[{Name}] timeout when request is switch to: {vpa.TempServer.Name}");
                vpa.Callback = null;
            }

            TempConnection?.Shutdown(SocketShutdown.Both);
            TempConnection?.Dispose();
            TempConnection = null;
        }
        public void Dispose()
        {
            Disposed = true;
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
    }
}
