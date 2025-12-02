using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Common;
using MySqlConnector;
using Newtonsoft.Json;

namespace Server
{
    public class Program
    {
        private static IPAddress ipAddress;
        private static int port;
        private static string connectionString = "Server=localhost;Database=ftp_server;Uid=root;Pwd=;";

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "FTP Server";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.ForegroundColor = ConsoleColor.White;

            Console.WriteLine("Проверка подключения к MySQL...");
            try
            {
                using (MySqlConnection testConn = new MySqlConnection(connectionString))
                {
                    testConn.Open();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✓ Подключение к MySQL успешно!");

                    MySqlCommand versionCmd = new MySqlCommand("SELECT VERSION()", testConn);
                    string version = versionCmd.ExecuteScalar()?.ToString();
                    Console.WriteLine($"✓ Версия MySQL: {version}");

                    MySqlCommand tableCmd = new MySqlCommand("SHOW TABLES FROM ftp_server", testConn);
                    MySqlDataReader reader = tableCmd.ExecuteReader();
                    Console.WriteLine("✓ Таблицы в БД:");
                    while (reader.Read())
                    {
                        Console.WriteLine($"  - {reader.GetString(0)}");
                    }
                    reader.Close();
                    Console.ForegroundColor = ConsoleColor.White;
                }
            }
            catch (MySqlException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ ОШИБКА MySQL [{ex.Number}]: {ex.Message}");
                Console.WriteLine($"SQL State: {ex.SqlState}");
                Console.WriteLine("\nПроверьте:");
                Console.WriteLine("1. Запущен ли MySQL (XAMPP)");
                Console.WriteLine("2. Существует ли база данных 'ftp_server'");
                Console.WriteLine("3. Правильность строки подключения");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("\nНажмите любую клавишу для выхода...");
                Console.ReadKey();
                return;
            }

            Console.WriteLine();
            Console.Write("IP адрес (Enter = 127.0.0.1): ");
            string ip = Console.ReadLine();
            ipAddress = string.IsNullOrWhiteSpace(ip) ? IPAddress.Parse("127.0.0.1") : IPAddress.Parse(ip);

            Console.Write("Порт (Enter = 8888): ");
            string portStr = Console.ReadLine();
            port = string.IsNullOrWhiteSpace(portStr) ? 8888 : int.Parse(portStr);

            StartServer();
        }

        static void StartServer()
        {
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint endPoint = new IPEndPoint(ipAddress, port);

            serverSocket.Bind(endPoint);
            serverSocket.Listen(10);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n✓ Сервер запущен: {ipAddress}:{port}\n");
            Console.ForegroundColor = ConsoleColor.White;

            Dictionary<int, string> userFolders = new Dictionary<int, string>();

            while (true)
            {
                try
                {
                    Socket clientSocket = serverSocket.Accept();
                    string clientIP = ((IPEndPoint)clientSocket.RemoteEndPoint).Address.ToString();

                    byte[] buffer = new byte[1024 * 1024 * 10];
                    int received = clientSocket.Receive(buffer);
                    string data = Encoding.UTF8.GetString(buffer, 0, received);

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Клиент: {clientIP}");
                    Console.WriteLine($"Данные: {data}");
                    Console.ForegroundColor = ConsoleColor.White;

                    ViewModelSend request = JsonConvert.DeserializeObject<ViewModelSend>(data);

                    Stopwatch stopwatch = Stopwatch.StartNew();
                    ViewModelMessage response = ProcessRequest(request, clientIP, userFolders);
                    stopwatch.Stop();

                    if (request.Id != -1 && !request.Message.StartsWith("connect") && !request.Message.StartsWith("register"))
                    {
                        LogCommand(request.Id, request.Message, clientIP, stopwatch.ElapsedMilliseconds,
                                   response.TypeMessage == "message" ? "error" : "success", response.Message);
                    }

                    string jsonResponse = JsonConvert.SerializeObject(response);
                    byte[] responseBytes = Encoding.UTF8.GetBytes(jsonResponse);
                    clientSocket.Send(responseBytes);

                    clientSocket.Shutdown(SocketShutdown.Both);
                    clientSocket.Close();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Ошибка: {ex.Message}");
                    Console.ForegroundColor = ConsoleColor.White;
                }
            }
        }

        static ViewModelMessage ProcessRequest(ViewModelSend request, string clientIP, Dictionary<int, string> userFolders)
        {
            string[] parts = request.Message.Split(' ');
            string command = parts[0].ToLower();

            switch (command)
            {
                case "register":
                    return HandleRegister(parts, clientIP);

                case "connect":
                    return HandleConnect(parts, clientIP);

                case "cd":
                    return HandleCD(request, userFolders, clientIP);

                case "get":
                    return HandleGet(request, userFolders, clientIP);

                case "history":
                    return HandleHistory(request);

                default:
                    return HandleUpload(request, userFolders);
            }
        }

        static ViewModelMessage HandleRegister(string[] parts, string clientIP)
        {
            if (parts.Length < 4)
            {
                return new ViewModelMessage("message", "Формат: register логин пароль путь");
            }

            string login = parts[1];
            string password = parts[2];
            string path = string.Join(" ", parts.Skip(3));

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"→ Регистрация: {login}");
            Console.ForegroundColor = ConsoleColor.White;

            MySqlConnection conn = null;
            try
            {
                conn = new MySqlConnection(connectionString);
                conn.Open();

                MySqlCommand checkCmd = new MySqlCommand("SELECT COUNT(*) FROM users WHERE login=@login", conn);
                checkCmd.Parameters.AddWithValue("@login", login);
                int exists = Convert.ToInt32(checkCmd.ExecuteScalar());

                if (exists > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("  Пользователь уже существует");
                    Console.ForegroundColor = ConsoleColor.White;
                    return new ViewModelMessage("message", "Логин уже занят");
                }

                MySqlCommand insertCmd = new MySqlCommand(
                    "INSERT INTO users (login, password, src) VALUES (@login, @password, @src)", conn);
                insertCmd.Parameters.AddWithValue("@login", login);
                insertCmd.Parameters.AddWithValue("@password", password);
                insertCmd.Parameters.AddWithValue("@src", path);

                int result = insertCmd.ExecuteNonQuery();

                if (result > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  ✓ Пользователь {login} зарегистрирован!");
                    Console.ForegroundColor = ConsoleColor.White;
                    return new ViewModelMessage("message", "Регистрация успешна!");
                }
            }
            catch (MySqlException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗ MySQL ошибка: {ex.Message}");
                Console.ForegroundColor = ConsoleColor.White;
                return new ViewModelMessage("message", $"Ошибка БД: {ex.Message}");
            }
            finally
            {
                if (conn != null && conn.State == System.Data.ConnectionState.Open)
                    conn.Close();
            }

            return new ViewModelMessage("message", "Ошибка регистрации");
        }

        static ViewModelMessage HandleConnect(string[] parts, string clientIP)
        {
            if (parts.Length < 3)
            {
                return new ViewModelMessage("message", "Формат: connect логин пароль");
            }

            string login = parts[1];
            string password = parts[2];

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"→ Авторизация: {login}");
            Console.ForegroundColor = ConsoleColor.White;

            MySqlConnection conn = null;
            try
            {
                conn = new MySqlConnection(connectionString);
                conn.Open();

                MySqlCommand cmd = new MySqlCommand(
                    "SELECT id FROM users WHERE login=@login AND password=@password", conn);
                cmd.Parameters.AddWithValue("@login", login);
                cmd.Parameters.AddWithValue("@password", password);

                object result = cmd.ExecuteScalar();

                if (result != null)
                {
                    int userId = Convert.ToInt32(result);

                    try
                    {
                        MySqlCommand logCmd = new MySqlCommand(
                            "INSERT INTO user_sessions (user_id, ip_address) VALUES (@userId, @ip)", conn);
                        logCmd.Parameters.AddWithValue("@userId", userId);
                        logCmd.Parameters.AddWithValue("@ip", clientIP);
                        logCmd.ExecuteNonQuery();
                    }
                    catch { }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  ✓ Авторизован. ID: {userId}");
                    Console.ForegroundColor = ConsoleColor.White;

                    return new ViewModelMessage("authorization", userId.ToString());
                }
            }
            catch (MySqlException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗ MySQL ошибка: {ex.Message}");
                Console.ForegroundColor = ConsoleColor.White;
                return new ViewModelMessage("message", $"Ошибка БД: {ex.Message}");
            }
            finally
            {
                if (conn != null && conn.State == System.Data.ConnectionState.Open)
                    conn.Close();
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Неверные данные");
            Console.ForegroundColor = ConsoleColor.White;
            return new ViewModelMessage("message", "Неверный логин или пароль");
        }

        static ViewModelMessage HandleCD(ViewModelSend request, Dictionary<int, string> userFolders, string clientIP)
        {
            if (request.Id == -1)
            {
                return new ViewModelMessage("message", "Сначала авторизуйтесь");
            }

            try
            {
                string userPath = GetUserPath(request.Id);

                if (!userFolders.ContainsKey(request.Id))
                {
                    userFolders[request.Id] = userPath;
                }

                string[] parts = request.Message.Split(new[] { ' ' }, 2);
                string command = parts[0]; // "cd"

                if (parts.Length > 1)
                {
                    string targetFolder = parts[1].Trim();

                    if (targetFolder == "..")
                    {
                        string currentUserPath = userFolders[request.Id];
                        DirectoryInfo parentDir = Directory.GetParent(currentUserPath);

                        if (parentDir != null)
                        {
                            userFolders[request.Id] = parentDir.FullName;
                        }
                    }
                    else if (targetFolder == "~")
                    {
                        userFolders[request.Id] = userPath;
                    }
                    else
                    {
                        string newPath = Path.Combine(userFolders[request.Id], targetFolder);

                        if (Directory.Exists(newPath))
                        {
                            userFolders[request.Id] = newPath;
                        }
                        else
                        {
                            return new ViewModelMessage("message", $"Папка не найдена: {targetFolder}");
                        }
                    }
                }

                string finalPath = userFolders[request.Id];

                if (!Directory.Exists(finalPath))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  ⚠ Папка не существует: {finalPath}");
                    Console.WriteLine($"  → Создаю папку...");
                    Console.ForegroundColor = ConsoleColor.White;

                    try
                    {
                        Directory.CreateDirectory(finalPath);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"  ✓ Папка создана");
                        Console.ForegroundColor = ConsoleColor.White;
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  ✗ Не удалось создать папку: {ex.Message}");
                        Console.ForegroundColor = ConsoleColor.White;
                        return new ViewModelMessage("message", $"Папка не существует: {finalPath}");
                    }
                }

                List<string> items = new List<string>();

                DirectoryInfo parent = Directory.GetParent(finalPath);
                if (parent != null)
                {
                    items.Add("../");
                }

                foreach (string dir in Directory.GetDirectories(finalPath))
                {
                    items.Add(Path.GetFileName(dir) + "/");
                }

                foreach (string file in Directory.GetFiles(finalPath))
                {
                    items.Add(Path.GetFileName(file));
                }

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  → Просмотр папки: {finalPath} ({items.Count} элементов)");
                Console.ForegroundColor = ConsoleColor.White;

                var result = new
                {
                    items = items,
                    currentPath = finalPath
                };

                return new ViewModelMessage("cd", JsonConvert.SerializeObject(result));
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗ Ошибка: {ex.Message}");
                Console.ForegroundColor = ConsoleColor.White;
                return new ViewModelMessage("message", $"Ошибка: {ex.Message}");
            }
        }



        static ViewModelMessage HandleGet(ViewModelSend request, Dictionary<int, string> userFolders, string clientIP)
        {
            if (request.Id == -1)
            {
                return new ViewModelMessage("message", "Сначала авторизуйтесь");
            }

            try
            {
                string[] parts = request.Message.Split(new[] { ' ' }, 2);
                if (parts.Length < 2)
                {
                    return new ViewModelMessage("message", "Укажите имя файла");
                }

                string fileName = parts[1];
                string currentPath = userFolders[request.Id];
                string filePath = Path.Combine(currentPath, fileName);

                if (File.Exists(filePath))
                {
                    byte[] fileData = File.ReadAllBytes(filePath);

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"  → Скачивание файла: {fileName} ({fileData.Length} байт)");
                    Console.ForegroundColor = ConsoleColor.White;

                    return new ViewModelMessage("file", JsonConvert.SerializeObject(fileData));
                }

                return new ViewModelMessage("message", "Файл не найден");
            }
            catch (Exception ex)
            {
                return new ViewModelMessage("message", $"Ошибка: {ex.Message}");
            }
        }

        static ViewModelMessage HandleUpload(ViewModelSend request, Dictionary<int, string> userFolders)
        {
            if (request.Id == -1)
            {
                return new ViewModelMessage("message", "Сначала авторизуйтесь");
            }

            try
            {
                FileInfoFTP fileInfo = JsonConvert.DeserializeObject<FileInfoFTP>(request.Message);
                string currentPath = userFolders[request.Id];
                string filePath = Path.Combine(currentPath, fileInfo.Name);

                File.WriteAllBytes(filePath, fileInfo.Data);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  → Загружен файл: {fileInfo.Name} ({fileInfo.Data.Length} байт)");
                Console.ForegroundColor = ConsoleColor.White;

                return new ViewModelMessage("message", "Файл успешно загружен");
            }
            catch (Exception ex)
            {
                return new ViewModelMessage("message", $"Ошибка загрузки: {ex.Message}");
            }
        }

        static ViewModelMessage HandleHistory(ViewModelSend request)
        {
            if (request.Id == -1)
            {
                return new ViewModelMessage("message", "Сначала авторизуйтесь");
            }

            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();

                    string query = @"
                        SELECT command, ip_address, executed_at, execution_time_ms, status 
                        FROM user_commands 
                        WHERE user_id = @userId 
                        ORDER BY executed_at DESC 
                        LIMIT 20";

                    MySqlCommand cmd = new MySqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@userId", request.Id);

                    List<string> history = new List<string>();

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string command = reader.GetString(0);
                            string ip = reader.IsDBNull(1) ? "N/A" : reader.GetString(1);
                            DateTime time = reader.GetDateTime(2);
                            string execTime = reader.IsDBNull(3) ? "N/A" : reader.GetInt32(3) + "ms";
                            string status = reader.IsDBNull(4) ? "N/A" : reader.GetString(4);

                            history.Add($"[{time:dd.MM.yyyy HH:mm:ss}] {command} | IP: {ip} | {execTime} | {status}");
                        }
                    }

                    if (history.Count == 0)
                    {
                        return new ViewModelMessage("message", "История пуста");
                    }

                    return new ViewModelMessage("history", JsonConvert.SerializeObject(history));
                }
            }
            catch (Exception ex)
            {
                return new ViewModelMessage("message", $"Ошибка: {ex.Message}");
            }
        }

        static string GetUserPath(int userId)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();
                    MySqlCommand cmd = new MySqlCommand("SELECT src FROM users WHERE id=@id", conn);
                    cmd.Parameters.AddWithValue("@id", userId);
                    return cmd.ExecuteScalar()?.ToString() ?? "";
                }
            }
            catch
            {
                return "";
            }
        }

        static void LogCommand(int userId, string command, string ipAddress, long executionTimeMs, string status, string resultMessage)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();

                    string query = @"
                        INSERT INTO user_commands 
                        (user_id, command, ip_address, execution_time_ms, status, result_message) 
                        VALUES 
                        (@userId, @command, @ip, @execTime, @status, @result)";

                    MySqlCommand cmd = new MySqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@command", command);
                    cmd.Parameters.AddWithValue("@ip", ipAddress);
                    cmd.Parameters.AddWithValue("@execTime", executionTimeMs);
                    cmd.Parameters.AddWithValue("@status", status);
                    cmd.Parameters.AddWithValue("@result", resultMessage.Length > 200 ? resultMessage.Substring(0, 200) : resultMessage);

                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }
    }
}