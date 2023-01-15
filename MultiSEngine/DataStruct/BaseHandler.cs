using System;
using MultiSEngine.Core.Adapter;
using TrProtocol;

namespace MultiSEngine.DataStruct
{
    public abstract class BaseHandler
    {
        public BaseHandler(BaseAdapter parent)
        {
            Parent = parent;
        }

        public BaseAdapter Parent { get; init; }
        public ClientData Client => Parent.Client;
        public bool IsDisposed { get; internal set; }

        public virtual void Initialize() { }

        public virtual void Dispose() { IsDisposed = true; }

        public virtual bool RecieveData(MessageID msgType, byte[] data)
        {
            return false;
        }

        public virtual bool RecieveClientData(MessageID msgType, byte[] data) { return false; }
        public virtual bool RecieveServerData(MessageID msgType, byte[] data) { return false; }

        protected bool SendToClientDirect(ref Span<byte> data)
            => Parent.SendToClientDirect(ref data);
        protected bool SendToServerDirect(ref Span<byte> data)
            => Parent.SendToServerDirect(ref data);
        protected bool SendToClientDirect(byte[] data)
            => Parent.SendToClientDirect(data);
        protected bool SendToServerDirect(byte[] data)
            => Parent.SendToServerDirect(data);
        protected bool SendToClientDirect(Packet packet)
        {
            var data = packet.AsBytes().AsSpan();
            return SendToClientDirect(ref data);
        }
        protected bool SendToServerDirect(Packet packet)
        {
            var data = packet.AsBytes().AsSpan();
            return SendToServerDirect(ref data);
        }
    }
}
