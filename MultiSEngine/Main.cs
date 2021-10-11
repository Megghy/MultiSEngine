using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiSEngine
{
    public class Main
    {
        public static Core.Net Net { get; set; } = new();
        public static Modules.Logs Log { get; set; } = new();
        public static void Init()
        {
            Net.Init();
        }
    }
}
