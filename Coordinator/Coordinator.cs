using ApplicationLayer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Coordinator
{
    static class Coordinator
    {
        static async Task Main(string[] args)
        {
            IPAddress[] Network = new IPAddress[2] { IPAddress.Parse("127.0.0.1"), IPAddress.Parse("127.0.0.1") };

            Node This = new Node(IPAddress.Parse("127.0.0.1"), Network);

            Node Client = new Node(IPAddress.Parse("127.0.0.1"), Network, false);

            await Task.Run(() => Client.Send(This, "Sent from client."));
        }
    }
}
