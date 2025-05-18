using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ChatServer
{
    class Program
    {
        static List<TcpClient> clients = new List<TcpClient>();
        static void Main(string[] args)
        {
            TcpListener server = new TcpListener(IPAddress.Any, 5000);
            server.Start();
            Console.WriteLine("Server started...");

            while (true)
            {
                TcpClient client = server.AcceptTcpClient();
                clients.Add(client);
                Console.WriteLine("Client connected.");
                Thread thread = new Thread(() => HandleClient(client));
                thread.Start();
            }
        }

        static void HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int byteCount;

            while ((byteCount = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                string message = Encoding.UTF8.GetString(buffer, 0, byteCount);
                Console.WriteLine("Received: " + message);
                Broadcast(message, client);
            }

            clients.Remove(client);
            client.Close();
        }

        static void Broadcast(string message, TcpClient sender)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            foreach (var client in clients)
            {
                if (client != sender)
                {
                    NetworkStream stream = client.GetStream();
                    stream.Write(buffer, 0, buffer.Length);
                }
            }
        }
    }
}