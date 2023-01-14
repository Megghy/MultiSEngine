using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrProtocol.Packets;

namespace MultiSEngine.DataStruct
{
    public class CharacterData
    {
        public short Health;
        public short Mana;
        public short HealthMax;
        public short ManaMax;
        public SyncPlayer Info { get; set; }
        public WorldData WorldData { get; set; }
        public SyncEquipment[] Inventory { get; set; } = new SyncEquipment[350];
    }
}
