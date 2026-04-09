
namespace MultiSEngine.Protocol
{
    public abstract class BaseHandler(BaseAdapter parent)
    {
        private static readonly MessageID[] EmptySubscriptions = [];

        public BaseAdapter Parent { get; init; } = parent;
        public ClientData Client => Parent.Client;
        public bool IsDisposed { get; internal set; }
        public virtual IReadOnlyList<MessageID>? ClientMessageSubscriptions => EmptySubscriptions;
        public virtual IReadOnlyList<MessageID>? ServerMessageSubscriptions => EmptySubscriptions;

        public virtual void Initialize() { }

        public virtual void Dispose() { IsDisposed = true; }

        // 异步处理来自客户端/服务端的数据，返回 true 表示已处理并拦截后续转发
        public virtual ValueTask<bool> RecieveClientDataAsync(HandlerPacketContext context) => ValueTask.FromResult(false);
        public virtual ValueTask<bool> RecieveServerDataAsync(HandlerPacketContext context) => ValueTask.FromResult(false);

        protected ValueTask<bool> SendToClientDirectAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
            => Parent.SendToClientDirectAsync(data, cancellationToken);
        protected ValueTask<bool> SendToServerDirectAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
            => Parent.SendToServerDirectAsync(data, cancellationToken);
        protected async ValueTask<bool> SendToServerDirectAsync(CustomData.BaseCustomData data, CancellationToken cancellationToken = default)
        {
            using var rental = CustomData.BaseCustomData.Serialize(data);
            return await Parent.SendToServerDirectAsync(rental.Memory, cancellationToken).ConfigureAwait(false);
        }
        protected ValueTask<bool> SendToClientDirectAsync(INetPacket packet, CancellationToken cancellationToken = default)
            => Parent.SendToClientDirectAsync(packet, cancellationToken);
        protected ValueTask<bool> SendToServerDirectAsync(INetPacket packet, CancellationToken cancellationToken = default)
            => Parent.SendToServerDirectAsync(packet, cancellationToken);
    }

    public sealed class HandlerPacketContext
    {
        private readonly bool _fromServer;
        private object? _packet;
        private bool _packetMaterialized;

        public HandlerPacketContext(MessageID messageId, ReadOnlyMemory<byte> data, bool fromServer)
        {
            MessageId = messageId;
            Data = data;
            _fromServer = fromServer;
        }

        public MessageID MessageId { get; }

        public ReadOnlyMemory<byte> Data { get; }

        public bool FromServer => _fromServer;

        public object Packet
        {
            get
            {
                if (!_packetMaterialized)
                {
                    _packet = MultiSEngine.Utils.AsPacket(Data.Span, _fromServer);
                    _packetMaterialized = true;
                }

                return _packet!;
            }
        }

        public bool PacketMaterialized => _packetMaterialized;
    }
}


