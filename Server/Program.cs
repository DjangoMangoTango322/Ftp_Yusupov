using System.Net;
using System.Net.Sockets;
using System.Text;
using Common;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Server.Models; 

namespace Server 
{
    public class Program 
    {
        public static IPAddress IpAdress;
        public static int Port;

        public static void Main(string[] args)
        {
            // Создаём БД и тестового пользователя
            using (var db = new AppDbContext())
            {
                db.Database.Migrate();

                if (!db.Users.Any(u => u.Login == "yusupov"))
                {
                    db.Users.Add(new User
                    {
                        Login = "yusupov",
                        Password = "Asdfg123",
                        RootDirectory = @"C:\Users\student-a502.PERMAVIAT\Desktop\asd\Ftp_Yusupov",
                        CurrentDirectory = @"C:\Users\student-a502.PERMAVIAT\Desktop\asd\Ftp_Yusupov"
                    });
                    db.SaveChanges();
                    Console.WriteLine("Создан пользователь: yusupov / Asdfg123");
                }
            }

            Console.Write("IP сервера (по умолчанию 127.0.0.1): ");
            string sIp = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(sIp)) sIp = "127.0.0.1";

            Console.Write("Порт (по умолчанию 8080): ");
            string sPort = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(sPort)) sPort = "8080";

            if (IPAddress.TryParse(sIp, out IpAdress) && int.TryParse(sPort, out Port))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Сервер запущен: {IpAdress}:{Port}");
                Console.ResetColor();
                StartServer();
            }
            else
            {
                Console.WriteLine("Ошибка ввода IP или порта!");
            }

            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey();
        }

        public static void StartServer()
        {
            var endPoint = new IPEndPoint(IpAdress, Port);
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                listener.Bind(endPoint);
                listener.Listen(10);
                Console.WriteLine("Ожидание подключений...");

                while (true)
                {
                    Socket client = listener.Accept();
                    var clientEndPoint = client.RemoteEndPoint as IPEndPoint;
                    _ = Task.Run(() => HandleClient(client, clientEndPoint));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка сервера: " + ex.Message);
            }
        }

        private static void HandleClient(Socket handler, IPEndPoint clientEndPoint)
        {
            string clientIp = clientEndPoint?.Address.ToString() ?? "unknown";

            try
            {
                byte[] buffer = new byte[10 * 1024 * 1024];
                int bytesRec = handler.Receive(buffer);

                if (bytesRec == 0)
                {
                    Console.WriteLine($"Клиент {clientIp} отключился без данных.");
                    return;
                }

                string receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRec);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Запрос от {clientIp}: {receivedData}");

                var request = JsonConvert.DeserializeObject<ViewModelSend>(receivedData);
                if (request == null)
                {
                    SendResponse(handler, new ViewModelMessage("message", "Неверный формат данных"));
                    return;
                }

                var response = ProcessCommand(request, clientIp);
                string jsonResponse = JsonConvert.SerializeObject(response);
                byte[] msg = Encoding.UTF8.GetBytes(jsonResponse);
                handler.Send(msg);

                Console.WriteLine($"Ответ отправлен клиенту {clientIp}: {response.Command}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки клиента {clientIp}: {ex.Message}");
                try { SendResponse(handler, new ViewModelMessage("message", "Ошибка сервера")); }
                catch { }
            }
            finally
            {
                try { handler.Shutdown(SocketShutdown.Both); handler.Close(); }
                catch { }
                Console.WriteLine($"Соединение с {clientIp} закрыто.\n");
            }
        }

        private static void SendResponse(Socket handler, ViewModelMessage response)
        {
            string json = JsonConvert.SerializeObject(response);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            handler.Send(bytes);
        }

        private static ViewModelMessage ProcessCommand(ViewModelSend request, string clientIp)
        {
            using var db = new AppDbContext();

            var log = new UserActionLog
            {
                UserId = request.Id == -1 ? (int?)null : request.Id,
                Command = "",
                Arguments = "",
                IpAddress = clientIp,
                Timestamp = DateTime.UtcNow,
                Success = false,
                Result = ""
            };

            // Блокировка неавторизованных команд
            if (request.Id == -1 && !request.Message.TrimStart().StartsWith("connect"))
            {
                log.Command = "unauthorized";
                log.Arguments = request.Message;
                log.Result = "Требуется авторизация";
                db.ActionLogs.Add(log);
                db.SaveChanges();
                return new ViewModelMessage("message", log.Result);
            }

            // === Загрузка файла (set) — Message = JSON FileInfoFTP ===
            if (TryParseFileUpload(request.Message, out FileInfoFTP fileInfo))
            {
                log.Command = "set";
                log.Arguments = fileInfo.Name;

                var user = db.Users.FirstOrDefault(u => u.Id == request.Id);
                if (user == null)
                {
                    log.Result = "Сессия недействительна";
                    db.ActionLogs.Add(log);
                    db.SaveChanges();
                    return new ViewModelMessage("message", log.Result);
                }

                log.UserId = user.Id;
                string rootDir = Path.GetFullPath(user.RootDirectory).TrimEnd('\\') + "\\";
                string currDir = string.IsNullOrEmpty(user.CurrentDirectory)
                    ? rootDir
                    : Path.GetFullPath(user.CurrentDirectory).TrimEnd('\\') + "\\";

                string savePath = Path.Combine(currDir, fileInfo.Name);

                if (!savePath.StartsWith(rootDir, StringComparison.OrdinalIgnoreCase))
                {
                    log.Result = "Доступ запрещён (выход за пределы корня)";
                    db.ActionLogs.Add(log);
                    db.SaveChanges();
                    return new ViewModelMessage("message", "Доступ запрещён");
                }

                try
                {
                    File.WriteAllBytes(savePath, fileInfo.Data);
                    log.Success = true;
                    log.Result = $"Файл загружен: {fileInfo.Name}";
                    db.ActionLogs.Add(log);
                    db.SaveChanges();
                    return new ViewModelMessage("message", log.Result);
                }
                catch (Exception ex)
                {
                    log.Result = $"Ошибка записи файла: {ex.Message}";
                    db.ActionLogs.Add(log);
                    db.SaveChanges();
                    return new ViewModelMessage("message", "Ошибка загрузки файла");
                }
            }

            // === Авторизация (connect) ===
            if (request.Message.TrimStart().StartsWith("connect", StringComparison.OrdinalIgnoreCase))
            {
                log.Command = "connect";
                var parts = request.Message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                {
                    log.Result = "Использование: connect <login> <password>";
                    db.ActionLogs.Add(log);
                    db.SaveChanges();
                    return new ViewModelMessage("message", log.Result);
                }

                string login = parts[1];
                string password = parts[2];
                log.Arguments = $"{login} ***";

                var user = db.Users.FirstOrDefault(u => u.Login == login && u.Password == password);
                if (user != null)
                {
                    log.UserId = user.Id;
                    log.Success = true;
                    log.Result = "Успешный вход";
                    db.ActionLogs.Add(log);
                    db.SaveChanges();
                    Console.WriteLine($"[УСПЕХ] Вход: {login} (ID: {user.Id}) от {clientIp}");
                    return new ViewModelMessage("autorization", user.Id.ToString());
                }

                log.Result = "Неверный логин или пароль";
                db.ActionLogs.Add(log);
                db.SaveChanges();
                return new ViewModelMessage("message", log.Result);
            }

            // === Обычные команды (cd, get) ===
            var currentUser = db.Users.FirstOrDefault(u => u.Id == request.Id);
            if (currentUser == null)
            {
                log.Command = "invalid_session";
                log.Result = "Сессия недействительна";
                db.ActionLogs.Add(log);
                db.SaveChanges();
                return new ViewModelMessage("message", "Сессия истекла");
            }

            log.UserId = currentUser.Id;

            string rootDirectory = Path.GetFullPath(currentUser.RootDirectory).TrimEnd('\\') + "\\";
            string currentDirectory = string.IsNullOrEmpty(currentUser.CurrentDirectory)
                ? rootDirectory
                : Path.GetFullPath(currentUser.CurrentDirectory).TrimEnd('\\') + "\\";

            string[] cmdParts = request.Message.Split(' ', 2);
            string command = cmdParts[0].ToLower();
            string argument = cmdParts.Length > 1 ? cmdParts[1].Trim('"') : "";

            log.Command = command;
            log.Arguments = argument;

            switch (command)
            {
                case "cd":
                    string newPath = currentDirectory;

                    if (string.IsNullOrEmpty(argument) || argument == ".")
                    {
                        newPath = rootDirectory;
                    }
                    else if (argument == "..")
                    {
                        var parent = Directory.GetParent(currentDirectory.TrimEnd('\\'));
                        newPath = parent != null && (parent.FullName + "\\").StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase)? parent.FullName + "\\": rootDirectory;
                    }
                    else
                    {
                        string target = Path.Combine(currentDirectory, argument);
                        string fullPath = Path.GetFullPath(target);
                        if (fullPath.StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullPath))
                        {
                            newPath = fullPath.EndsWith("\\") ? fullPath : fullPath + "\\";
                        }
                        else
                        {
                            log.Result = "Папка не найдена или доступ запрещён";
                            db.ActionLogs.Add(log);
                            db.SaveChanges();
                            return new ViewModelMessage("message", log.Result);
                        }
                    }

                    currentUser.CurrentDirectory = newPath;
                    db.SaveChanges();

                    log.Success = true;
                    log.Result = "OK";
                    db.ActionLogs.Add(log);
                    db.SaveChanges();

                    return new ViewModelMessage("cd", JsonConvert.SerializeObject(GetDirectoryList(newPath)));

                case "get":
                    if (string.IsNullOrEmpty(argument))
                    {
                        log.Result = "Укажите имя файла";
                        db.ActionLogs.Add(log);
                        db.SaveChanges();
                        return new ViewModelMessage("message", log.Result);
                    }

                    string filePath = Path.Combine(currentDirectory, argument);
                    if (!filePath.StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        log.Result = "Доступ запрещён";
                        db.ActionLogs.Add(log);
                        db.SaveChanges();
                        return new ViewModelMessage("message", log.Result);
                    }

                    if (File.Exists(filePath))
                    {
                        byte[] fileBytes = File.ReadAllBytes(filePath);
                        log.Success = true;
                        log.Result = "OK";
                        db.ActionLogs.Add(log);
                        db.SaveChanges();
                        return new ViewModelMessage("file", JsonConvert.SerializeObject(fileBytes));
                    }
                    else
                    {
                        log.Result = "Файл не найден";
                        db.ActionLogs.Add(log);
                        db.SaveChanges();
                        return new ViewModelMessage("message", log.Result);
                    }

                default:
                    log.Result = "Неизвестная команда";
                    db.ActionLogs.Add(log);
                    db.SaveChanges();
                    return new ViewModelMessage("message", log.Result);
            }
        }

        private static bool TryParseFileUpload(string message, out FileInfoFTP fileInfo)
        {
            fileInfo = null;
            try
            {
                fileInfo = JsonConvert.DeserializeObject<FileInfoFTP>(message);
                return fileInfo != null;
            }
            catch
            {
                return false;
            }
        }

        private static List<string> GetDirectoryList(string path)
        {
            var list = new List<string>();
            try
            {
                foreach (string dir in Directory.GetDirectories(path))
                    list.Add(Path.GetFileName(dir) + "/");

                foreach (string file in Directory.GetFiles(path))
                    list.Add(Path.GetFileName(file));
            }
            catch { }
            return list;
        }
    }
}