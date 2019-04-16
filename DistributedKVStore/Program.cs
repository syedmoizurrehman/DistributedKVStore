﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NetworkLayer;

namespace DistributedKVStore
{
    public enum NodeStatus
    {
        Coordinator = 1,
        Node = 2,
        Client = 3
    }

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

        static int HashFunction(int ringSize, string key) => key.GetHashCode() % ringSize;

        public Node(IPAddress address)
        {
            Initiated = new Action(InitiatePolling);
            Address = address;
            PollDelay = 5000;
            Initiated();
        }

        private async void InitiatePolling()
        {
            while (true)
            {
                await Network.ReceiveAsync(Address, 11000).ConfigureAwait(false);
                await Task.Delay(PollDelay).ConfigureAwait(false);
            }
        }
    }

    static class Program
    {
        static async Task Main(string[] args)
        {
            Node This = new Node(IPAddress.Parse("127.0.0.1"));
            if (args[0].Equals("-coord"))
                This.Status = NodeStatus.Coordinator;

            Console.ReadKey();
        }
    }
}