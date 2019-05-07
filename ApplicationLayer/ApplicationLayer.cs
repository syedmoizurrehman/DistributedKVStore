using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using AppUtilities;
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
        /// Represents all the <see cref="Node"/>s in the network. First item (Key: 0) will always be coordinator.
        /// </summary>
        public Dictionary<int, Node> NodeNetwork { get; }

        /// <summary>
        /// Gets whether this node represents this machine or any other machine in the network.
        /// </summary>
        public bool IsHost { get; internal set; }

        public IPAddress CoordinatorAddress { get; set; }

        private static int HashFunction(string key) => key.GetHashCode() % AppProperties.RingSize;

        internal Node()
        {
            IsHost = false;
        }

        public Node(IPAddress coordAddress, bool isHost = false, bool isClient = false)
        {
            NodeNetwork = new Dictionary<int, Node>();
            //Initiated = new Action(Initialize);

            Index = -1;                             // -1 indicated unassigned ID.
            IsHost = isHost;
            CoordinatorAddress = coordAddress;
            Status = NodeStatus.Node;
            PollDelay = 500;
            //Initiated();
        }

        public async Task Initialize()
        {
            if (IsHost)
            {
                Address = Network.GetHostIPAddress();
                if (Address.Equals(CoordinatorAddress))
                    Status = NodeStatus.Coordinator;

                switch (Status)
                {
                    case NodeStatus.Coordinator:
                        Index = 0;
                        // Add coordinator to network list.
                        NodeNetwork.Add(Index, this);
                        // Start Listening for client's request.
                        //await Network.ListenAsync(AppProperties.PortNumber);      // Debugging: Client's request will be received from console.
                        break;

                    case NodeStatus.Node:   // Only knows Coord's IP, nothing about the node network at this point.
                        NodeNetwork.Add(0, new Node(CoordinatorAddress) { Status = NodeStatus.Coordinator, Address = CoordinatorAddress });
                        await NodeNetwork[0].Initialize();
                        await SendJoinRequest();
                        Message M;
                        do M = await ListenAsync();
                        while (M.Type != MessageType.JoinResponse);

                        Index = M.NewNode.Index;
                        for (int i = 0; i < M.Network.Count; i++)
                        {
                            if (NodeNetwork.ContainsKey(M.Network[i].Index))
                                NodeNetwork[M.Network[i].Index] = M.Network[i];
                            else
                                NodeNetwork.Add(M.Network[i].Index, M.Network[i]);
                        }
                        break;
                }
                await Poll();
            }
            else
            {
                switch (Status)
                {
                    case NodeStatus.Node:
                        break;
                    case NodeStatus.Coordinator:
                        Index = 0;
                        break;
                    case NodeStatus.Client:
                        break;
                }
            }
        }

        public Task Write(string key, string value)
        {
            throw new NotImplementedException();
        }

        public void Read(string key)
        {
            for (int i = 0; i < AppProperties.RingSize; i++)
            {
                int ReplicaNodeIndex = HashFunction(key);
                var M = Message.ConstructKeyRequest(this, NodeNetwork[i], NodeNetwork, key);
                SendAsync(ReplicaNodeIndex, M);
            }
        }

        public Task SendJoinRequest()
        {
            return SendAsync(0, Message.ConstructJoinRequest(this, NodeNetwork[0]));
        }

        /// <summary>
        /// Sends to the node added most recently to the network of nodes.
        /// </summary>
        /// <returns></returns>
        public Task SendJoinResponse()
        {
            return SendAsync(NodeNetwork.Count - 1, Message.ConstructJoinResponse(this, NodeNetwork[NodeNetwork.Count - 1], NodeNetwork));
        }

        /// <summary>
        /// Introduces the most recently added node.
        /// </summary>
        /// <returns></returns>
        public Task SendIntroduction(int nodeIndex)
        {
            return SendAsync(nodeIndex, Message.ConstructJoinIntroduction(this, NodeNetwork[nodeIndex], NodeNetwork, NodeNetwork.Count - 1));
        }

        public Task Ping(int targetNodeIndex)
        {
            return SendAsync(targetNodeIndex, Message.ConstructPing(this, NodeNetwork[targetNodeIndex], NodeNetwork));
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

        /// <summary>
        /// Keep listening, respond if a message is received, continue listening.
        /// </summary>
        private async Task Poll()
        {
            switch (Status)
            {
                case NodeStatus.Coordinator:
                    while (true)
                    {
                        Message M = await ListenAsync();
                        switch (M.Type)
                        {
                            case MessageType.KeyAcknowledgement:
                                break;

                            case MessageType.ValueResponse:
                                break;

                            case MessageType.JoinRequest:
                                var N = M.Source;
                                N.Index = NodeNetwork.Count;
                                N.Status = NodeStatus.Node;
                                N.CoordinatorAddress = Address;
                                N.NodeNetwork?.Clear();   // Networks of other nodes are not stored.
                                if (NodeNetwork.ContainsKey(N.Index))
                                    NodeNetwork[N.Index] = N;
                                else
                                    NodeNetwork.Add(N.Index, N);
                                await SendJoinResponse();
                                await InitiateGossip(N);
                                break;

                            case MessageType.JoinIntroduction:
                                break;

                            case MessageType.Ping:
                                break;

                            // Coord should not receive these messages
                            //case MessageType.KeyRequest:
                            //case MessageType.KeyQuery:
                            //case MessageType.JoinResponse:
                            default:
                                throw new Exception("Coordinator received invalid message " + M.Type.ToString() + " from " + M.Source);
                        }
                    }

                case NodeStatus.Node:
                    while (true)
                    {
                        Message M = await ListenAsync();
                        switch (M.Type)
                        {
                            case MessageType.KeyRequest:
                                break;

                            case MessageType.KeyQuery:
                                break;

                            case MessageType.Ping:
                                break;

                            case MessageType.JoinResponse:
                                break;
                            case MessageType.JoinIntroduction:
                                break;

                            case MessageType.ValueResponse:
                                break;
                            case MessageType.JoinRequest:
                                break;
                            case MessageType.KeyAcknowledgement:
                                break;
                        }

                    }

            }
        }

        internal Task InitiateGossip(Node newNode)
        {
            if (NodeNetwork.Count < 3)
                return Task.CompletedTask;

            // Send a random node the information of the new node.
            int RandomNodeIndex;
            do RandomNodeIndex = ThreadSafeRandom.CurrentThreadsRandom.Next(NodeNetwork.Count);
            while (RandomNodeIndex == NodeNetwork.Count - 1);       // If the random generated node is new node, generate a different index.

            return SendIntroduction(RandomNodeIndex);
        }
    }
}