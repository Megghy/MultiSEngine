using MultiSEngine.Modules;

namespace MultiSEngine.DataStruct.CustomData
{
    internal class ExcuteMSECommand : BaseCustomData
    {
        public override string Name => "MultiSEngine.ExcuteMSECommand";
        public string PlayerName { get; set; }
        public string Command { get; set; }

        public override void InternalRead(BinaryReader reader)
        {
            PlayerName = reader.ReadString();
            Command = reader.ReadString();
        }

        public override void InternalWrite(BinaryWriter writer)
        {
            writer.Write(PlayerName);
            writer.Write(Command);
        }
        public override async ValueTask OnRecievedData(ClientData client)
        {
            if (ClientManager.GetClientByName(PlayerName) is { } tempClient)
            {
                await tempClient.HandleCommand($"/{Command}").ConfigureAwait(false);
            }
            else
            {
                await client.HandleCommand($"/{Command}").ConfigureAwait(false);
            }
            Logs.Info($"Receive command calls from the server [{client.CurrentServer.Name}] inside the tshock plugin: {Command}");
        }
    }
}
