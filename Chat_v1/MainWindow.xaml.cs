using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Chat_v1
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        TcpClient client;
        NetworkStream stream;
        Thread receiveThread;

        string currentGroup = "";
        string userName = "";

        // Track joined groups
        Dictionary<string, List<string>> groupMessages = new();
        Dictionary<string, List<string>> groupUsers = new();
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string groupId = group.Text.Trim();
                userName = client_name.Text.Trim();

                if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(groupId))
                {
                    MessageBox.Show("Please enter both Client Name and Group ID.");
                    return;
                }

                client = new TcpClient("192.168.1.184", 5000);
                stream = client.GetStream();

                // Send intro message with group and name
                string intro = $"{groupId}|{userName}";
                byte[] introBytes = Encoding.UTF8.GetBytes(intro);
                stream.Write(introBytes, 0, introBytes.Length);

                // Start receiving
                receiveThread = new Thread(() => ReceiveMessages(groupId));
                receiveThread.IsBackground = true;
                receiveThread.Start();

                if (!groupMessages.ContainsKey(groupId))
                {
                    groupMessages[groupId] = new List<string>();
                    groupUsers[groupId] = new List<string>();
                    GroupSelector.Items.Add(groupId);
                }

                GroupSelector.SelectedItem = groupId;
                currentGroup = groupId;

                AppendChat(groupId, "Connected to group " + groupId);
            }
            catch (Exception ex)
            {
                AppendChat(currentGroup, "Connection failed: " + ex.Message);
            }
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            if (stream == null || !stream.CanWrite) return;

            string msg = message.Text.Trim();
            if (!string.IsNullOrEmpty(msg))

                if (!string.IsNullOrEmpty(msg))
            {
                string fullMessage = $"{userName} (Group {currentGroup}): {msg}";
                byte[] buffer = Encoding.UTF8.GetBytes(fullMessage);
                stream.Write(buffer, 0, buffer.Length);

                AppendChat(currentGroup, "You: " + msg);
                message.Clear();
            }
        }

        private void ReceiveMessages(string groupId)
        {
            byte[] buffer = new byte[2048];
            int byteCount;

            try
            {
                while ((byteCount = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, byteCount);

                    Dispatcher.Invoke(() =>
                    {
                        if (message.StartsWith("[HISTORY]"))
                        {
                            string history = message.Substring(9);
                            string[] lines = history.Split('\n');

                            if (!groupMessages.ContainsKey(groupId))
                                groupMessages[groupId] = new List<string>();

                            groupMessages[groupId].AddRange(lines);

                            if (currentGroup == groupId)
                                chat_box.Text = string.Join("\n", groupMessages[groupId]);
                        }
                        else if (message.StartsWith("[USERLIST]"))
                        {
                            string[] users = message.Substring(10).Split('|');
                            groupUsers[groupId] = new List<string>(users);

                            if (currentGroup == groupId)
                            {
                                UserList.Items.Clear();
                                foreach (string user in users)
                                {
                                    UserList.Items.Add(user);
                                }
                            }
                        }
                        else
                        {
                            // Normal chat message
                            if (!groupMessages.ContainsKey(groupId))
                                groupMessages[groupId] = new List<string>();

                            groupMessages[groupId].Add(message);

                            if (currentGroup == groupId)
                                chat_box.Text = string.Join("\n", groupMessages[groupId]);

                            UpdateUserList(groupId, message);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendChat(groupId, "Connection lost: " + ex.Message));
            }
        }

        private void UpdateUserList(string groupId, string message)
        {
            // Ignore system messages like [HISTORY] and [USERLIST]
            if (message.StartsWith("[HISTORY]") || message.StartsWith("[USERLIST]"))
                return;

            string[] parts = message.Split(':');
            if (parts.Length > 0)
            {
                string sender = parts[0].Trim();
                if (!groupUsers[groupId].Contains(sender))
                    groupUsers[groupId].Add(sender);
            }

            if (currentGroup == groupId)
            {
                UserList.Items.Clear();
                foreach (var user in groupUsers[groupId])
                {
                    UserList.Items.Add(user);
                }
            }
        }

        private void GroupSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GroupSelector.SelectedItem == null) return;

            currentGroup = GroupSelector.SelectedItem.ToString();

            if (groupMessages.ContainsKey(currentGroup))
                chat_box.Text = string.Join("\n", groupMessages[currentGroup]);

            if (groupUsers.ContainsKey(currentGroup))
            {
                UserList.Items.Clear();
                foreach (var user in groupUsers[currentGroup])
                {
                    UserList.Items.Add(user);
                }
            }
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            stream?.Close();
            client?.Close();
            receiveThread?.Abort(); // Not recommended for production

            AppendChat(currentGroup, "Disconnected.");
        }

        private void AppendChat(string groupId, string message)
        {
            if (!groupMessages.ContainsKey(groupId))
                groupMessages[groupId] = new List<string>();

            groupMessages[groupId].Add(message);

            if (groupId == currentGroup)
                chat_box.Text = string.Join("\n", groupMessages[groupId]);
        }
        private void OpenEmojiMenu_Click(object sender, RoutedEventArgs e)
        {
            EmojiPopup.IsOpen = true;
        }
        private void Emoji_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button emojiButton)
            {
                message.Text += emojiButton.Content.ToString();
                message.Focus();
                message.CaretIndex = message.Text.Length;
            }
        }
    }

}