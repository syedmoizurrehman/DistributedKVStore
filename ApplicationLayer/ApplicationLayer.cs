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

        public int RingSize
        {
            get
            {
                if (NodeNetwork.ContainsKey(-1))
                    return NodeNetwork.Count - 2;
                else
                    return NodeNetwork.Count - 1;
            }
        }

        public NodeStatus Status { get; set; }

        private DateTimeOffset _LastUpdated;
        /// <summary>
        /// Represents when the information of this node was last updated. 
        /// Must only be used for nodes which are stored as network in another host node.
        /// </summary>
        public DateTimeOffset LastUpdated
        {
            get { if (!IsHost) throw new Exception(nameof(LastUpdated) + " cannot be used for host machines."); return _LastUpdated; }
            set { if (!IsHost) throw new Exception(nameof(LastUpdated) + " cannot be used for host machines."); _LastUpdated = value; }
        }

        /// <summary>
        /// Delay in ms after which the node will check for new messages.
        /// </summary>
        public int PollDelay { get; set; }

        /// <summary>
        /// IPv4 Address of this node.
        /// </summary>
        public IPAddress Address { get; internal set; }

        /// <summary>
        /// Represents all the <see cref="Node"/>s in the network. Keys 0 and -1 are reserved for coordinator and client respectively. 
        /// Insertion/Updation must not be done on this directly. Use <see cref="UpdateNodeNetwork()"/> for this purpose
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

        private static List<int> GetHash(string key, int ringSize, Dictionary<int, Node> nodeNetwork)
        {
            HashAlgorithm HashAlgo = HashAlgorithm.Create();
            HashAlgo.ComputeHash(Encoding.UTF8.GetBytes(key));
            var Indices = new List<int>();
            for (int i = 0; i < AppProperties.ReplicationFactor; i++)
            {
                int Index = Math.Abs(BitConverter.ToInt32(HashAlgo.Hash, i % HashAlgo.HashSize) % ringSize);
                while (Indices.Contains(Index) || nodeNetwork.Keys.ElementAt(Index) == 0 || nodeNetwork.Keys.ElementAt(Index) == -1)
                {
                    Index++;
                    Index %= ringSize + 1;
                }
                Indices.Add(Index);
            }
            return Indices;
        }

        internal Node()
        {
            IsHost = false;
            IsDown = false;
            Index = -1;
            LastUpdated = DateTimeOffset.MinValue;
        }

        public Node(IPAddress coordAddress, bool isHost = false, bool isClient = false)
        {
            NodeNetwork = new Dictionary<int, Node>();

            Index = -1;                             // -1 indicated unassigned ID.
            IsHost = isHost;
            IsDown = false;
            LastUpdated = DateTimeOffset.MinValue;
            CoordinatorAddress = coordAddress;
            if (isClient)
                Status = NodeStatus.Client;
            else
                Status = NodeStatus.Node;
            GossipCount = RingSize / 4;
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
                        UpdateNodeNetwork(this);
                        await SqliteDatabase.InitializeLookupTable();
                        break;

                    case NodeStatus.Node:   // Only knows Coord's IP, nothing about the node network at this point.
                        UpdateNodeNetwork(new Node(CoordinatorAddress) { Status = NodeStatus.Coordinator, Address = CoordinatorAddress });
                        await NodeNetwork[0].Initialize();
                        await SendJoinRequest();
                        Message M;
                        /*do*/ M = await ListenAsync();
                        //while (M.Type != MessageType.JoinResponse && M.Source.Address != CoordinatorAddress);

                        Index = M.NewNode.Index;
                        UpdateNodeNetwork(M.Network);
                        await SqliteDatabase.InitializeDatabase();
                        break;

                    case NodeStatus.Client:
                        Index = -1;
                        UpdateNodeNetwork(new Node
                        {
                            Address = CoordinatorAddress,
                            Status = NodeStatus.Coordinator,
                            Index = 0,
                            CoordinatorAddress = CoordinatorAddress
                        });
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
                        Index = -1;
                        break;
                }
            }
        }

        public async Task<bool> Delete(string key, bool stabilize = true)
        {
            switch (Status)
            {
                case NodeStatus.Node:
                    if (await SqliteDatabase.DeleteValue(key))
                        return true;
                    return false;

                case NodeStatus.Coordinator:
                    CoordinatorLookupTable Lookup = await SqliteDatabase.GetLookupEntryAsync(key);
                    if (Lookup == null)
                    {
                        LogMessage("Key was not found in Coordinator lookup.");
                        return false;
                    }
                    // Use the RingSize from when the key was last updated/written.
                    int KeyRingSize = Lookup.RingSize;
                    if (stabilize && Lookup.RingSize != RingSize)
                        await Stabilize(key);

                    List<int> ReplicaIndices = GetHash(key, KeyRingSize, NodeNetwork);
                    for (int i = 0; i < KeyRingSize/*AppProperties.RingSize*/; i++)
                    {
                        int NodeId = NodeNetwork.Keys.ElementAt(ReplicaIndices[i]);
                        await SendDeleteRequest(NodeId, key);
                        Message Response = await ListenAsync();
                        if (Response != null)
                        {
                            NodeNetwork[NodeId].IsDown = false;
                            if (Response.Type == MessageType.FailureIndication)
                            {
                                LogMessage("Failed to delete key. " + Response.FailureMessage, NodeNetwork[NodeId]);
                                return false;
                            }
                            else
                                LogMessage("Key deleted successfully from replica# " + i + 1 + " out of " + KeyRingSize, NodeNetwork[NodeId]);
                        }
                        else
                        {
                            NodeNetwork[NodeId].IsDown = true;
                            LogMessage("Node is down.", NodeNetwork[NodeId]);
                        }
                    }
                    LogMessage("Deleting Coordinator lookup...");
                    await SqliteDatabase.DeleteLookupEntry(key);
                    LogMessage("Coordinator lookup deleted successfully.");
                    return true;

                case NodeStatus.Client:
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Stabilization algorithm to restore broken key-node mappings on a specified key.
        /// </summary>
        /// <param name="key"></param>
        /// <returns>True if operation was successful, otherwise false.</returns>
        public async Task<bool> Stabilize(string key)
        {
            if (Status != NodeStatus.Coordinator) throw new Exception("Stabilize called on non-coordinator node.");
            /*
            Delete key on all nodes using Lookup.RingSize for hashing
            Add key to all nodes using RingSize
            */
            LogMessage("Initiating stabilization. Getting database records from outdated mappings...");
            var Record = await Read(key, false);
            if (Record == null)
            {
                LogMessage("Stabilization failure. Failed to retrieve records from outdated mappings.");
                return false;
            }
            LogMessage("Outdated mappings retrieved. Proceeding to delete retrieved record...");
            if (!await Delete(key, false))
            {
                LogMessage("Stabilization failure. Failed to delete retrieve record from outdated mappings.");
                return false;
            }
            LogMessage("Succesfully deleted record from outdated mappings. Proceeding to write record with updated mappings...");
            if (!await Write(key, Record.Value))
            {
                LogMessage("Stabilization failure. Failed to write records with updated mappings.");    // TODO: Restore back the outdated mappings to prevent loss.
                return false;
            }
            LogMessage("Successfully written record with updated mappings. Updating Coordinator Lookup entry...");
            await SqliteDatabase.UpdateLookupEntry(key, RingSize);
            LogMessage("Successfully updated Coordinator Lookup entry. Stabilization successful.");
            return true;
        }

        private Task SendDeleteRequest(int nodeId, string key)
        {
            return SendAsync(nodeId, Message.ConstructDeleteRequest(this, NodeNetwork[nodeId], key, NodeNetwork));
        }

        private Task SendDeleteAcknowledgement(int nodeId, string key)
        {
            return SendAsync(nodeId, Message.ConstructDeleteAcknowledgement(this, NodeNetwork[0], key, NodeNetwork));
        }

        public async Task<bool> Write(string key, string value)
        {
            switch (Status)
            {
                case NodeStatus.Node:
                    Console.WriteLine("Writing to database...");
                    if (await SqliteDatabase.InsertKeyValuePairAsync(key, value))
                    {
                        Console.WriteLine("Write completed successfully.");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine("Failed to write to database. Key already exists.");
                        return false;
                    }

                case NodeStatus.Coordinator:
                    LogMessage("Contacting replica nodes for the given key...");
                    List<int> ReplicaIndices = GetHash(key, RingSize, NodeNetwork);
                    for (int i = 0; i < RingSize; i++)
                    {
                        int NodeId = NodeNetwork.Keys.ElementAt(ReplicaIndices[i]);
                        Message Response;
                        await SendWriteRequest(NodeId, key, value);
                        /*do*/ Response = await ListenAsync();
                        //while (Response.Type != MessageType.WriteAcknowledgement || !ReplicaIndices.Contains(Response.Source.Index));
                        if (Response != null)
                        {
                            NodeNetwork[NodeId].IsDown = false;
                            LogMessage("Received response from key replica.", NodeNetwork[NodeId]);
                            if (Response.Type == MessageType.FailureIndication)
                            {
                                LogMessage("Failed to write key. " + Response.FailureMessage, NodeNetwork[NodeId]);
                                return false;
                            }
                        }
                        else
                        {
                            NodeNetwork[NodeId].IsDown = true;
                            LogMessage("Node is down.", NodeNetwork[NodeId]);
                        }
                    }
                    Console.WriteLine("Key-Value pair inserted.");
                    await SqliteDatabase.InsertLookupEntry(key, RingSize);
                    return true;

                case NodeStatus.Client:
                    Console.WriteLine("Requesting Coordinator for database write...");
                    await SendClientWriteRequest(key, value);
                    Message CoordResponse;
                    /*do*/ CoordResponse = await ListenAsync();
                    //while (CoordResponse.Type != MessageType.ClientWriteResponse && CoordResponse.Source.Address != CoordinatorAddress);
                    if (CoordResponse.Type == MessageType.FailureIndication)
                    {
                        Console.WriteLine(CoordResponse.FailureMessage);
                        return false;
                    }
                    else
                    {
                        Console.WriteLine("Write successful.");
                        return true;
                    }
                default:
                    return false;
            }
        }

        public async Task<KVTable> Read(string key, bool stabilize = true)
        {
            switch (Status)
            {
                case NodeStatus.Node:
                    LogMessage("Reading from database...");
                    var ReadResult = await SqliteDatabase.GetValueAsync(key);
                    LogMessage("Database read succesfull.");
                    return ReadResult;

                case NodeStatus.Coordinator:
                    CoordinatorLookupTable Lookup = await SqliteDatabase.GetLookupEntryAsync(key);
                    if (Lookup == null)
                    {
                        LogMessage("Key not found.");
                        return null;
                    }
                    // Use the RingSize from when the key was last updated/written.
                    int KeyRingSize = Lookup.RingSize;
                    if (stabilize && Lookup.RingSize != RingSize)
                        await Stabilize(key);

                    List<int> ReplicaIndices = GetHash(key, KeyRingSize, NodeNetwork);
                    List<Message> Responses = new List<Message>();
                    DateTimeOffset MaxTimeStamp = DateTimeOffset.MinValue;

                    // Most updated Replica index
                    int LatestIndex = -1;
                    for (int i = 0; i < KeyRingSize/*AppProperties.RingSize*/; i++)
                    {
                        int NodeId = NodeNetwork.Keys.ElementAt(ReplicaIndices[i]);
                        await SendKeyRequest(NodeId, key);
                        Message Response;
                        /*do*/ Response = await ListenAsync();
                        //while (Response.Type != MessageType.KeyAcknowledgement || !ReplicaIndices.Contains(Response.Source.Index));
                        if (Response != null)
                        {
                            NodeNetwork[NodeId].IsDown = false;
                            if (!string.IsNullOrEmpty(Response.Key))
                            {
                                Responses.Add(Response);
                                if (Response.KeyTimestamp > MaxTimeStamp)
                                {
                                    MaxTimeStamp = Response.KeyTimestamp;
                                    LatestIndex = i;
                                }
                            }
                        }
                        else
                        {
                            NodeNetwork[NodeId].IsDown = true;
                            LogMessage("Node is down.", NodeNetwork[NodeId]);
                            Responses.Add(Message.ConstructEmptyMessage());     // Add empty message to make loop index work with LatestIndex.
                        }
                    }
                    if (LatestIndex == -1)
                        return null;        // Key was not found on any replica.

                    await SendKeyQuery(Responses[LatestIndex].Source.Index, key);
                    Message ValueResponse;
                    /*do*/ ValueResponse = await ListenAsync();
                    //while (ValueResponse.Type != MessageType.ValueResponse && !ReplicaIndices.Contains(ValueResponse.Source.Index));
                    return new KVTable { Key = key, Value = ValueResponse.Value, TimeStamp = ValueResponse.KeyTimestamp };

                case NodeStatus.Client:
                    LogMessage("Requesting Coordinator for the key...");
                    await SendClientReadRequest(key);
                    Message CoordResponse;
                    /*do*/ CoordResponse = await ListenAsync();
                    //while (CoordResponse.Type != MessageType.ClientReadResponse && CoordResponse.Source.Address != CoordinatorAddress);
                    if (CoordResponse.Type == MessageType.FailureIndication)
                    {
                        LogMessage(CoordResponse.FailureMessage, NodeNetwork[0]);
                        return null;
                    }
                    LogMessage("Read successful.");
                    Console.WriteLine();
                    return new KVTable { Key = CoordResponse.Key, Value = CoordResponse.Value, TimeStamp = CoordResponse.KeyTimestamp };
            }
            return null;
        }

        public Task SendClientReadRequest(string key)
        {
            var M = Message.ConstructClientReadRequest(this, NodeNetwork[0], key);
            return SendAsync(0, M);
        }

        public Task SendClientReadResponse(KVTable readResult)
        {
            Message M;
            if (readResult == null)
                M = Message.ConstructFailureMessage(this, NodeNetwork[-1], "Key does not exist.");

            else
                M = Message.ConstructClientReadResponse(
                    this,
                    NodeNetwork[-1],
                    readResult.Key, readResult.Value, readResult.TimeStamp);
            return SendAsync(-1, M);
        }

        public Task SendClientWriteRequest(string key, string value)
        {
            var M = Message.ConstructClientWriteRequest(this, NodeNetwork[0], key, value);
            return SendAsync(0, M);
        }

        public Task SendClientWriteResponse(string key, string value)
        {
            var M = Message.ConstructClientWriteResponse(this, NodeNetwork[-1], key, value);
            return SendAsync(-1, M);
        }

        public Task SendFailureIndication(int nodeIndex, string failureMessaeg)
        {
            var M = Message.ConstructFailureMessage(this, NodeNetwork[nodeIndex], failureMessaeg);
            return SendAsync(nodeIndex, M);
        }

        private Task SendKeyQuery(int nodeIndex, string key)
        {
            var M = Message.ConstructKeyQuery(this, NodeNetwork[nodeIndex], NodeNetwork, key);
            return SendAsync(nodeIndex, M);
        }

        public Task SendWriteRequest(int nodeIndex, string key, string value)
        {
            var M = Message.ConstructWriteRequest(this, NodeNetwork[nodeIndex], key, value, NodeNetwork);
            return SendAsync(nodeIndex, M);
        }

        public Task SendWriteAcknowledgement(string key)
        {
            return SendAsync(0, Message.ConstructWriteAcknowledgment(this, NodeNetwork[0], key));
        }

        public Task SendJoinRequest()
        {
            return SendAsync(0, Message.ConstructJoinRequest(this, NodeNetwork[0]));
        }

        /// <summary>
        /// Sends to the node added most recently to the network of nodes.
        /// </summary>
        /// <returns></returns>
        public Task SendJoinResponse(int newNodeId)
        {
            return SendAsync(newNodeId, Message.ConstructJoinResponse(this, NodeNetwork[newNodeId], NodeNetwork));
        }

        /// <summary>
        /// Introduces the specified node to the network.
        /// </summary>
        /// <returns></returns>
        public Task SendIntroduction(int nodeIndex, int newNodeId)
        {
            return SendAsync(nodeIndex,
                Message.ConstructJoinIntroduction(this, NodeNetwork[nodeIndex], NodeNetwork, newNodeId, GossipCount));
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

        public Task SendValueResponse(int nodeIndex, KVTable record)
        {
            var M = Message.ConstructValueResponse(this, NodeNetwork[nodeIndex], NodeNetwork, record.Key, record.Value, record.TimeStamp);
            return SendAsync(nodeIndex, M);
        }

        public Task SendAsync(int nodeIndex, Message message)
        {
            return Network.SendAsync(NodeNetwork[nodeIndex].Address, AppProperties.PortNumber, Encoding.ASCII.GetBytes(message.Serialize()));
        }

        public async Task<Message> ListenAsync()
        {
            byte[] Result = await Network.ListenAsync(AppProperties.PortNumber);
            if (Result == null) return Message.ConstructEmptyMessage();
            var M = Message.Deserialize(Encoding.ASCII.GetString(Result));
            //UpdateNodeNetwork(M.Source);
            return M;
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
                            case MessageType.ClientReadRequest:
                                {
                                    var Client = M.Source;
                                    UpdateNodeNetwork(Client);
                                    KVTable X = await Read(M.Key);
                                    await SendClientReadResponse(X);
                                    break;
                                }

                            case MessageType.ClientWriteRequest:
                                {
                                    var Client = M.Source;
                                    UpdateNodeNetwork(Client);
                                    if (await Write(M.Key, M.Value))
                                        await SendClientWriteResponse(M.Key, M.Value);

                                    else
                                        await SendFailureIndication(Client.Index, "Failed to write. Key possibly already exists."); ;
                                    break;
                                }

                            case MessageType.JoinRequest:
                                Console.WriteLine("Received join request. Sending back the assigned ID.");
                                var N = M.Source;
                                N.Index = Convert.ToInt32(N.Address.ToString().Split('.').Last());
                                N.Status = NodeStatus.Node;
                                N.CoordinatorAddress = Address;
                                N.NodeNetwork?.Clear();   // Networks of other nodes are not stored.
                                UpdateNodeNetwork(N);
                                await SendJoinResponse(N.Index);
                                await InitiateGossip(N);
                                break;

                            case MessageType.JoinIntroduction:
                                break;

                            case MessageType.Ping:
                                break;

                            case MessageType.Empty:     // Timed out.
                                // TODO: Update network by Gossip.
                                continue;

                            // Coord receives these messages only in specific methods.
                            //case MessageType.KeyAcknowledgement:
                            //case MessageType.ValueResponse:
                            //MessageType.WriteAcknowledgement
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
                            case MessageType.WriteRequest:
                                if (await Write(M.Key, M.Value))
                                    await SendWriteAcknowledgement(M.Key);
                                else
                                    await SendFailureIndication(0, "Failed to write to database. Key already exists.");
                                break;

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
                                await SendValueResponse(0, Z);
                                break;

                            case MessageType.Ping:
                                break;

                            case MessageType.DeleteRequest:
                                if (await Delete(M.Key))
                                {
                                    Console.WriteLine("Successfully deleted record from database.");
                                    await SendDeleteAcknowledgement(0, M.Key);
                                }
                                else
                                    await SendFailureIndication(0, "Error deleting key from database. Key possibly does not exist.");

                                break;

                            case MessageType.JoinIntroduction:
                                Console.WriteLine("Received introduction of a new node. Initiating Gossip protocol.");
                                UpdateNodeNetwork(M.Network);
                                //if (M.GossipCount > 0)
                                    //await InitiateGossip(NodeNetwork[M.NewNode.Index]) /*SendIntroduction(NodeNetwork.Count - 1)*/;
                                break;

                            case MessageType.Empty:     // Timed out.
                                // TODO: Gossip
                                continue;

                            case MessageType.JoinResponse:
                            case MessageType.ValueResponse:
                            case MessageType.JoinRequest:
                            case MessageType.KeyAcknowledgement:
                                break;
                        }

                    }
            }
        }

        internal Task InitiateGossip(Node newNode)
        {
            if (RingSize < 2)
                return Task.CompletedTask;

            Console.WriteLine("Initiating Gossip protocol.");
            // Send a random node the information of the new node.
            int RandomIdIndex;
            do RandomIdIndex = ThreadSafeRandom.CurrentThreadsRandom.Next(1, NodeNetwork.Count);
            while (RandomIdIndex == newNode.Index);       // If the random generated node is new node, generate a different index.

            return SendIntroduction(NodeNetwork.Keys.ElementAt(RandomIdIndex), newNode.Index);
        }

        private void UpdateNodeNetwork(Dictionary<int, Node> nodeNetwork)
        {
            foreach (int Key in nodeNetwork.Keys)
            {
                if (NodeNetwork.ContainsKey(Key))
                {
                    if (nodeNetwork[Key].LastUpdated > NodeNetwork[Key].LastUpdated)
                    {
                        LogMessage($"Updating node {Key}. Ring Size = " + RingSize);
                        NodeNetwork[Key] = nodeNetwork[Key];
                    }
                }
                else
                {
                    NodeNetwork.Add(nodeNetwork[Key].Index, nodeNetwork[Key]);
                    LogMessage($"Adding node {Key} to network. Ring Size = " + RingSize);
                }
            }
        }

        private void UpdateNodeNetwork(Node newNode)
        {
            if (NodeNetwork.ContainsKey(newNode.Index))
            {
                NodeNetwork[newNode.Index] = newNode;
                LogMessage($"Update node {newNode.Index}. Ring Size = " + RingSize);
            }
            else
            {
                NodeNetwork.Add(newNode.Index, newNode);
                LogMessage($"Adding node {newNode.Index} to network. Ring Size = " + RingSize);
            }
        }

        private void LogMessage(string message, Node node = null)
        {
            Console.WriteLine("--");
            Console.WriteLine(message);
            Console.WriteLine("Host ID: " + Index);
            if (node != null)
                Console.WriteLine("Remote Node ID: " + node.Index);
            Console.WriteLine("--");
        }
    }
}