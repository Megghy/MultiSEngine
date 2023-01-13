using System.IO;
using MultiSEngine.Modules;

namespace MultiSEngine.DataStruct.CustomData
{
    [CustomPacketStuff.TokenCheck]
    internal class ExcuteMSECommand : CustomData
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
        public override void RecievedData(ClientData client)
        {
            if (ClientManager.GetClientByName(PlayerName) is { } tempClient)
            {
                tempClient.HandleCommand($"/{Command}");
            }
            else
            {
                client.HandleCommand($"/{Command}");
            }
            Logs.Info($"Receive command calls from the server [{client.Server.Name}] inside the tshock plugin: {Command}");
        }
    }
}
