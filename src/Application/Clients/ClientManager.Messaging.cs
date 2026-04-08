
namespace MultiSEngine.Application.Clients
{
    public static partial class ClientManager
    {
        public static async ValueTask<bool> SendDataToClientAsync(this ClientData client, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (client is null || client.Disposed)
                return false;
            if (client.Adapter?.ClientConnection is null)
                return false;
            if (buffer.Length < 3)
                return true;

            try
            {
#if DEBUG
                var span = buffer.Span;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[Send to CLIENT] <{BitConverter.ToInt16(span)} byte>, Length: {span.Length} - {(MessageID)span[2]}");
                Console.ResetColor();
#endif
                return await client.Adapter.SendToClientDirectAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logs.Warn($"Failed to send data to {client.Name}{Environment.NewLine}{ex}");
                return false;
            }
        }

        public static async ValueTask<bool> SendDataToServerAsync(this ClientData client, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (client is null || client.Disposed)
                return false;
            if (client.Adapter?.ServerConnection is null)
                return false;
            if (buffer.Length < 3)
                return true;

            try
            {
#if DEBUG
                var span = buffer.Span;
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine($"[Send to SERVER] <{BitConverter.ToInt16(span)} byte>, Length: {span.Length} - {(MessageID)span[2]}");
                Console.ResetColor();
#endif
                return await client.Adapter.SendToServerDirectAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logs.Info($"Failed to send data to server: {client.Name}{Environment.NewLine}{ex}");
                return false;
            }
        }

        public static async ValueTask SendMessageAsync(this ClientData client, string text, Color color, bool withPrefix = true)
        {
            if (client is null)
            {
                Console.WriteLine(text);
                return;
            }
            if (client.Adapter is not { } adapter)
                return;

            var message = withPrefix ? $"{Localization.Instance["Prefix"]}{text}" : text;
            await adapter
                .SendToClientDirectAsync(new NetTextModule
                {
                    TextS2C = new TextS2C
                    {
                        PlayerSlot = 255,
                        Text = Utils.LiteralText(message),
                        Color = color,
                    }
                })
                .ConfigureAwait(false);
        }

        public static ValueTask SendMessageAsync(this ClientData client, string text, bool withPrefix = true)
            => client.SendMessageAsync(text, Utils.Rgb(255, 255, 255), withPrefix);

        public static ValueTask SendInfoMessageAsync(this ClientData client, string text, bool withPrefix = true)
            => client.SendMessageAsync(text, Utils.Rgb(220, 220, 130), withPrefix);

        public static ValueTask SendSuccessMessageAsync(this ClientData client, string text, bool withPrefix = true)
            => client.SendMessageAsync(text, Utils.Rgb(165, 230, 155), withPrefix);

        public static ValueTask SendErrorMessageAsync(this ClientData client, string text, bool withPrefix = true)
            => client.SendMessageAsync(text, Utils.Rgb(220, 135, 135), withPrefix);

        public static void SendMessage(this ClientData client, string text, Color color, bool withPrefix = true)
            => _ = client.SendMessageAsync(text, color, withPrefix);

        public static void SendMessage(this ClientData client, string text, bool withPrefix = true)
            => client.SendMessage(text, Utils.Rgb(255, 255, 255), withPrefix);

        public static void SendInfoMessage(this ClientData client, string text, bool withPrefix = true)
            => client.SendMessage(text, Utils.Rgb(220, 220, 130), withPrefix);

        public static void SendSuccessMessage(this ClientData client, string text, bool withPrefix = true)
            => client.SendMessage(text, Utils.Rgb(165, 230, 155), withPrefix);

        public static void SendErrorMessage(this ClientData client, string text, bool withPrefix = true)
            => client.SendMessage(text, Utils.Rgb(220, 135, 135), withPrefix);

        public static async ValueTask BroadcastAsync(this ClientData client, string message, bool ignoreSelf = true)
        {
            foreach (var c in RuntimeState.ClientRegistry.Where(c => !ignoreSelf || c != client))
                await c.SendMessageAsync(message, Utils.Rgb(255, 255, 255), false).ConfigureAwait(false);
        }

        public static void Broadcast(this ClientData client, string message, bool ignoreSelf = true)
            => _ = client.BroadcastAsync(message, ignoreSelf);
    }
}


