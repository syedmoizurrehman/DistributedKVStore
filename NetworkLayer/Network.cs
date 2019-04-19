using AppUtilities;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace NetworkLayer
{
    public static class Network
    {
        /// <summary>
        /// Sends a message to the specified address asynchronously.
        /// </summary>
        /// <param name="receiverAddress"></param>
        /// <param name="port"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        public static async Task SendAsync(IPAddress receiverAddress, int port, string message)
        {
            IPEndPoint remoteEndPoint = new IPEndPoint(receiverAddress, port);

            var ClientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await ClientSocket.ConnectAsync(remoteEndPoint);
            byte[] Data = Encoding.ASCII.GetBytes(message);

            await ClientSocket.SendAsync(Data);

            ClientSocket.Shutdown(SocketShutdown.Both);
            ClientSocket.Close();
        }

        /// <summary>
        /// Listens for message from any host.
        /// </summary>
        /// <param name="port"></param>
        /// <returns></returns>
        public static async Task<string> ListenAsync(int port)
        {
            IPEndPoint LocalEndPoint = new IPEndPoint(IPAddress.Any, port);
            Socket SenderSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            SenderSocket.Bind(LocalEndPoint);
            SenderSocket.Listen(100);
            Socket HandlerSocket = await SenderSocket.AcceptAsync();
            var Buffer = new byte[30];
            int X = await HandlerSocket.ReceiveAsync(Buffer);
            Console.WriteLine(X + " bytes received.");
            Console.WriteLine(Encoding.ASCII.GetString(Buffer));
            return Encoding.ASCII.GetString(Buffer);
        }
    }
}