using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Common;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClientWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string serverIP = "127.0.0.1";
        private int serverPort = 8888;
        private int userId = -1;
        private string currentPath = "";

        public MainWindow()
        {
            InitializeComponent();
            UpdateUI();
        }

        private void UpdateUI()
        {
            bool isConnected = userId != -1;
            PanelAuth.IsEnabled = !isConnected;
            BtnRefresh.IsEnabled = isConnected;
            BtnUpload.IsEnabled = isConnected;
            BtnHistory.IsEnabled = isConnected;

            if (isConnected)
            {
                TxtStatus.Text = $"Подключен | ID: {userId}";
            }
            else
            {
                TxtStatus.Text = "Не авторизован";
                ListViewFiles.Items.Clear();
                TxtCurrentPath.Text = "Путь: не подключен";
            }
        }

        private ViewModelMessage SendRequest(string message)
        {
            try
            {
                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(serverIP), serverPort);
                socket.Connect(endPoint);

                ViewModelSend request = new ViewModelSend
                {
                    Message = message,
                    Id = userId
                };

                string json = JsonConvert.SerializeObject(request);
                byte[] data = Encoding.UTF8.GetBytes(json);
                socket.Send(data);

                byte[] buffer = new byte[1024 * 1024 * 10];
                int received = socket.Receive(buffer);
                string response = Encoding.UTF8.GetString(buffer, 0, received);

                socket.Shutdown(SocketShutdown.Both);
                socket.Close();

                return JsonConvert.DeserializeObject<ViewModelMessage>(response);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка соединения: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            serverIP = TxtServer.Text;
            if (int.TryParse(TxtPort.Text, out int port))
            {
                serverPort = port;
                MessageBox.Show($"Настройки сохранены: {serverIP}:{serverPort}", "Информация");
            }
            else
            {
                MessageBox.Show("Неверный формат порта!", "Ошибка");
            }
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string login = TxtLogin.Text;
            string password = TxtPassword.Password;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Заполните все поля!", "Ошибка");
                return;
            }

            TxtStatus.Text = "Авторизация...";
            ViewModelMessage response = SendRequest($"connect {login} {password}");

            if (response != null)
            {
                if (response.TypeMessage == "authorization")
                {
                    userId = int.Parse(response.Message);
                    MessageBox.Show($"Вход выполнен!\nВаш ID: {userId}", "Успех");
                    UpdateUI();
                    LoadFiles();
                }
                else
                {
                    MessageBox.Show(response.Message, "Ошибка");
                    TxtStatus.Text = "Ошибка авторизации";
                }
            }
        }

        private void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            // Создаем диалоговое окно для регистрации
            var registerDialog = new Window
            {
                Title = "Регистрация нового пользователя",
                Width = 400,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Логин
            var lblLogin = new TextBlock { Text = "Логин:", Margin = new Thickness(0, 0, 0, 5) };
            Grid.SetRow(lblLogin, 0);
            var txtRegLogin = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(txtRegLogin, 1);

            // Пароль
            var lblPassword = new TextBlock { Text = "Пароль:", Margin = new Thickness(0, 0, 0, 5) };
            Grid.SetRow(lblPassword, 2);
            var txtRegPassword = new PasswordBox { Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(txtRegPassword, 3);

            // Подтверждение пароля
            var lblConfirm = new TextBlock { Text = "Подтверждение пароля:", Margin = new Thickness(0, 0, 0, 5) };
            Grid.SetRow(lblConfirm, 4);
            var txtRegConfirm = new PasswordBox { Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(txtRegConfirm, 5);

            // Кнопки
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(buttonPanel, 6);

            var btnCancel = new Button
            {
                Content = "Отмена",
                Width = 80,
                Margin = new Thickness(0, 0, 10, 0)
            };
            btnCancel.Click += (s, args) => registerDialog.DialogResult = false;

            var btnRegister = new Button
            {
                Content = "Зарегистрироваться",
                Width = 120,
                Background = System.Windows.Media.Brushes.Green,
                Foreground = System.Windows.Media.Brushes.White
            };
            btnRegister.Click += async (s, args) =>
            {
                string login = txtRegLogin.Text;
                string password = txtRegPassword.Password;
                string confirm = txtRegConfirm.Password;

                if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
                {
                    MessageBox.Show("Заполните все поля!", "Ошибка");
                    return;
                }

                if (password != confirm)
                {
                    MessageBox.Show("Пароли не совпадают!", "Ошибка");
                    return;
                }

                if (password.Length < 6)
                {
                    MessageBox.Show("Пароль должен быть не менее 6 символов!", "Ошибка");
                    return;
                }

                try
                {
                    TxtStatus.Text = "Регистрация...";

                    // Отправляем запрос на регистрацию
                    ViewModelMessage response = SendRequest($"register {login} {password}");

                    if (response != null)
                    {
                        if (response.TypeMessage == "authorization")
                        {
                            MessageBox.Show("Регистрация успешна!\nТеперь вы можете войти в систему.", "Успех");
                            registerDialog.DialogResult = true;

                            // Автоматически заполняем поля для входа
                            TxtLogin.Text = login;
                            TxtPassword.Password = password;
                        }
                        else
                        {
                            MessageBox.Show(response.Message, "Ошибка регистрации");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка");
                }
            };

            buttonPanel.Children.Add(btnCancel);
            buttonPanel.Children.Add(btnRegister);

            grid.Children.Add(lblLogin);
            grid.Children.Add(txtRegLogin);
            grid.Children.Add(lblPassword);
            grid.Children.Add(txtRegPassword);
            grid.Children.Add(lblConfirm);
            grid.Children.Add(txtRegConfirm);
            grid.Children.Add(buttonPanel);

            registerDialog.Content = grid;

            // Устанавливаем кнопку Enter для регистрации
            registerDialog.KeyDown += (s, args) =>
            {
                if (args.Key == System.Windows.Input.Key.Enter)
                {
                    btnRegister.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            };

            var result = registerDialog.ShowDialog();

            if (result == true)
            {
                TxtStatus.Text = "Регистрация завершена";
            }
            else
            {
                TxtStatus.Text = "Регистрация отменена";
            }
        }

        private void LoadFiles()
        {
            if (userId == -1) return;

            TxtStatus.Text = "Загрузка файлов...";
            ViewModelMessage response = SendRequest("cd");

            if (response != null && response.TypeMessage == "cd")
            {
                try
                {
                    // Парсим JSON с путём и списком
                    JObject jsonObj = JObject.Parse(response.Message);
                    currentPath = jsonObj["currentPath"]?.ToString() ?? "Неизвестно";
                    List<string> items = jsonObj["items"]?.ToObject<List<string>>() ?? new List<string>();

                    TxtCurrentPath.Text = $"Путь: {currentPath}";
                    ListViewFiles.Items.Clear();

                    foreach (string item in items)
                    {
                        bool isFolder = item.EndsWith("/");
                        string displayName = item;
                        string icon = "📁";

                        if (item == "../")
                        {
                            icon = "⬆️";
                            displayName = ".. (Назад)";
                        }
                        else if (!isFolder)
                        {
                            icon = "📄";
                        }

                        ListViewFiles.Items.Add(new FileItem
                        {
                            Name = item,
                            DisplayName = displayName,
                            Type = icon + " " + (isFolder ? "Папка" : "Файл"),
                            DownloadVisible = isFolder ? Visibility.Collapsed : Visibility.Visible
                        });
                    }

                    TxtStatus.Text = $"Загружено элементов: {items.Count}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка парсинга: {ex.Message}", "Ошибка");
                }
            }
            else
            {
                TxtStatus.Text = response?.Message ?? "Ошибка загрузки";
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadFiles();
        }

        private void ListViewFiles_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ListViewFiles.SelectedItem == null) return;

            FileItem item = (FileItem)ListViewFiles.SelectedItem;

            if (item.Name.EndsWith("/"))
            {
                // Переход в папку
                string folderName = item.Name.TrimEnd('/');

                if (item.Name == "../")
                {
                    // Назад
                    NavigateToFolder("..");
                }
                else
                {
                    // Вперёд в папку
                    NavigateToFolder(folderName);
                }
            }
            else
            {
                // Скачать файл
                DownloadFile(item.Name);
            }
        }

        private void NavigateToFolder(string folderName)
        {
            TxtStatus.Text = $"Переход: {folderName}...";
            ViewModelMessage response = SendRequest($"cd {folderName}");

            if (response != null && response.TypeMessage == "cd")
            {
                try
                {
                    JObject jsonObj = JObject.Parse(response.Message);
                    currentPath = jsonObj["currentPath"]?.ToString() ?? "Неизвестно";
                    List<string> items = jsonObj["items"]?.ToObject<List<string>>() ?? new List<string>();

                    TxtCurrentPath.Text = $"Путь: {currentPath}";
                    ListViewFiles.Items.Clear();

                    foreach (string item in items)
                    {
                        bool isFolder = item.EndsWith("/");
                        string displayName = item;
                        string icon = "📁";

                        if (item == "../")
                        {
                            icon = "⬆️";
                            displayName = ".. (Назад)";
                        }
                        else if (!isFolder)
                        {
                            icon = "📄";
                        }

                        ListViewFiles.Items.Add(new FileItem
                        {
                            Name = item,
                            DisplayName = displayName,
                            Type = icon + " " + (isFolder ? "Папка" : "Файл"),
                            DownloadVisible = isFolder ? Visibility.Collapsed : Visibility.Visible
                        });
                    }

                    TxtStatus.Text = $"Открыто: {currentPath}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка");
                }
            }
            else
            {
                MessageBox.Show(response?.Message ?? "Ошибка перехода", "Ошибка");
            }
        }

        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            string fileName = btn.Tag.ToString();
            DownloadFile(fileName);
        }

        private void DownloadFile(string fileName)
        {
            TxtStatus.Text = $"Скачивание: {fileName}...";
            ViewModelMessage response = SendRequest($"get {fileName}");

            if (response != null && response.TypeMessage == "file")
            {
                byte[] fileData = JsonConvert.DeserializeObject<byte[]>(response.Message);

                SaveFileDialog saveDialog = new SaveFileDialog
                {
                    FileName = fileName,
                    Filter = "Все файлы (*.*)|*.*"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    File.WriteAllBytes(saveDialog.FileName, fileData);
                    MessageBox.Show($"Файл сохранен:\n{saveDialog.FileName}", "Успех");
                    TxtStatus.Text = $"Файл скачан: {fileName}";
                }
            }
            else
            {
                MessageBox.Show(response?.Message ?? "Ошибка скачивания", "Ошибка");
                TxtStatus.Text = "Ошибка скачивания";
            }
        }

        private void BtnUpload_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog
            {
                Filter = "Все файлы (*.*)|*.*",
                Multiselect = false
            };

            if (openDialog.ShowDialog() == true)
            {
                string fileName = Path.GetFileName(openDialog.FileName);
                byte[] fileData = File.ReadAllBytes(openDialog.FileName);

                TxtStatus.Text = $"Загрузка: {fileName}...";

                FileInfoFTP fileInfo = new FileInfoFTP
                {
                    Name = fileName,
                    Data = fileData
                };

                string message = JsonConvert.SerializeObject(fileInfo);
                ViewModelMessage response = SendRequest(message);

                if (response != null)
                {
                    MessageBox.Show(response.Message, "Результат");
                    TxtStatus.Text = $"Файл загружен: {fileName}";
                    LoadFiles();
                }
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            string fileName = btn.Tag.ToString();

            var result = MessageBox.Show($"Удалить файл:\n{fileName}?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                MessageBox.Show("Функция удаления в разработке", "Информация");
            }
        }

        private void BtnHistory_Click(object sender, RoutedEventArgs e)
        {
            TxtStatus.Text = "Загрузка истории...";
            ViewModelMessage response = SendRequest("history");

            if (response != null && response.TypeMessage == "history")
            {
                List<string> history = JsonConvert.DeserializeObject<List<string>>(response.Message);

                // Создаем окно для отображения истории
                var historyWindow = new Window
                {
                    Title = "История операций",
                    Width = 600,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this
                };

                var listBox = new ListBox
                {
                    ItemsSource = history,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    Padding = new Thickness(10)
                };

                var scrollViewer = new ScrollViewer
                {
                    Content = listBox,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                historyWindow.Content = scrollViewer;
                historyWindow.ShowDialog();

                TxtStatus.Text = "История загружена";
            }
            else
            {
                MessageBox.Show(response?.Message ?? "Ошибка", "Ошибка");
            }
        }
    }

    public class FileItem
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Type { get; set; }
        public Visibility DownloadVisible { get; set; }
    }
}