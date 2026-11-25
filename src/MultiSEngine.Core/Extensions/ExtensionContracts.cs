using System.Collections.Immutable;
using MultiSEngine.Core.Configuration;
using MultiSEngine.Core.Logging;
using MultiSEngine.Core.Security;
using MultiSEngine.Core.Sessions;
using MultiSEngine.Core.Transfers;
using TrProtocol;

namespace MultiSEngine.Core.Extensions;

public sealed record PluginMetadata(
    string Id,
    string Name,
    Version Version,
    string Author = "",
    string Description = "");

public interface IMsePlugin : IAsyncDisposable
{
    PluginMetadata Metadata { get; }
    ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken);
}

public interface IPluginContext
{
    IProxyLogger Logger { get; }
    IConfigStore Config { get; }
    ILocalizationStore Localization { get; }
    ISessionRegistry Sessions { get; }
    IServerProbeService Probes { get; }
    CancellationToken UnloadToken { get; }

    IDisposable RegisterPacketMiddleware(IPacketMiddleware middleware);
    IDisposable RegisterCommand(ICommand command);
    IDisposable RegisterCustomPacket(ICustomPacketDefinition definition);

    IDisposable OnPlayerJoining(Func<PlayerJoiningEvent, CancellationToken, ValueTask> handler);
    IDisposable OnPlayerJoined(Func<PlayerJoinedEvent, CancellationToken, ValueTask> handler);
    IDisposable OnPlayerLeft(Func<PlayerLeftEvent, CancellationToken, ValueTask> handler);
    IDisposable OnPreSwitch(Func<PreSwitchEvent, CancellationToken, ValueTask> handler);
    IDisposable OnPostSwitch(Func<PostSwitchEvent, CancellationToken, ValueTask> handler);
    IDisposable OnPlayerChat(Func<PlayerChatEvent, CancellationToken, ValueTask> handler);
    IDisposable OnCustomPacketReceived(Func<CustomPacketReceivedEvent, CancellationToken, ValueTask> handler);

    ValueTask BroadcastAsync(string text, Microsoft.Xna.Framework.Color color, string? serverFilter = null, bool withPrefix = true, CancellationToken cancellationToken = default);
}

public enum PacketDirection
{
    ClientToServer,
    ServerToClient,
}

public enum PacketAction
{
    Forward,
    Drop,
    Replace,
}

public delegate INetPacket PacketDeserializeDelegate();

public sealed class PacketMiddlewareContext
{
    public PacketMiddlewareContext(
        ISessionHandle session,
        PacketDirection direction,
        MessageID messageId,
        ReadOnlyMemory<byte> rawFrame,
        PacketDeserializeDelegate packetFactory)
    {
        Session = session;
        Direction = direction;
        MessageId = messageId;
        RawFrame = rawFrame;
        _packetFactory = packetFactory;
    }

    private readonly PacketDeserializeDelegate _packetFactory;
    private INetPacket? _cachedPacket;

    public ISessionHandle Session { get; }
    public PacketDirection Direction { get; }
    public MessageID MessageId { get; }
    public ReadOnlyMemory<byte> RawFrame { get; }
    public PacketAction Action { get; private set; } = PacketAction.Forward;
    public ReadOnlyMemory<byte> ReplacementFrame { get; private set; }
    public INetPacket? ReplacementPacket { get; private set; }

    public INetPacket GetPacket() => _cachedPacket ??= _packetFactory();

    public void Drop()
    {
        Action = PacketAction.Drop;
        ReplacementFrame = default;
        ReplacementPacket = null;
    }

    public void ReplaceWith(INetPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        Action = PacketAction.Replace;
        ReplacementPacket = packet;
        ReplacementFrame = default;
    }

    public void ReplaceWithRaw(ReadOnlyMemory<byte> frame)
    {
        if (frame.Length < 3)
            throw new ArgumentException("Terraria frame must be at least 3 bytes.", nameof(frame));
        Action = PacketAction.Replace;
        ReplacementFrame = frame;
        ReplacementPacket = null;
    }
}

public interface IPacketMiddleware
{
    int Priority => 0;
    ImmutableHashSet<MessageID>? ClientSubscriptions => null;
    ImmutableHashSet<MessageID>? ServerSubscriptions => null;
    ValueTask HandleAsync(PacketMiddlewareContext context, CancellationToken cancellationToken);
}

public interface ICustomPacketDefinition
{
    string PacketType { get; }
    bool AllowFromClient => false;
    bool AllowFromServer => true;
    ValueTask HandleAsync(CustomPacketContext context, CancellationToken cancellationToken);
}

public sealed record CustomPacketContext(
    ISessionHandle Session,
    PacketDirection Direction,
    string PacketType,
    ReadOnlyMemory<byte> Payload,
    bool IsAuthenticated,
    ServerDescriptor? SourceServer);

public interface ICommand
{
    string Name { get; }
    IReadOnlyList<string> Aliases => [];
    CommandSourceKind AllowedSources { get; }
    string Usage { get; }
    string Description { get; }
    ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken);
}
