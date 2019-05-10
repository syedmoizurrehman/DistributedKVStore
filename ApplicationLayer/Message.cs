using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace ApplicationLayer
{
    public enum MessageType
    {
        /// <summary>
        /// Message sent from Coordinator to key replicas.
        /// </summary>
        KeyRequest,

        /// <summary>
        /// Acknowledgement message sent by a key replica indicating the presence/absence of the key and the timestamp of the last update of value corresponding to the key.
        /// </summary>
        KeyAcknowledgement,
        
        /// <summary>
        /// Messsage from Coordinator requesting value of a particular key. 
        /// This message is sent after Coordinator has received the KeyAcknowledgement and selected the node with latest timestamp.
        /// </summary>
        KeyQuery,

        /// <summary>
        /// Message sent by a key replica as a response to KeyQuery containing the value corresponding to the key.
        /// </summary>
        ValueResponse,

        /// <summary>
        /// A simple ping message to check whether receiving node is available.
        /// </summary>
        Ping,

        /// <summary>
        /// The message sent from a node to coordinator requesting to be introduced into the network.
        /// </summary>
        JoinRequest,

        /// <summary>
        /// The response of JoinRequest sent from coordinator to the node with assigned ID.
        /// </summary>
        JoinResponse,

        /// <summary>
        /// The message sent from Coordinator to introduce a newly up node in network.
        /// </summary>
        JoinIntroduction,

        /// <summary>
        /// Request from Coordinator containing data to be written to a node
        /// </summary>
        WriteRequest,

        /// <summary>
        /// Acknowledgement of WriteRequest from the node indicating successful write.
        /// </summary>
        WriteAcknowledgement,

        /// <summary>
        /// Message from any node indicating a failure to execute a requested operation.
        /// </summary>
        FailureIndication,
    }

    /// <summary>
    /// Immutable.
    /// </summary>
    public sealed class Message
    {
        public MessageType Type { get; private set; }

        public Node Source { get; private set; }

        public Node Destination { get; private set; }

        public Dictionary<int, Node> Network { get; private set; }

        public Node NewNode { get; private set; }

        public string Key { get; private set; }

        public string Value { get; private set; }

        public string FailureMessage { get; private set; }

        public int GossipCount { get; private set; }

        public DateTimeOffset KeyTimestamp { get; private set; }

        /// <summary>
        /// Request sent by a new node to coordinator to be introduced to the network.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="coordinator"></param>
        /// <returns></returns>
        public static Message ConstructJoinRequest(Node source, Node coordinator)
        {
            var Msg = new Message
            {
                Type = MessageType.JoinRequest,
                Source = source,
                Destination = coordinator,
            };
            Msg.Network = new Dictionary<int, Node>(1);
            Msg.Network.Add(0, coordinator);
            return Msg;
        }

        /// <summary>
        /// Returns a message which is used for introduction of new nodes to the network by the coordinator.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="nodeNetwork"></param>
        /// <param name="gossipCount">The number of times this message will be disseminated throughout the network.</param>
        /// <param name="newNodeIndex">The index of new node in the node network array.</param>
        /// <returns></returns>
        public static Message ConstructJoinIntroduction(Node source, Node destination, Dictionary<int, Node> nodeNetwork, int newNodeIndex, int gossipCount)
        {
            var Msg = new Message
            {
                Type = MessageType.JoinIntroduction,
                Source = source,
                Destination = destination,
                Network = nodeNetwork,
                GossipCount = gossipCount,
            };
            Msg.NewNode = Msg.Network[newNodeIndex];
            return Msg;
        }

        /// <summary>
        /// Request sent by coordinator to nodes mapped to a key.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="nodeNetwork"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static Message ConstructKeyRequest(Node source, Node destination, Dictionary<int, Node> nodeNetwork, string key)
        {
            return new Message
            {
                Type = MessageType.KeyRequest,
                Source = source,
                Destination = destination,
                Network = nodeNetwork,
                Key = key,
            };
        }

        /// <summary>
        /// Sent from coordinator to the selected node for retrieval of value of specified key.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="nodeNetwork"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static Message ConstructKeyQuery(Node source, Node destination, Dictionary<int, Node> nodeNetwork, string key)
        {
            return new Message
            {
                Type = MessageType.KeyQuery,
                Source = source,
                Destination = destination,
                Network = nodeNetwork,
                Key = key,
            };
        }

        public static Message ConstructKeyAcknowledgment(Node source, Node destination, Dictionary<int, Node> nodeNetwork, string key, DateTimeOffset keyTimestamp)
        {
            return new Message
            {
                Type = MessageType.KeyAcknowledgement,
                Source = source,
                Destination = destination,
                Network = nodeNetwork,
                Key = key,
                KeyTimestamp = keyTimestamp
            };
        }

        internal static Message ConstructValueResponse(Node source, Node destination, Dictionary<int, Node> nodeNetwork, string key, string value)
        {
            return new Message
            {
                Type = MessageType.ValueResponse,
                Source = source,
                Destination = destination,
                Network = nodeNetwork,
                Key = key,
                Value = value
            };
        }

        /// <summary>
        /// Ping.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="nodeNetwork"></param>
        /// <returns></returns>
        public static Message ConstructPing(Node source, Node destination, Dictionary<int, Node> nodeNetwork)
        {
            return new Message()
            {
                Source = source,
                Destination = destination,
                Network = nodeNetwork,
            };
        }

        /// <summary>
        /// The response to a join request.
        /// </summary>
        /// <param name="coordinator"></param>
        /// <param name="newNode">The destination node which sent a join request.</param>
        /// <param name="nodeNetwork"></param>
        /// <param name="newNodeIndex">The index of new node in the node network array.</param>
        /// <returns></returns>
        public static Message ConstructJoinResponse(Node coordinator, Node newNode, Dictionary<int, Node> nodeNetwork)
        {
            var Obj = new Message
            {
                Type = MessageType.JoinResponse,
                Source = coordinator,
                Destination = newNode,
                Network = nodeNetwork,
                NewNode = newNode,
            };
            return Obj;
        }

        private Message()
        {
            //Network = new List<Node>();
        }

        /// <summary>
        /// Returns a string representation of this Message object.
        /// </summary>
        /// <param name="shareNetwork"></param>
        /// <returns></returns>
        public string Serialize(bool shareNetwork = true)
        {
            StringBuilder Obj = new StringBuilder();
            Obj.Append("SOURCE:").AppendLine(Source.Address.ToString());
            Obj.Append("DESTINATION:").AppendLine(Destination.Address.ToString());
            Obj.Append("TYPE:").AppendLine(Type.ToString());
            Obj.Append("SOURCE-ID:").AppendLine(Source.Index.ToString());     // ID of Source node.
            Obj.Append("NODE-COUNT:").AppendLine(shareNetwork ? Network.Count.ToString() : "-1");     // Total number of nodes in network. -1 indicates network information was not shared.
            if (shareNetwork)
            {
                for (int i = 0; i < Network.Count; i++)
                {
                    Obj.Append("ID:").AppendLine(Network[i].Index.ToString());
                    Obj.Append("STATUS:").AppendLine(Network[i].Status.ToString());
                    Obj.Append("ADDRESS:").AppendLine(Network[i].Address.ToString());
                    Obj.Append("IS-DOWN:").AppendLine(Convert.ToInt32(Network[i].IsDown).ToString());
                }
            }

            switch (Type)
            {
                case MessageType.KeyRequest:
                    Obj.Append("KEY: ").AppendLine(Key);
                    break;

                case MessageType.KeyAcknowledgement:
                    Obj.Append("KEY: ").AppendLine(Key);        // Assuming the requested nodes will always have the keys.
                    Obj.Append("TIMESTAMP: ").AppendLine(KeyTimestamp.ToUnixTimeSeconds().ToString());
                    break;

                case MessageType.KeyQuery:
                    Obj.Append("KEY: ").AppendLine(Key);
                    break;

                case MessageType.ValueResponse:
                    Obj.Append("VALUE: ").AppendLine(Value);
                    break;

                case MessageType.Ping:
                    break;

                case MessageType.JoinRequest:
                    break;

                case MessageType.JoinResponse:
                    Obj.Append("NEW-ID:").AppendLine(NewNode.Index.ToString());         // The id indicating the index of new node in network array. This will be used to get info. of new node from network sent with this message.
                    break;

                case MessageType.JoinIntroduction:
                    Obj.Append("NEW-ID:").AppendLine(NewNode.Index.ToString());         // The id indicating the index of new node in network array. This will be used to get info. of new node from network sent with this message.
                    Obj.Append("GOSSIP-COUNT:").AppendLine(GossipCount.ToString());     // This count will be decremented every time the message is received and sent to another node until the count reaches 0, at which point the gossip will stop.
                    break;

                case MessageType.WriteRequest:
                    Obj.Append("KEY: ").AppendLine(Key);
                    Obj.Append("VALUE: ").AppendLine(Value);
                    break;

                case MessageType.WriteAcknowledgement:
                    break;

                case MessageType.FailureIndication:
                    Obj.Append("FAILED:").AppendLine(FailureMessage);
                    break;
            }
            return Obj.ToString();
        }

        /// <summary>
        /// Returns a Message object by deserializing the specified string.
        /// </summary>
        /// <param name="messageString"></param>
        /// <returns></returns>
        public static Message Deserialize(string messageString)
        {
            var NewMessage = new Message();
            using (StringReader Reader = new StringReader(messageString))
            {
                int LineNo = 1;
                string Line;
                while ((Line = Reader.ReadLine()) != null)
                {
                    Line = Line.Split(':')[1];      // Split KEY:VALUE to VALUE
                    Line = Line.Trim();             // Remove line feed.
                    switch (LineNo)
                    {
                        case 1:
                            NewMessage.Source = new Node() { Address = IPAddress.Parse(Line) }; break;

                        case 2:
                            NewMessage.Destination = new Node() { Address = IPAddress.Parse(Line) }; break;

                        case 3:
                            NewMessage.Type = (MessageType)Enum.Parse(typeof(MessageType), Line); break;

                        case 4:
                            NewMessage.Source.Index = Convert.ToInt32(Line); break;

                        case 5:
                            var Count = Convert.ToInt32(Line);
                            if (Count != -1)
                                NewMessage.Network = new Dictionary<int, Node>(Count);
                            else
                                break;

                            for (int i = 0; i < Count; i++)
                            {
                                var N = new Node();
                                Line = Reader.ReadLine().Split(':')[1].Trim();
                                N.Index = Convert.ToInt32(Line);
                                Line = Reader.ReadLine().Split(':')[1].Trim();
                                N.Status = (NodeStatus)Enum.Parse(typeof(NodeStatus), Line);
                                Line = Reader.ReadLine().Split(':')[1].Trim();
                                N.Address = IPAddress.Parse(Line);
                                Line = Reader.ReadLine().Split(':')[1].Trim();
                                N.IsDown = Line.Equals("1");
                                NewMessage.Network[i] = N;
                            }
                            LineNo += NewMessage.Network.Count * 4;
                            break;

                        default:
                            switch (NewMessage.Type)
                            {
                                case MessageType.KeyRequest:
                                    NewMessage.Key = Line; break;

                                case MessageType.KeyAcknowledgement:
                                    NewMessage.Key = Line;
                                    NewMessage.KeyTimestamp = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(Line));
                                    break;

                                case MessageType.KeyQuery:
                                    NewMessage.Key = Line; break;

                                case MessageType.ValueResponse:
                                    NewMessage.Value = Line; break;

                                case MessageType.Ping:
                                    break;

                                case MessageType.JoinRequest:
                                    break;

                                case MessageType.JoinResponse:
                                    NewMessage.NewNode = new Node { Index = Convert.ToInt32(Line) }; break;

                                case MessageType.JoinIntroduction:
                                    NewMessage.NewNode = new Node { Index = Convert.ToInt32(Line) };
                                    Line = Reader.ReadLine().Split(':')[1].Trim();
                                    NewMessage.GossipCount = Convert.ToInt32(Line) - 1;     // Decrement the Gossip count to indicate the successful receipt of gossip.
                                    break;

                                case MessageType.WriteRequest:
                                    NewMessage.Key = Line; break;

                                case MessageType.WriteAcknowledgement:
                                    break;

                                case MessageType.FailureIndication:
                                    NewMessage.FailureMessage = Line; break;
                            }
                            break;
                    }
                    ++LineNo;
                }
            }
            return NewMessage;
        }

        public override string ToString() => Serialize();
    }
}
