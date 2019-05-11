using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace AppUtilities
{
    public static class SocketExtensions
    {
        /// <summary>
        /// Asynchronously receives data from a bound <see cref="Socket"/> into a buffer.
        /// </summary>
        /// <param name="handlerSocket"></param>
        /// <param name="buffer">The byte array to store received data in.</param>
        /// <returns>The number of bytes received.</returns>
        public static async Task<int> ReceiveAsync(this Socket handlerSocket, byte[] buffer, SocketFlags socketFlags = SocketFlags.None)
        {
            if (handlerSocket == null) throw new ArgumentNullException(nameof(handlerSocket));
            int BytesReceived = 0;
            var ReceiveTaskCompletion = new TaskCompletionSource<int>();
            do
            {
                int ReceiveBufferSize = handlerSocket.Available;
                BytesReceived += ReceiveBufferSize;
                handlerSocket.BeginReceive(buffer, 0, ReceiveBufferSize, socketFlags, ReceiveCompletedResult =>
                {
                    try
                    {
                        ReceiveTaskCompletion.TrySetResult(handlerSocket.EndReceive(ReceiveCompletedResult));
                    }
                    catch (OperationCanceledException)
                    {
                        ReceiveTaskCompletion.TrySetCanceled();
                    }
                    catch (Exception exc)
                    {
                        ReceiveTaskCompletion.TrySetException(exc);
                    }
                }, null);
                await ReceiveTaskCompletion.Task;

            } while (handlerSocket.Available > 0);
            return BytesReceived;
        }

        /// <summary>
        /// Asynchronously sends data to a connected <see cref="Socket"/>.
        /// </summary>
        /// <param name="ClientSocket"></param>
        /// <param name="buffer">The byte array containing the data to be sent.</param>
        /// <returns></returns>
        public static async Task SendAsync(this Socket ClientSocket, byte[] buffer)
        {
            if (ClientSocket == null) throw new ArgumentNullException(nameof(ClientSocket));
            var SendTaskCompletion = new TaskCompletionSource<int>();
            ClientSocket.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, SendCompletedResult =>
            {
                try
                {
                    SendTaskCompletion.TrySetResult(ClientSocket.EndSend(SendCompletedResult));
                }
                catch (OperationCanceledException)
                {
                    SendTaskCompletion.TrySetCanceled();
                }
                catch (Exception E)
                {
                    SendTaskCompletion.TrySetException(E);
                }
            }, null);
            await SendTaskCompletion.Task;
        }
    }
}