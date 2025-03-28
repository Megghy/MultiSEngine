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

        public virtual bool RecieveData(MessageID msgType, ref Span<byte> data, ref bool modified)
        {
            return false;
        }

        public virtual bool RecieveClientData(MessageID msgType, Span<byte> data) { return false; }
        public virtual bool RecieveServerData(MessageID msgType, Span<byte> data) { return false; }

        protected bool SendToClientDirect(Span<byte> data)
            => Parent.SendToClientDirect(data);
        protected bool SendToServerDirect(Span<byte> data)
            => Parent.SendToServerDirect(data);
        protected bool SendToServerDirect(CustomData.BaseCustomData data)
            => Parent.SendToServerDirect(CustomData.BaseCustomData.Serialize(data));
        protected bool SendToClientDirect(NetPacket packet)
        {
            var data = packet.AsBytes();
            return SendToClientDirect(data);
        }
        protected bool SendToServerDirect(NetPacket packet)
        {
            var data = packet.AsBytes();
            return SendToServerDirect(data);
        }
    }
}
