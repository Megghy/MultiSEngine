namespace MultiSEngine.DataStruct.CustomData
{
    public abstract class BaseCustomData
    {
        public abstract string Name { get; }
        public abstract void InternalWrite(BinaryWriter writer);
        public abstract unsafe void InternalRead(BinaryReader reader);
        public virtual void OnRecievedData(ClientData client)
        {

        }

        public static Span<byte> Serialize(BaseCustomData data)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write((byte)MessageID.Unused15);
            var pos = ms.Position;
            bw.Write(data.Name);
            data.InternalWrite(bw);
            return ms.ToArray().AsSpan();
        }
    }
}
