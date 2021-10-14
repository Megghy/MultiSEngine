using System;
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
        public ClientData(ClientAdapter ca)
        {
            if (ca is null)
                throw new ArgumentNullException(nameof(ca));
            ca.Client = this;
            CAdapter = ca;
            IP = (ca.Connection.RemoteEndPoint as IPEndPoint)?.Address.ToString();
            Port = (ca.Connection.RemoteEndPoint as IPEndPoint)?.Port ?? -1;

            TimeOutTimer = new()
            {
                Interval = Config.Instance.SwitchTimeOut,
                AutoReset = false
            };
            TimeOutTimer.Elapsed += OnTimeOut;
        }
        public ServerAdapter SAdapter { get; set; }
        public ClientAdapter CAdapter { get; set; }
        public Socket TempConnection { get; set; }

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
            State = ClientState.ReadyToSwitch;
            if (SAdapter is VisualPlayerAdapter vpa)
                vpa.Callback = null;
            this.SendErrorMessage($"Time out");
            Logs.Warn($"{Name} timeout when request is switch to server: {TempConnection.RemoteEndPoint}");
            TempConnection?.Shutdown(SocketShutdown.Both);
            TempConnection?.Dispose();
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
