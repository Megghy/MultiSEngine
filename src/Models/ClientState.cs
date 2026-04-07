namespace MultiSEngine.Models
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


