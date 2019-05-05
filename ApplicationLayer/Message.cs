﻿using System;
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
    }

    /// <summary>
    /// Immutable.
    /// </summary>
    public sealed class Message
    {
        public MessageType Type { get; private set; }

        public Node Source { get; private set; }

        public Node Destination { get; private set; }

        public Node[] Network { get; private set; }

        public Node NewNode { get; private set; }

        public string Key { get; private set; }

        public string Value { get; private set; }

        public DateTimeOffset KeyTimestamp { get; private set; }

        /// <summary>
        /// Request sent by a new node to coordinator to be introduced to the network.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="coordinator"></param>
        /// <returns></returns>
        public static Message ConstructJoinRequest(Node source, Node coordinator)
        {
            var Obj = new Message
            {
                Type = MessageType.JoinRequest,
                Source = source,
                Destination = coordinator,
            };
            Obj.Network = new Node[1] { coordinator };
            return Obj;
        }

        /// <summary>
        /// Returns a message which is used for introduction of new nodes to the network by the coordinator.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="nodeNetwork"></param>
        /// <param name="newNodeIndex">The index of new node in the node network array.</param>
        /// <returns></returns>
        public static Message ConstructJoinIntroduction(Node source, Node destination, Node[] nodeNetwork, int newNodeIndex)
        {
            var Obj = new Message
            {
                Type = MessageType.JoinIntroduction,
                Source = source,
                Destination = destination,
                Network = nodeNetwork,
            };
            Obj.NewNode = Obj.Network[newNodeIndex];
            return Obj;
        }

        /// <summary>
        /// Request sent by coordinator to nodes mapped to a key.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="nodeNetwork"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static Message ConstructKeyRequest(Node source, Node destination, Node[] nodeNetwork, string key)
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
        /// Ping.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="nodeNetwork"></param>
        /// <returns></returns>
        public static Message ConstructPing(Node source, Node destination, Node[] nodeNetwork)
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
        public static Message ConstructJoinResponse(Node coordinator, Node newNode, Node[] nodeNetwork)
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

        public string Serialize(bool shareNetwork = true)
        {
            StringBuilder Obj = new StringBuilder();
            Obj.Append("SOURCE:").AppendLine(Source.Address.ToString());
            Obj.Append("DESTINATION:").AppendLine(Destination.Address.ToString());
            Obj.Append("TYPE:").AppendLine(Type.ToString());
            Obj.Append("NODE-ID:").AppendLine(Source.Index.ToString());     // ID of Source node.
            Obj.Append("NODE-COUNT:").AppendLine(shareNetwork ? Network.Length.ToString() : "-1");     // Total number of nodes in network. -1 indicates network information was not shared.
            if (shareNetwork)
            {
                for (int i = 0; i < Network.Length; i++)
                {
                    //Obj.Append(i).AppendLine(":");
                    Obj.Append("ID:").AppendLine(Network[i].Index.ToString());
                    Obj.Append("STATUS:").AppendLine(Network[i].Status.ToString());
                    Obj.Append("ADDRESS:").AppendLine(Network[i].Address.ToString());
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
                    Obj.Append("NEW-ID:").AppendLine(NewNode.Index.ToString());         // The id indicating the index of new node in network array.
                    break;

                case MessageType.JoinIntroduction:
                    Obj.Append("NEW-ID:").AppendLine(NewNode.Index.ToString());         // The id indicating the index of new node in network array.
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
                                NewMessage.Network = new Node[Count];
                            else
                                break;

                            for (int i = 0; i < NewMessage.Network.Length; i++)
                            {
                                var N = new Node();
                                Line = Reader.ReadLine();
                                N.Index = Convert.ToInt32(Line);
                                Line = Reader.ReadLine();
                                N.Status = (NodeStatus)Enum.Parse(typeof(NodeStatus), Line);
                                Line = Reader.ReadLine();
                                N.Address = IPAddress.Parse(Line);
                                NewMessage.Network[i] = N;
                            }
                            LineNo += NewMessage.Network.Length * 3;
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
                                    NewMessage.NewNode = new Node { Index = Convert.ToInt32(Line) }; break;
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