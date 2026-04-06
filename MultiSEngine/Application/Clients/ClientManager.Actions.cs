
namespace MultiSEngine.Application.Clients
{
    public static partial class ClientManager
    {
        public static void ReadVersion(this ClientData client, ClientHello hello) => client.ReadVersion(hello.Version);

        public static void ReadVersion(this ClientData client, string version)
        {
            client.Player.VersionNum = version.StartsWith("Terraria") && int.TryParse(version[8..], out var v)
                ? v
                : Config.Instance.DefaultServerInternal.VersionNum;
            Logs.Info($"Version of {client.Name} is {RuntimeState.Convert(client.Player.VersionNum)}<{client.Player.VersionNum}>.");
        }

        public static ValueTask<(bool handled, bool continueSend)> HandleCommand(this ClientData client, string cmd)
            => Commands.CommandDispatcher.HandleCommand(client, cmd);

        public static async ValueTask Teleport(this ClientData client, int tileX, int tileY)
        {
            await client.Adapter.SendToClientDirectAsync(new Teleport
            {
                Bit1 = new BitsByte(),
                PlayerSlot = client.Player.Index,
                Position = new Vector2(tileX * 16, tileY * 16),
                Style = 0,
                ExtraInfo = 0,
            }).ConfigureAwait(false);
        }

        public static async ValueTask AddBuffAsync(this ClientData client, ushort buffID, int time = 60)
        {
            if (client?.Adapter is not { } adapter)
                return;

            await adapter.SendToClientDirectAsync(new AddPlayerBuff
            {
                OtherPlayerSlot = client.Player.Index,
                BuffType = buffID,
                BuffTime = time,
            }).ConfigureAwait(false);
        }

        public static void AddBuff(this ClientData client, ushort buffID, int time = 60)
            => _ = client.AddBuffAsync(buffID, time);

        public static async ValueTask CreateParticalAsync(this ClientData client, ParticleOrchestraType type, ParticleOrchestraSettings? setting = null)
        {
            if (client?.Adapter is not { } adapter)
                return;

            await adapter.SendToClientDirectAsync(new NetParticlesModule
            {
                ParticleType = type,
                Setting = setting ?? new ParticleOrchestraSettings
                {
                    MovementVector = new Vector2(0, 0),
                    PositionInWorld = new Vector2(client.Player.X, client.Player.Y),
                    UniqueInfoPiece = 0,
                    IndexOfPlayerWhoInvokedThis = client.Player.Index,
                }
            }).ConfigureAwait(false);
        }

        public static void CreatePartical(this ClientData client, ParticleOrchestraType type, ParticleOrchestraSettings? setting = null)
            => _ = client.CreateParticalAsync(type, setting);

        public static void CreatePartical(this ClientData client, ParticleOrchestraType type, Vector2 position, Vector2 movement = default)
        {
            client.CreatePartical(type, new ParticleOrchestraSettings
            {
                MovementVector = movement,
                PositionInWorld = position,
                UniqueInfoPiece = 0,
                IndexOfPlayerWhoInvokedThis = client.Player.Index,
            });
        }

        public static ClientData GetClientByName(string name)
            => RuntimeState.Clients.FirstOrDefault(c => c.Name == name);
    }
}


