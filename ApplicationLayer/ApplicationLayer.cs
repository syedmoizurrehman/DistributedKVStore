using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AppUtilities;
using DataAccess;
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

        public bool IsDown { get; set; }

        public IPAddress CoordinatorAddress { get; set; }

        /// <summary>
        /// Gets or sets the number of nodes a gossip will be disseminated to before termination.
        /// </summary>
        public static int GossipCount { get; internal set; }

        private static List<int> GetHash(string key)
        {
            HashAlgorithm HashAlgo = HashAlgorithm.Create();
            HashAlgo.ComputeHash(Encoding.UTF8.GetBytes(key));
            var Indices = new List<int>();
            for (int i = 0; i < AppProperties.ReplicationFactor; i++)
            {
                int Index = Math.Abs(BitConverter.ToInt32(HashAlgo.Hash, i % HashAlgo.HashSize) % AppProperties.RingSize);
                while (Indices.Contains(Index) || Index == 0)
                {
                    Index++;
                    Index %= AppProperties.RingSize;
                }
                Indices.Add(Index);
            }
            return Indices;
        }

        internal Node()
        {
            IsHost = false;
            IsDown = false;
        }

        public Node(IPAddress coordAddress, bool isHost = false, bool isClient = false)
        {
            NodeNetwork = new Dictionary<int, Node>();

            Index = -1;                             // -1 indicated unassigned ID.
            IsHost = isHost;
            IsDown = false;
            CoordinatorAddress = coordAddress;
            Status = NodeStatus.Node;
            GossipCount = AppProperties.RingSize / 4;
            PollDelay = 500;
            if (IsHost)
            {
                Address = Network.GetHostIPAddress();
                if (Address.Equals(CoordinatorAddress))
                    Status = NodeStatus.Coordinator;
            }
        }

        public async Task Initialize()
        {
            if (IsHost)
            {
                switch (Status)
                {
                    case NodeStatus.Coordinator:
                        Index = 0;
                        // Add coordinator (self) to network list.
                        NodeNetwork.Add(Index, this);
                        break;

                    case NodeStatus.Node:   // Only knows Coord's IP, nothing about the node network at this point.
                        NodeNetwork.Add(0, new Node(CoordinatorAddress) { Status = NodeStatus.Coordinator, Address = CoordinatorAddress });
                        await NodeNetwork[0].Initialize();
                        await SendJoinRequest();
                        Message M;
                        do M = await ListenAsync();
                        while (M.Type != MessageType.JoinResponse && M.Source.Address != CoordinatorAddress);

                        Index = M.NewNode.Index;
                        UpdateNodeNetwork(M.Network);
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

        public async Task<KVTable> Read(string key)
        {
            switch (Status)
            {
                case NodeStatus.Node:
                    return await SqliteDatabase.GetValueAsync(key);

                case NodeStatus.Coordinator:
                    List<int> ReplicaIndices = GetHash(key);
                    List<Message> Responses = new List<Message>();
                    DateTimeOffset MaxTimeStamp = DateTimeOffset.MinValue;
                    int LatestIndex = -1;
                    for (int i = 0; i < 1/*AppProperties.RingSize*/; i++)
                    {
                        await SendKeyRequest(ReplicaIndices[i], key);
                        Message Response;
                        do Response = await ListenAsync();
                        while (Response.Type != MessageType.KeyAcknowledgement || !ReplicaIndices.Contains(Response.Source.Index));
                        if (string.IsNullOrEmpty(Response.Key))
                            continue;

                        Responses.Add(Response);
                        if (Response.KeyTimestamp > MaxTimeStamp)
                        {
                            MaxTimeStamp = Response.KeyTimestamp;
                            LatestIndex = i;
                        }
                    }
                    if (LatestIndex == -1)
                        return null;        // Key was not found on any replica.

                    await SendKeyQuery(Responses[LatestIndex].Source.Index, key);
                    Message ValueResponse;
                    do ValueResponse = await ListenAsync();
                    while (ValueResponse.Type != MessageType.ValueResponse && !ReplicaIndices.Contains(ValueResponse.Source.Index));
                    return new KVTable { Key = key, Value = ValueResponse.Value, TimeStamp = ValueResponse.KeyTimestamp };

                case NodeStatus.Client:
                    break;
            }
            return null;
        }

        private Task SendKeyQuery(int nodeIndex, string key)
        {
            var M = Message.ConstructKeyQuery(this, NodeNetwork[nodeIndex], NodeNetwork, key);
            return SendAsync(nodeIndex, M);
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
        /// Introduces the specified node to the network.
        /// </summary>
        /// <returns></returns>
        public Task SendIntroduction(int nodeIndex)
        {
            return SendAsync(nodeIndex,
                Message.ConstructJoinIntroduction(this, NodeNetwork[nodeIndex], NodeNetwork, NodeNetwork.Count - 1, GossipCount));
        }

        public Task Ping(int targetNodeIndex)
        {
            return SendAsync(targetNodeIndex, Message.ConstructPing(this, NodeNetwork[targetNodeIndex], NodeNetwork));
        }

        public Task SendKeyRequest(int nodeIndex, string key)
        {
            var M = Message.ConstructKeyRequest(this, NodeNetwork[nodeIndex], NodeNetwork, key);
            return SendAsync(nodeIndex, M);
        }

        public Task SendKeyAcknowledgment(int nodeIndex, string key, DateTimeOffset keyTimestamp)
        {
            var M = Message.ConstructKeyAcknowledgment(this, NodeNetwork[nodeIndex], NodeNetwork, key, keyTimestamp);
            return SendAsync(nodeIndex, M);
        }

        public Task SendValueResponse(int nodeIndex, string key, string value)
        {
            var M = Message.ConstructValueResponse(this, NodeNetwork[nodeIndex], NodeNetwork, key, value);
            return SendAsync(nodeIndex, M);
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
                            case MessageType.JoinRequest:
                                Console.WriteLine("Received join request. Sending back the ID to be assigned.");
                                var N = M.Source;
                                N.Index = NodeNetwork.Count;
                                N.Status = NodeStatus.Node;
                                N.CoordinatorAddress = Address;
                                N.NodeNetwork?.Clear();   // Networks of other nodes are not stored.
                                if (NodeNetwork.ContainsKey(N.Index))
                                    NodeNetwork[N.Index] = N;
                                else
                                    NodeNetwork.Add(N.Index, N);
                                await SendJoinResponse(); Console.WriteLine("Initiating Gossip protocol.");
                                await InitiateGossip(N);
                                KVTable X = await Read("A");
                                break;

                            case MessageType.JoinIntroduction:
                                break;

                            case MessageType.Ping:
                                break;

                            // Coord receives these messages only in specific methods.
                            //case MessageType.KeyAcknowledgement:
                            //case MessageType.ValueResponse:
                                //break;
                            
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
                                Console.WriteLine("Received Key request from coordinator.");
                                var X = await Read(M.Key);
                                if (X != null)  // if Key exists in local storage.
                                { await SendKeyAcknowledgment(0, X.Key, X.TimeStamp); Console.WriteLine("Key found. Sending key ACK."); }
                                else
                                { await SendKeyAcknowledgment(0, null, DateTimeOffset.MinValue); Console.WriteLine("Key not found. Notifying coordinator about absense of key."); }

                                break;

                            case MessageType.KeyQuery:
                                Console.WriteLine("Received Key query from coordinator. Sending back the value requested.");
                                var Z = await Read(M.Key);
                                await SendValueResponse(0, Z.Key, Z.Value);
                                break;

                            case MessageType.Ping:
                                break;

                            case MessageType.JoinResponse:
                                break;

                            case MessageType.JoinIntroduction:
                                Console.WriteLine("Received introduction of a new node. Initiating Gossip protocol.");
                                UpdateNodeNetwork(M.NewNode);
                                if (M.GossipCount > 0)
                                    await SendIntroduction(NodeNetwork.Count - 1);
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
            do RandomNodeIndex = ThreadSafeRandom.CurrentThreadsRandom.Next(0, AppProperties.RingSize);
            while (RandomNodeIndex == NodeNetwork.Count - 1);       // If the random generated node is new node, generate a different index.

            return SendIntroduction(RandomNodeIndex);
        }

        private void UpdateNodeNetwork(Dictionary<int, Node> nodeNetwork)
        {
            for (int i = 0; i < nodeNetwork.Count; i++)
            {
                if (NodeNetwork.ContainsKey(nodeNetwork[i].Index))
                    NodeNetwork[nodeNetwork[i].Index] = nodeNetwork[i];
                else
                    NodeNetwork.Add(nodeNetwork[i].Index, nodeNetwork[i]);
            }
        }

        private void UpdateNodeNetwork(Node newNode)
        {
            if (NodeNetwork.ContainsKey(newNode.Index))
                NodeNetwork[newNode.Index] = newNode;
            else
                NodeNetwork.Add(newNode.Index, newNode);
        }
    }
}