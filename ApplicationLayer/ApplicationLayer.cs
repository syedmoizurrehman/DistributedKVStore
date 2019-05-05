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

        /// <summary>
        /// Gets the id which uniquely identifies this node on the network.
        /// </summary>
        public int Index { get; internal set; }

        public NodeStatus Status { get; set; }

        /// <summary>
        /// Delay in ms after which the node will check for new messages.
        /// </summary>
        public int PollDelay { get; set; }

        /// <summary>
        /// IPv4 Address of this node.
        /// </summary>
        public IPAddress Address { get; internal set; }

        /// <summary>
        /// Represents all the <see cref="Node"/>s in the network. First item will always be coordinator.
        /// </summary>
        public List<Node> NodeNetwork { get; }

        /// <summary>
        /// Gets whether this node represents this machine or any other machine in the network.
        /// </summary>
        public bool IsHost { get; private set; }

        public IPAddress CoordinatorAddress { get; set; }

        private static int HashFunction(string key) => key.GetHashCode() % AppProperties.RingSize;

        public Node()
        {
            IsHost = false;
        }

        public Node(IPAddress coordAddress, bool isHost = false)
        {
            NodeNetwork = new List<Node>();
            Initiated = new Action(Initialize);

            Index = -1;                             // -1 indicated unassigned ID.
            IsHost = isHost;
            CoordinatorAddress = coordAddress;
            Status = NodeStatus.Node;
            PollDelay = 500;
            Initiated();
        }

        public async void Initialize()
        {
            if (IsHost)
            {
                Address = await Network.GetIPAddress();
                if (Address == CoordinatorAddress)
                    Status = NodeStatus.Coordinator;

                switch (Status)
                {
                    case NodeStatus.Coordinator:
                        Index = 0;
                        // Add coordinator to network list.
                        NodeNetwork.Add(this);
                        // Start Listening for client's request.
                        //await Network.ListenAsync(AppProperties.PortNumber);      // Debugging: Client's request will be received from console.
                        break;

                    case NodeStatus.Node:   // Only knows Coord's IP, nothing about the node network at this point.
                        await SendJoinRequest();
                        Message M = await ListenAsync();
                        if (M.Type == MessageType.JoinResponse)
                        {
                            Index = M.NewNode.Index;
                            for (int i = 0; i < M.Network.Length; i++)
                                NodeNetwork.Add(M.Network[i]);

                            NodeNetwork = M.Network.ToList();
                        }
                        break;
                }
            }
        }

        public void Write(string key, string value)
        {
            throw new NotImplementedException();
        }

        public void Read(string key)
        {
            for (int i = 0; i < AppProperties.RingSize; i++)
            {
                int ReplicaNodeIndex = HashFunction(key);
                var M = Message.ConstructKeyRequest(this, NodeNetwork[i], NodeNetwork.ToArray(), key);
                SendAsync(ReplicaNodeIndex, M);
            }
        }

        public async Task SendJoinRequest()
        {
            await SendAsync(0, Message.ConstructJoinRequest(this, NodeNetwork[0]));
        }

        public Task Ping(int targetNodeIndex)
        {
            return SendAsync(targetNodeIndex, Message.ConstructPing(this, NodeNetwork[targetNodeIndex], NodeNetwork.ToArray()));
        }

        public Task SendAsync(int nodeIndex, Message message)
        {
            return Network.SendAsync(NodeNetwork[nodeIndex].Address, AppProperties.PortNumber, Encoding.ASCII.GetBytes(message.Serialize()));
        }

        public async Task<Message> ListenAsync()
        {
            byte[] Result = await Network.ListenAsync(AppProperties.PortNumber);
            return Message.Deserialize(Encoding.ASCII.GetString(Result));
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