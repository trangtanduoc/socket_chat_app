using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Server_chat
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        TcpListener server;
        Thread listenerThread;

        Dictionary<string, List<ClientInfo>> groups = new();
        Dictionary<string, List<string>> chatHistory = new();
        public MainWindow()
        {
            InitializeComponent();
            StartServer();
        }
        private void StartServer()
        {
            server = new TcpListener(IPAddress.Any, 5000);
            server.Start();
            listenerThread = new Thread(ListenForClients);
            listenerThread.IsBackground = true;
            listenerThread.Start();
            AppendChatLog("Server started on port 5000");
        }
        private void ListenForClients()
        {
            while (true)
            {
                try
                {
                    TcpClient client = server.AcceptTcpClient();
                    Thread clientThread = new Thread(() => HandleClient(client));
                    clientThread.IsBackground = true;
                    clientThread.Start();
                }
                catch { break; }
            }
        }
        private void SendUserListToClient(TcpClient client, string groupId)
        {
            if (!groups.ContainsKey(groupId)) return;

            var names = groups[groupId].Select(u => u.Name).ToList();
            string message = "[USERLIST]" + string.Join("|", names);

            SendToClient(client, message);
        }

        private void SendChatHistoryToClient(TcpClient client, string groupId)
        {
            if (!chatHistory.ContainsKey(groupId) || chatHistory[groupId].Count == 0)
                return;

            string history = "[HISTORY]" + string.Join("\n", chatHistory[groupId]);
            SendToClient(client, history);
        }

        private void SendToClient(TcpClient client, string msg)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] buffer = Encoding.UTF8.GetBytes(msg);
                stream.Write(buffer, 0, buffer.Length);
            }
            catch { }
        }
        private void HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int byteCount = stream.Read(buffer, 0, buffer.Length);
            string intro = Encoding.UTF8.GetString(buffer, 0, byteCount);

            string[] parts = intro.Split('|');
            string groupId = parts[0];
            string userName = parts[1];

            Dispatcher.Invoke(() =>
            {
                if (!groups.ContainsKey(groupId))
                {
                    groups[groupId] = new();
                    chatHistory[groupId] = new();
                    GroupList.Items.Add(groupId);
                }
                groups[groupId].Add(new ClientInfo { Name = userName, Tcp = client });
                SendUserListToClient(client, groupId);
                SendChatHistoryToClient(client, groupId);

                AppendChatLog($"{userName} joined group {groupId}");
                UpdateUserList(groupId);
            });

            while (true)
            {
                try
                {
                    byteCount = stream.Read(buffer, 0, buffer.Length);
                    if (byteCount == 0) break;

                    string message = Encoding.UTF8.GetString(buffer, 0, byteCount);
                    Dispatcher.Invoke(() =>
                    {
                        chatHistory[groupId].Add(message);
                        if (GroupList.SelectedItem?.ToString() == groupId)
                            ChatLog.AppendText(message + "\n");
                    });
                    BroadcastToGroup(groupId, message);
                }
                catch { break; }
            }
        }
        private void BroadcastToGroup(string groupId, string message)
        {
            if (!groups.ContainsKey(groupId)) return;
            foreach (var client in groups[groupId])
            {
                try
                {
                    NetworkStream stream = client.Tcp.GetStream();
                    byte[] buffer = Encoding.UTF8.GetBytes(message);
                    stream.Write(buffer, 0, buffer.Length);
                }
                catch { }
            }
        }
        private void AppendChatLog(string msg)
        {
            ChatLog.AppendText(msg + "\n");
        }

        private void UpdateUserList(string groupId)
        {
            UserList.Items.Clear();
            if (!groups.ContainsKey(groupId)) return;
            foreach (var client in groups[groupId])
                UserList.Items.Add(client.Name);
        }
        private void GroupList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (GroupList.SelectedItem == null) return;
            string selectedGroup = GroupList.SelectedItem.ToString();
            ChatLog.Text = string.Join("\n", chatHistory[selectedGroup]);
            UpdateUserList(selectedGroup);
        }
        private void SendAsServer_Click(object sender, RoutedEventArgs e)
        {
            string groupId = GroupList.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(groupId)) return;
            string msg = ServerMessageBox.Text;
            if (string.IsNullOrWhiteSpace(msg)) return;

            string fullMessage = $"Server: {msg}";
            chatHistory[groupId].Add(fullMessage);
            ChatLog.AppendText(fullMessage + "\n");
            BroadcastToGroup(groupId, fullMessage);
            ServerMessageBox.Clear();
        }
    }
    public class ClientInfo
    {
        public string Name { get; set; }
        public TcpClient Tcp { get; set; }
    }
}