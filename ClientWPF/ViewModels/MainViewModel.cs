using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using Microsoft.Win32;
using Newtonsoft.Json;
using Common;
using System.IO;

namespace ClientWPF.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // === Поля и свойства ===
        private string _serverIp = "127.0.0.1";
        private string _port = "8080";
        private string _status = "Не подключено";
        private FileItem _selectedItem;

        public string ServerIp
        {
            get => _serverIp;
            set { _serverIp = value; OnPropertyChanged(); }
        }

        public string Port
        {
            get => _port;
            set { _port = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public ObservableCollection<FileItem> Files { get; } = new();

        public FileItem SelectedItem
        {
            get => _selectedItem;
            set { _selectedItem = value; OnPropertyChanged(); }
        }

        // === Подключение ===
        private TcpClient _client;
        private NetworkStream _stream;
        private int _userId = -1;

        // === Команды ===
        public ICommand ConnectCommand { get; }
        public ICommand OpenItemCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand UploadCommand { get; }
        public ICommand RefreshCommand { get; }

        public MainViewModel()
        {
            ConnectCommand = new RelayCommand(async () => await ConnectAsync());
            OpenItemCommand = new RelayCommand(async () => await OpenSelectedItem(), () => SelectedItem != null);
            DownloadCommand = new RelayCommand(async () => await DownloadSelectedFile(), () => SelectedItem != null && !SelectedItem.IsDirectory);
            UploadCommand = new RelayCommand(async () => await UploadFile());
            RefreshCommand = new RelayCommand(async () => await RefreshDirectory());

            // Drag & Drop
            Application.Current.MainWindow.Drop += MainWindow_Drop;
            Application.Current.MainWindow.AllowDrop = true;
        }

        private async Task ConnectAsync()
        {
            try
            {
                Status = "Подключение...";
                _client = new TcpClient();
                await _client.ConnectAsync(ServerIp, int.Parse(Port));
                _stream = _client.GetStream();

                var response = await SendCommand("connect yusupov Asdfg123");
                if (response.Command == "autorization")
                {
                    _userId = int.Parse(response.Data);
                    Status = $"Подключено! ID: {_userId}";
                    await RefreshDirectory();
                }
                else
                {
                    Status = "Ошибка авторизации: " + response.Data;
                }
            }
            catch (Exception ex)
            {
                Status = "Ошибка: " + ex.Message;
            }
        }

        private async Task<ViewModelMessage> SendCommand(string command)
        {
            var request = new ViewModelSend(command, _userId);
            string json = JsonConvert.SerializeObject(request);
            byte[] data = Encoding.UTF8.GetBytes(json);
            await _stream.WriteAsync(data, 0, data.Length);

            byte[] buffer = new byte[10 * 1024 * 1024];
            int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
            string responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            return JsonConvert.DeserializeObject<ViewModelMessage>(responseJson);
        }

        private async Task RefreshDirectory()
        {
            Files.Clear();
            Files.Add(new FileItem { Name = "..", IsDirectory = true }); // вверх

            var response = await SendCommand("cd");
            if (response.Command == "cd")
            {
                var list = JsonConvert.DeserializeObject<List<string>>(response.Data);
                foreach (var item in list)
                {
                    bool isDir = item.EndsWith("/");
                    string name = isDir ? item.TrimEnd('/') : item;
                    Files.Add(new FileItem { Name = name, IsDirectory = isDir });
                }
            }
        }

        private async Task OpenSelectedItem()
        {
            if (SelectedItem == null) return;

            if (SelectedItem.Name == "..")
                await SendCommand("cd ..");
            else if (SelectedItem.IsDirectory)
                await SendCommand($"cd \"{SelectedItem.Name}\"");
            else
                await DownloadSelectedFile();

            await RefreshDirectory();
        }

        private async Task DownloadSelectedFile()
        {
            if (SelectedItem == null || SelectedItem.IsDirectory) return;

            var dialog = new SaveFileDialog
            {
                FileName = SelectedItem.Name,
                Title = "Сохранить файл"
            };

            if (dialog.ShowDialog() == true)
            {
                Status = $"Скачивание {SelectedItem.Name}...";
                var response = await SendCommand($"get \"{SelectedItem.Name}\"");
                if (response.Command == "file")
                {
                    byte[] fileBytes = JsonConvert.DeserializeObject<byte[]>(response.Data);
                    File.WriteAllBytes(dialog.FileName, fileBytes);
                    Status = "Файл скачан!";
                }
            }
        }

        private async Task UploadFile()
        {
            var dialog = new OpenFileDialog();
            if (dialog.ShowDialog() == true)
            {
                Status = $"Загрузка {Path.GetFileName(dialog.FileName)}...";
                byte[] fileBytes = File.ReadAllBytes(dialog.FileName);
                var fileInfo = new FileInfoFTP(fileBytes, Path.GetFileName(dialog.FileName));
                var request = new ViewModelSend(JsonConvert.SerializeObject(fileInfo), _userId);

                string json = JsonConvert.SerializeObject(request);
                byte[] data = Encoding.UTF8.GetBytes(json);
                await _stream.WriteAsync(data, 0, data.Length);

                var response = await ReadResponse();
                Status = response.Data;
            }
        }

        private async void MainWindow_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    byte[] fileBytes = File.ReadAllBytes(files[0]);
                    var fileInfo = new FileInfoFTP(fileBytes, Path.GetFileName(files[0]));
                    var request = new ViewModelSend(JsonConvert.SerializeObject(fileInfo), _userId);

                    string json = JsonConvert.SerializeObject(request);
                    byte[] data = Encoding.UTF8.GetBytes(json);
                    await _stream.WriteAsync(data, 0, data.Length);

                    var response = await ReadResponse();
                    Status = response.Data;
                    await RefreshDirectory();
                }
            }
        }

        private async Task<ViewModelMessage> ReadResponse()
        {
            byte[] buffer = new byte[10 * 1024 * 1024];
            int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
            string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            return JsonConvert.DeserializeObject<ViewModelMessage>(json);
        }

        
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string prop = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }

    
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();
        public void Execute(object parameter) => _execute();
    }

    
    public class FileItem
    {
        public string Name { get; set; }
        public bool IsDirectory { get; set; }
        public string DisplayName => IsDirectory ? $"[DIR] {Name}" : Name;
    }
}