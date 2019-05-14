using AppUtilities;
using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkLayer
{
    public static class Network
    {
        /// <summary>
        /// Returns <see cref="IPAddress"/> of this machine.
        /// </summary>
        /// <returns></returns>
        public static IPAddress GetHostIPAddress()
        {
            UnicastIPAddressInformation MostSuitableIp = null;

            foreach (NetworkInterface Network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (Network.OperationalStatus != OperationalStatus.Up)
                    continue;

                var Properties = Network.GetIPProperties();

                if (Properties.GatewayAddresses.Count == 0)
                    continue;

                foreach (UnicastIPAddressInformation Address in Properties.UnicastAddresses)
                {
                    if (Address.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    if (IPAddress.IsLoopback(Address.Address))
                        continue;

                    if (!Address.IsDnsEligible)
                    {
                        if (MostSuitableIp == null)
                            MostSuitableIp = Address;
                        continue;
                    }

                    // The best IP is the IP got from DHCP server
                    if (Address.PrefixOrigin != PrefixOrigin.Dhcp)
                    {
                        if (MostSuitableIp == null || !MostSuitableIp.IsDnsEligible)
                            MostSuitableIp = Address;
                        continue;
                    }

                    return Address.Address;
                }
            }

            return MostSuitableIp?.Address;
        }

        /// <summary>
        /// Sends a message to the specified address asynchronously.
        /// </summary>
        /// <param name="receiverAddress"></param>
        /// <param name="port"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        public static async Task SendAsync(IPAddress receiverAddress, int port, byte[] message)
        {
            IPEndPoint RemoteEndPoint = new IPEndPoint(receiverAddress, port);

            var ClientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            using (var TimeoutCancellationTokenSource = new CancellationTokenSource())
            {
                var T = ClientSocket.ConnectAsync(RemoteEndPoint);
                var CompletedTask = Task.WhenAny(T, Task.Delay(Properties.NetworkTimeout, TimeoutCancellationTokenSource.Token));
                if (CompletedTask == T)
                {
                    await T;
                    await ClientSocket.SendAsync(message);

                    ClientSocket.Shutdown(SocketShutdown.Both);
                    ClientSocket.Close();
                }
                else    // Timed out.
                {
                    ClientSocket.Close();

                }
            }
        }

        /// <summary>
        /// Listens for message from any host.
        /// </summary>
        /// <param name="port"></param>
        /// <returns></returns>
        public static async Task<byte[]> ListenAsync(int port)
        {
            var Buffer = new byte[65536];
            IPEndPoint LocalEndPoint = new IPEndPoint(IPAddress.Any, port);
            Socket SenderSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            SenderSocket.Bind(LocalEndPoint);
            SenderSocket.Listen(100);
            using (var TimeoutCancellationTokenSource = new CancellationTokenSource())
            {
                var T = SenderSocket.AcceptAsync();
                var CompletedTask = await Task.WhenAny(T, Task.Delay(Properties.NetworkTimeout, TimeoutCancellationTokenSource.Token));
                if (CompletedTask == T)
                {
                    Socket HandlerSocket = await T;
                    int X = await HandlerSocket.ReceiveAsync(Buffer);
                    Console.WriteLine(X + " bytes received.");
                    //SenderSocket.Shutdown(SocketShutdown.Both);
                    SenderSocket.Close();
                    byte[] ActualDataReceived = new byte[X];
                    Array.Copy(Buffer, 0, ActualDataReceived, 0, X);
                    Console.WriteLine(Encoding.ASCII.GetString(ActualDataReceived));
                    return ActualDataReceived;
                }
                else    // Timed out.
                {
                    SenderSocket.Close();
                    return null;
                }
            }
        }
    }
}