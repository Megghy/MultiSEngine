using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiSEngine.DataStruct
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
}
