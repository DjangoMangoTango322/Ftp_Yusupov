using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Server;
using Server.Models;

namespace Common
{
    public class Programm
    {
        
       public static IPAddress IpAdress;
       public static int Port;

       public static void Main(string[] args)
       {
           // Создаём БД и тестового пользователя (если его нет)
           using (var db = new AppDbContext())
           {
               db.Database.Migrate(); // создаёт базу ftp_server и таблицу Users

               if (!db.Users.Any(u => u.Login == "yusupov"))
               {
                   db.Users.Add(new User
                   {
                       Login = "yusupov",
                       Password = "Asdfg123", // пароль хранится открыто
                       RootDirectory = @"C:\Users\student-a502.PERMAVIAT\Desktop\asdd\Ftp_Yusupov",
                       CurrentDirectory = @"C:\Users\student-a502.PERMAVIAT\Desktop\asdd\Ftp_Yusupov\"
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
                   _ = Task.Run(() => HandleClient(client));
               }
           }
           catch (Exception ex)
           {
               Console.WriteLine("Ошибка сервера: " + ex.Message);
           }
       }

       private static void HandleClient(Socket handler)
       {
           try
           {
               byte[] buffer = new byte[10485760];
               int bytesRec = handler.Receive(buffer);
               string data = Encoding.UTF8.GetString(buffer, 0, bytesRec);

               Console.WriteLine($"Запрос: {data}");

               var request = JsonConvert.DeserializeObject<ViewModelSend>(data)!;
               var response = ProcessCommand(request);

               string json = JsonConvert.SerializeObject(response);
               byte[] msg = Encoding.UTF8.GetBytes(json);
               handler.Send(msg);
           }
           catch (Exception ex)
           {
               Console.WriteLine("Ошибка клиента: " + ex.Message);
           }
           finally
           {
               handler.Shutdown(SocketShutdown.Both);
               handler.Close();
           }
       }

       private static ViewModelMessage ProcessCommand(ViewModelSend request)
       {
           string[] parts = request.Message.Split(' ', 2);
           string cmd = parts[0].ToLower();
           string args = parts.Length > 1 ? parts[1] : "";

           using var db = new AppDbContext();

           // Только connect разрешён без авторизации
           if (request.Id == -1 && cmd != "connect")
               return new ViewModelMessage("message", "Сначала авторизуйтесь: connect login password");

           if (cmd == "connect")
           {
               string[] creds = args.Split(' ', 2);
               if (creds.Length != 2)
                   return new ViewModelMessage("message", "Использование: connect login password");

               string login = creds[0];
               string password = creds[1];

               var user = db.Users.FirstOrDefault(u => u.Login == login && u.Password == password);
               if (user != null)
               {
                   Console.WriteLine($"Вход успешен: {login} (ID: {user.Id})");
                   return new ViewModelMessage("autorization", user.Id.ToString());
               }
               return new ViewModelMessage("message", "Неверный логин или пароль");
           }

           // Проверяем, что пользователь существует
           var userSession = db.Users.FirstOrDefault(u => u.Id == request.Id);
           if (userSession == null)
               return new ViewModelMessage("message", "Сессия недействительна");

           string root = Path.GetFullPath(userSession.RootDirectory).TrimEnd('\\') + "\\";
           string current = string.IsNullOrEmpty(userSession.CurrentDirectory)
               ? root
               : Path.GetFullPath(userSession.CurrentDirectory).TrimEnd('\\') + "\\";

           switch (cmd)
           {
                case "cd":
                    string newPath = current;

                    if (string.IsNullOrEmpty(args) || args == ".")
                    {
                        newPath = root;
                    }
                    else if (args == "..")
                    {
                        var parent = Directory.GetParent(current);
                        if (parent != null)
                        {
                            string parentPath = parent.FullName + "\\";
                            if (parentPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                                newPath = parentPath;
                            else
                                newPath = root;
                        }
                        else
                        {
                            newPath = root;
                        }
                    }
                    else
                    {
                        string target = Path.Combine(current, args);
                        string full = Path.GetFullPath(target);

                        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full))
                        {
                            newPath = full.EndsWith("\\") ? full : full + "\\";
                        }
                        else
                        {
                            return new ViewModelMessage("message", "Папка не существует или доступ запрещён");
                        }
                    }

                    userSession.CurrentDirectory = newPath;
                    db.SaveChanges();

                    return new ViewModelMessage("cd", JsonConvert.SerializeObject(GetDirectoryList(newPath)));
               case "get":
                   if (string.IsNullOrEmpty(args))
                       return new ViewModelMessage("message", "Укажите имя файла");

                   string filePath = Path.Combine(current, args);
                   if (!filePath.StartsWith(root))
                       return new ViewModelMessage("message", "Доступ запрещён");

                   if (File.Exists(filePath))
                   {
                       byte[] bytes = File.ReadAllBytes(filePath);
                       return new ViewModelMessage("file", JsonConvert.SerializeObject(bytes));
                   }
                   return new ViewModelMessage("message", "Файл не найден");

               case "set":
                   try
                   {
                       var fileInfo = JsonConvert.DeserializeObject<FileInfoFTP>(request.Message)!;
                       string savePath = Path.Combine(current, fileInfo.Name);

                       if (!savePath.StartsWith(root))
                           return new ViewModelMessage("message", "Доступ запрещён");

                       File.WriteAllBytes(savePath, fileInfo.Data);
                       return new ViewModelMessage("message", $"Файл загружен: {fileInfo.Name}");
                   }
                   catch
                   {
                       return new ViewModelMessage("message", "Ошибка загрузки файла");
                   }

               default:
                   return new ViewModelMessage("message", "Неизвестная команда");
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
