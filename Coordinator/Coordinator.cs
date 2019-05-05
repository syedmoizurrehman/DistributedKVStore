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
        /// <summary>
        /// The instance of node class representing this machine.
        /// </summary>
        private static Node This { get; set; }

        static async Task<int> Main(string[] args)
        {
            string CoordinatorAddress = args[0].Remove(0, 1);       // Remove '-' from the argument.

            try
            {
                This = new Node(IPAddress.Parse(CoordinatorAddress), true);
                await This.Initialize();
                string Input = string.Empty;
                switch (This.Status)
                {
                    case NodeStatus.Node:
                        Console.WriteLine("Initializing this node...");
                        Console.WriteLine("Connecting to Coordinator...");
                        await This.SendJoinRequest();
                        break;

                    case NodeStatus.Coordinator:
                        Console.WriteLine("Coordinator detected. Listening for client's requests...");
                        Console.WriteLine("Switching to debug mode: Accepting client input from console.");
                        while (true)
                        {
                            Console.WriteLine("R: Request Read\tW: Request Write\tE: Exit");
                            Input = Console.ReadLine();
                            switch (Input)
                            {
                                case "R":
                                    Console.Write("Enter key to be read from database:");
                                    Input = Console.ReadLine();
                                    This.Read(Input);
                                    break;

                                case "W":
                                    Console.Write("Enter key to be written to database:");
                                    var Key = Console.ReadLine();
                                    Console.Write("Enter value to be written to database corresponding to entered key:");
                                    Input = Console.ReadLine();
                                    This.Write(Key, Input);
                                    break;

                                case "E":
                                    throw new Exception();
                            }
                        }
                        break;
                    
                    //case NodeStatus.Client:
                        //break;
                }

                while (true)
                {
                    //Console.WriteLine("S: Send Message.");
                    //Input = Console.ReadLine();
                    //switch (Input)
                    //{
                    //    case "S":
                    //        Console.Write("Enter message to send:");
                    //        Input = Console.ReadLine();
                    //        Console.WriteLine("Sending Message...");
                    //        This.SendAsync()
                    //        break;

                    //}
                    //Node Client = new Node(Network, false);
                    //await Task.Run(() => Client.Send(This, "Sent from client."));
                }
            }
            catch (Exception E)
            {
                Console.WriteLine(E.Message);
            }
            finally
            {
                Console.WriteLine("Exiting the program.");
            }
            return 0;
        }
    }
}
