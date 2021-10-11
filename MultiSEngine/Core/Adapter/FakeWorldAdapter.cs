using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using MultiSEngine.Modules.DataStruct;
using TrProtocol;

namespace MultiSEngine.Core.Adapter
{
    internal class FakeWorldAdapter : AdapterBase
    {
        public FakeWorldAdapter(ClientData client, Socket connection) : base(client, connection)
        {
        }

        public override bool GetData(Packet packet)
        {
            throw new NotImplementedException();
        }

        public override void SendData(Packet packet)
        {
            throw new NotImplementedException();
        }
    }
}
