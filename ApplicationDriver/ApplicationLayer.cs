using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NetworkLayer;

using AppProperties = AppUtilities.Properties;

namespace ApplicationLayer
{
    public enum NodeStatus
    {
        Node = 1,
        Coordinator = 2,
        Client = 3
    }

    /// <summary>
    /// Represents an instance of this application running on a network node.
    /// </summary>
    public class Node
    {
        private readonly Action Initiated;

        public NodeStatus Status { get; set; }

        /// <summary>
        /// Delay in ms after which the node will check for new messages.
        /// </summary>
        public int PollDelay { get; set; }

        /// <summary>
        /// IPv4 Address of this node.
        /// </summary>
        public IPAddress Address { get; }

        /// <summary>
        /// IPv4 Addresses of all the <see cref="Node"/>s in the network.
        /// </summary>
        public IPAddress[] NodeNetwork { get; }

        static int HashFunction(int ringSize, string key) => key.GetHashCode() % ringSize;

        public Node(IPAddress address, IPAddress[] nodeNetwork, bool startPolling = true)
        {
            Initiated = new Action(InitiatePolling);
            Address = address;
            PollDelay = 500;
            if (startPolling)
                Initiated();
        }

        public Task Send(Node receiver, string message)
        {
            return Network.SendAsync(receiver.Address, AppProperties.PortNumber, message);
        }

        private async void InitiatePolling()
        {
            while (true)
            {
                var T = Network.ListenAsync(AppProperties.PortNumber);
                await T;
                await Task.Delay(PollDelay);
            }
        }
    }
}