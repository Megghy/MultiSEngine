using System.Collections.Generic;

namespace MultiSEngine.Modules
{
    internal class Data
    {
        public static readonly string MessagePrefix = "MultiSEngine";
        public static List<DataStruct.ClientData> Clients { get; set; } = new();
        public static byte[] StaticSpawnSquareData { get; set; }
        public static void Init()
        {
            StaticSpawnSquareData = Utils.GetTileSquare(4150, 1150, 100, 100);
        }
    }
}
