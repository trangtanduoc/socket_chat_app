
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace Chat_v1
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private TcpClient client;
        private NetworkStream stream;
        private CancellationTokenSource cts;
        private readonly HttpClient httpClient;
        private string currentGroup = "";
        private string userName = "";

        private readonly Dictionary<string, List<string>> groupMessages = new();
        private readonly Dictionary<string, List<string>> groupUsers = new();
        private const int CHUNK_SIZE = 5 * 1024 * 1024;
        private bool isDisposed = false;

        public MainWindow()
        {
            InitializeComponent();
            httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        }

        private async void UploadFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() != true) return;

            try
            {
                string filePath = openFileDialog.FileName;
                string fileName = Path.GetFileName(filePath);

                AppendChat(currentGroup, $"Starting upload of {fileName}...");
                string cloudFrontUrl = await UploadFileWithMultipart(filePath, fileName);

                if (string.IsNullOrEmpty(cloudFrontUrl))
                {
                    AppendChat(currentGroup, "File upload failed");
                    return;
                }

                string message = $"{userName} (Group {currentGroup}) shared a file: {cloudFrontUrl}";
                await SendMessageAsync(message);
            }
            catch (Exception ex)
            {
                AppendChat(currentGroup, $"File upload failed: {ex.Message}");
            }
        }

        private async Task<string> UploadFileWithMultipart(string filePath, string fileName)
{
    try
    {
        // Step 1: Initiate multipart upload
        string initiateUrl = $"https://s3-image-production.up.railway.app/multipart/initiate?key={Uri.EscapeDataString(fileName)}";
        HttpResponseMessage initiateResponse = await httpClient.PostAsync(initiateUrl, null);
        initiateResponse.EnsureSuccessStatusCode();
        
        string initiateContent = await initiateResponse.Content.ReadAsStringAsync();
        var initiateData = JsonConvert.DeserializeObject<MultipartInitiateResponse>(initiateContent);
        
        if (string.IsNullOrEmpty(initiateData?.UploadId))
        {
            return null;
        }

        var fileInfo = new FileInfo(filePath);
        long fileSize = fileInfo.Length;
        int totalParts = (int)Math.Ceiling((double)fileSize / CHUNK_SIZE);
        
        var completedParts = new List<CompletedPart>();
        
        using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // Create tasks for parallel uploads
            var uploadTasks = new List<Task>();
            for (int partNumber = 1; partNumber <= totalParts; partNumber++)
            {
                int position = (partNumber - 1) * CHUNK_SIZE;
                int partSize = (int)Math.Min(CHUNK_SIZE, fileSize - position);
                
                // Get presigned URL
                string partUrl = $"https://s3-image-production.up.railway.app/multipart/presigned?filename={Uri.EscapeDataString(fileName)}&uploadId={initiateData.UploadId}&partNumber={partNumber}";
                HttpResponseMessage partUrlResponse = await httpClient.GetAsync(partUrl);
                partUrlResponse.EnsureSuccessStatusCode();
                
                string presignedPartUrl = (await partUrlResponse.Content.ReadAsStringAsync())
                    .Let(json => JsonConvert.DeserializeObject<Dictionary<string, string>>(json))["url"];
                
                // Read the chunk
                byte[] buffer = new byte[partSize];
                fileStream.Seek(position, SeekOrigin.Begin);
                await fileStream.ReadAsync(buffer, 0, partSize);
                
                // Add task for parallel upload
                int currentPartNumber = partNumber; // Capture partNumber in closure
                uploadTasks.Add(Task.Run(async () =>
                {
                    using (var content = new ByteArrayContent(buffer))
                    {
                        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                        content.Headers.ContentLength = partSize;
                        
                        HttpResponseMessage uploadResponse = await httpClient.PutAsync(presignedPartUrl, content);
                        uploadResponse.EnsureSuccessStatusCode();
                        
                        string eTag = uploadResponse.Headers.ETag?.Tag;
                        if (!string.IsNullOrEmpty(eTag))
                        {
                            lock (completedParts)
                            {
                                completedParts.Add(new CompletedPart { PartNumber = currentPartNumber, ETag = eTag });
                            }
                        }
                    }
                }));
            }

            // Wait for all uploads to complete
            await Task.WhenAll(uploadTasks);

            // Sort completedParts by PartNumber in ascending order
            completedParts.Sort((a, b) => a.PartNumber.CompareTo(b.PartNumber));
        }
        
        // Step 3: Complete multipart upload
        string completeUrl = $"https://s3-image-production.up.railway.app/multipart/complete";
        var completeRequest = new
        {
            Key = $"uploads/{fileName}",
            UploadId = initiateData.UploadId,
            Parts = completedParts
        };
        
        var completeContent = new StringContent(
            JsonConvert.SerializeObject(completeRequest),
            Encoding.UTF8,
            "application/json");
        
        HttpResponseMessage completeResponse = await httpClient.PostAsync(completeUrl, completeContent);
        completeResponse.EnsureSuccessStatusCode();
        
        string cloudFrontUrl = $"https://d3otg5l97buf8h.cloudfront.net/uploads/{fileName}";
        
        return cloudFrontUrl;
    }
    catch (Exception ex)
    {
        return null;
    }
}

        private async void Connect_Click(object sender, RoutedEventArgs e)
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

                client = new TcpClient();
                await client.ConnectAsync("192.168.2.3", 5000);
                stream = client.GetStream();

                // Send intro message with group and name
                string intro = $"{groupId}|{userName}";
                await SendMessageAsync(intro);

                cts = new CancellationTokenSource();
                _ = Task.Run(() => ReceiveMessages(groupId, cts.Token));

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

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            string msg = message.Text.Trim();
            if (string.IsNullOrEmpty(msg)) return;

            string fullMessage = $"{userName} (Group {currentGroup}): {msg}";
            await SendMessageAsync(fullMessage);
            message.Clear();
        }

        private async Task SendMessageAsync(string message)
        {
            if (stream == null || !stream.CanWrite) return;

            byte[] buffer = Encoding.UTF8.GetBytes(message);
            await stream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task ReceiveMessages(string groupId, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[2048];

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int byteCount = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (byteCount == 0) break;

                    string message = Encoding.UTF8.GetString(buffer, 0, byteCount);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (message.StartsWith("[HISTORY]"))
                        {
                            string history = message.Substring(9);
                            string[] lines = history.Split('\n');

                            if (!groupMessages.ContainsKey(groupId))
                                groupMessages[groupId] = new List<string>();

                            groupMessages[groupId].AddRange(lines);

                            if (currentGroup == groupId)
                                UpdateChatDisplay(groupId);
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
                            if (!groupMessages.ContainsKey(groupId))
                                groupMessages[groupId] = new List<string>();

                            groupMessages[groupId].Add(message);

                            if (currentGroup == groupId)
                                UpdateChatDisplay(groupId);

                            UpdateUserList(groupId, message);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                    AppendChat(groupId, "Connection lost: " + ex.Message));
            }
        }

        private void UpdateChatDisplay(string groupId)
        {
            chat_box.Children.Clear();

            foreach (string message in groupMessages[groupId])
            {
                if (message.Contains("shared a file:"))
                {
                    string[] parts = message.Split(new[] { "shared a file:" }, StringSplitOptions.None);
                    string fileUrl = parts[1].Trim();
                    string fileExtension = Path.GetExtension(fileUrl).ToLower();

                    StackPanel messagePanel = new StackPanel { Margin = new Thickness(5) };
                    TextBlock textBlock = new TextBlock { Text = parts[0].Trim(), TextWrapping = TextWrapping.Wrap };
                    messagePanel.Children.Add(textBlock);

                    if (IsImageExtension(fileExtension))
                    {
                        try
                        {
                            Image image = new Image
                            {
                                Source = new BitmapImage(new Uri(fileUrl)),
                                MaxWidth = 200,
                                MaxHeight = 200,
                                Margin = new Thickness(0, 5, 0, 0)
                            };
                            messagePanel.Children.Add(image);
                        }
                        catch
                        {
                            AddFileLink(messagePanel, fileUrl);
                        }
                    }
                    else
                    {
                        AddFileLink(messagePanel, fileUrl);
                    }

                    chat_box.Children.Add(messagePanel);
                }
                else
                {
                    TextBlock textBlock = new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(5)
                    };
                    chat_box.Children.Add(textBlock);
                }
            }
        }

        private bool IsImageExtension(string extension)
        {
            string[] imageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };
            return Array.Exists(imageExtensions, ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
        }

        private void AddFileLink(StackPanel panel, string fileUrl)
        {
            Button fileButton = new Button
            {
                Content = "📄 Download File",
                Margin = new Thickness(0, 5, 0, 0),
                Tag = fileUrl
            };
            fileButton.Click += FileButton_Click;
            panel.Children.Add(fileButton);
        }

        private void FileButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string fileUrl)
            {
                MessageBoxResult result = MessageBox.Show("Do you want to download this file?", "Download File",
                    MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = fileUrl,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to open file: {ex.Message}");
                    }
                }
            }
        }

        private void UpdateUserList(string groupId, string message)
        {
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
            UpdateChatDisplay(currentGroup);

            if (groupUsers.ContainsKey(currentGroup))
            {
                UserList.Items.Clear();
                foreach (var user in groupUsers[currentGroup])
                {
                    UserList.Items.Add(user);
                }
            }
        }

        private async void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            await DisconnectAsync();
        }

        private async Task DisconnectAsync()
        {
            try
            {
                cts?.Cancel();
                stream?.Close();
                client?.Close();
                AppendChat(currentGroup, "Disconnected.");
            }
            catch (Exception ex)
            {
                AppendChat(currentGroup, $"Disconnect failed: {ex.Message}");
            }
            finally
            {
                cts?.Dispose();
                stream = null;
                client = null;
                cts = null;
            }
        }

        private void AppendChat(string groupId, string message)
        {
            Dispatcher.Invoke(() =>
            {
                if (!groupMessages.ContainsKey(groupId))
                    groupMessages[groupId] = new List<string>();

                groupMessages[groupId].Add(message);

                if (groupId == currentGroup)
                    UpdateChatDisplay(groupId);
            });
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

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            cts?.Cancel();
            cts?.Dispose();
            stream?.Dispose();
            client?.Dispose();
            httpClient?.Dispose();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Dispose();
        }
    }

    public class MultipartInitiateResponse
    {
        public string Key { get; set; }
        public string UploadId { get; set; }
    }

    public class CompletedPart
    {
        public int PartNumber { get; set; }
        public string ETag { get; set; }
    }
}