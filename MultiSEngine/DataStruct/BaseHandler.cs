using MultiSEngine.Core.Adapter;

namespace MultiSEngine.DataStruct
{
    public abstract class BaseHandler(BaseAdapter parent)
    {
        public BaseAdapter Parent { get; init; } = parent;
        public ClientData Client => Parent.Client;
        public bool IsDisposed { get; internal set; }

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
        protected ValueTask<bool> SendToClientDirectAsync(Packet packet, CancellationToken cancellationToken = default)
            => Parent.SendToClientDirectAsync(packet, cancellationToken);
        protected ValueTask<bool> SendToServerDirectAsync(Packet packet, CancellationToken cancellationToken = default)
            => Parent.SendToServerDirectAsync(packet, cancellationToken);
    }

    public sealed class HandlerPacketContext
    {
        private readonly bool _fromServer;
        private readonly Lazy<Packet?> _packet;

        public HandlerPacketContext(MessageID messageId, ReadOnlyMemory<byte> data, bool fromServer)
        {
            MessageId = messageId;
            Data = data;
            _fromServer = fromServer;
            _packet = new Lazy<Packet?>(() => MultiSEngine.Utils.AsPacket(data, fromServer), LazyThreadSafetyMode.None);
        }

        public MessageID MessageId { get; }

        public ReadOnlyMemory<byte> Data { get; }

        public bool FromServer => _fromServer;

        public Packet? Packet
        {
            get => _packet.Value;
        }

        public bool PacketMaterialized => _packet.IsValueCreated;
    }
}
