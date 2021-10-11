using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace MultiSCore
{
    public class RawDataBuilder
    {
        public RawDataBuilder()
        {
            memoryStream = new();
            writer = new(memoryStream);
            writer.BaseStream.Position = 3L;
        }
        public RawDataBuilder(int packetType)
        {
            memoryStream = new();
            writer = new(memoryStream);
            writer.BaseStream.Position = 3L;
            long position = writer.BaseStream.Position;
            writer.BaseStream.Position = 2L;
            writer.Write((byte)packetType);
            writer.BaseStream.Position = position;
        }

        public RawDataBuilder PackSByte(sbyte num)
        {
            writer.Write(num);
            return this;
        }

        public RawDataBuilder PackByte(byte num)
        {
            writer.Write(num);
            return this;
        }

        public RawDataBuilder PackInt16(short num)
        {
            writer.Write(num);
            return this;
        }

        public RawDataBuilder PackUInt16(ushort num)
        {
            writer.Write(num);
            return this;
        }

        public RawDataBuilder PackInt32(int num)
        {
            writer.Write(num);
            return this;
        }

        public RawDataBuilder PackUInt32(uint num)
        {
            writer.Write(num);
            return this;
        }

        public RawDataBuilder PackUInt64(ulong num)
        {
            writer.Write(num);
            return this;
        }

        public RawDataBuilder PackSingle(float num)
        {
            writer.Write(num);
            return this;
        }

        public RawDataBuilder PackString(string str)
        {
            writer.Write(str);
            return this;
        }

        public RawDataBuilder PackRGB(Color color)
        {
            writer.Write(color.R);
            writer.Write(color.G);
            writer.Write(color.B);
            return this;
        }
        public RawDataBuilder PackVector2(Vector2 v)
        {
            writer.Write(v.X);
            writer.Write(v.Y);
            return this;
        }

        private void UpdateLength()
        {
            long position = writer.BaseStream.Position;
            writer.BaseStream.Position = 0L;
            writer.Write((short)position);
            writer.BaseStream.Position = position;
        }

        public static string ByteArrayToString(byte[] ba)
        {
            StringBuilder stringBuilder = new(ba.Length * 2);
            foreach (byte b in ba)
            {
                stringBuilder.AppendFormat("{0:x2}", b);
            }
            return stringBuilder.ToString();
        }

        public byte[] GetByteData()
        {
            UpdateLength();
            return memoryStream.ToArray();
        }

        public MemoryStream memoryStream;

        public BinaryWriter writer;
    }
}
