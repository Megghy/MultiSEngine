using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiSEngine.Modules.DataStruct
{
    public class MSEPlayer
    {
        public int VersionNum { get; set; }
        public byte Index { get; set; }
        public string Name {  get; set; }
        public int SpawnX { get; set; }
        public int SpawnY {  get; set; }
        public int X { get; set; }
        public int Y {  get; set; }
    }
}
