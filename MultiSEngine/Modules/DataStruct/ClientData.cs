using System.Net;
using System.Net.Sockets;
using System.Timers;
using MultiSEngine.Core.Adapter;

namespace MultiSEngine.Modules.DataStruct
{
    public class ClientData
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
            CAdapter = ca;

            TimeOutTimer = new()
            {
                Interval = Config.Instance.SwitchTimeOut,
                AutoReset = false
            };
            TimeOutTimer.Elapsed += OnTimeOut;
        }
        public ServerAdapter SAdapter
        {
            get;
            set;
        }
        private ClientAdapter _cAdapter;
        public ClientAdapter CAdapter
        {
            get => _cAdapter; set
            {
                _cAdapter = value;
                if (value != null)
                {
                    IP = (value.Connection.RemoteEndPoint as IPEndPoint)?.Address.ToString();
                    Port = (value.Connection.RemoteEndPoint as IPEndPoint)?.Port ?? -1;
                }
            }
        }
        internal Socket TempConnection { get; set; }
        internal ServerAdapter TempAdapter { get; set; }

        public ClientState State { get; set; } = ClientState.NewConnection;
        public string IP { get; set; }
        public int Port { get; set; }
        public string Address => $"{IP}:{Port}";
        public int SpawnX => Server is { SpawnX: >= 0 } ? Server.SpawnX : Player.WorldSpawnX;
        public int SpawnY => Server is { SpawnY: >= 0 } ? Server.SpawnY : Player.WorldSpawnY;
        public ServerInfo Server { get; set; }
        public string Name => Player?.Name ?? Address;
        public MSEPlayer Player { get; set; } = new();

        public Timer TimeOutTimer { get; set; }
        public bool Syncing { get; internal set; } = false;
        public bool Disposed { get; private set; } = false;

        protected void OnTimeOut(object sender, ElapsedEventArgs args)
        {
            if (State > ClientState.Switching && State < ClientState.InGame)
                this.SendErrorMessage(Localization.Get("Prompt_CannotConnect"));
            State = ClientState.ReadyToSwitch;
            
            if (TempAdapter is VisualPlayerAdapter vpa)
            {
                Logs.Warn($"[{Name}] timeout when request is switch to: {vpa.TempServer.Name}");
                vpa.Callback = null; 
                if (Server == null && vpa.TempServer == Config.Instance.DefaultServerInternal)
                {
                    this.SendErrorMessage($"No default server avilable, back to FakeWorld.");
                    Logs.Info($"No default server avilable, send [{Name}] to FakeWorld.");
                    (CAdapter as FakeWorldAdapter)?.BackToThere();
                }
            }

            TempConnection?.Shutdown(SocketShutdown.Both);
            TempConnection?.Dispose();
            TempConnection = null;
        }
        public void Dispose()
        {
            Data.Clients.Remove(this);
            Disposed = true;
            State = ClientState.Disconnect;
            SAdapter?.Stop(true);
            CAdapter?.Stop(true);
            SAdapter = null;
            CAdapter = null;
            Player = null;
            TimeOutTimer.Dispose();
            Server = null;
        }
    }
}
