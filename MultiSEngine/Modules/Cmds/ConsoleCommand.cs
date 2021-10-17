using MultiSEngine.Modules.DataStruct;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiSEngine.Modules.Cmds
{
    internal class ConsoleCommand : Core.Command.CmdBase
    {
        public override string Name { get; }
        public override bool ServerCommand => true;

        public override void Execute(ClientData client, List<string> parma)
        {
            throw new NotImplementedException();
        }
    }
}
