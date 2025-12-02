using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Common;
using Newtonsoft.Json;

namespace Client
{

    public class Program
    {
        private static string serverIP = "127.0.0.1";
        private static int serverPort = 8888;
        private static int userId = -1;

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "FTP Client";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.ForegroundColor = ConsoleColor.White;

            Console.Write("IP сервера (Enter = 127.0.0.1): ");
            string ip = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(ip)) serverIP = ip;

            Console.Write("Порт (Enter = 8888): ");
            string portStr = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(portStr)) serverPort = int.Parse(portStr);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n✓ Сервер: {serverIP}:{serverPort}\n");
            Console.ForegroundColor = ConsoleColor.White;

            while (true)
            {
                ShowMenu();
                string choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        Register();
                        break;
                    case "2":
                        Login();
                        break;
                    case "3":
                        ShowFiles();
                        break;
                    case "4":
                        ShowHistory();
                        break;
                    case "0":
                        Console.WriteLine("\nДо свидания!");
                        return;
                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("\nНеверный выбор!");
                        Console.ForegroundColor = ConsoleColor.White;
                        break;
                }
            }
        }

        static void ShowMenu()
        {
            if (userId == -1)
            {
                Console.WriteLine("1 - Регистрация");
                Console.WriteLine("2 - Войти");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Вы вошли, ID: {userId}]");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("3 - Показать файлы");
                Console.WriteLine("4 - История команд");
            }
            Console.WriteLine("0 - Выход");
            Console.Write("Выбор: ");
        }

        static ViewModelMessage SendRequest(string message)
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
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nОшибка соединения: {ex.Message}");
                Console.ForegroundColor = ConsoleColor.White;
                return null;
            }
        }

        static void Register()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n--- РЕГИСТРАЦИЯ ---");
            Console.ForegroundColor = ConsoleColor.White;

            Console.Write("Логин: ");
            string login = Console.ReadLine();

            Console.Write("Пароль: ");
            string password = Console.ReadLine();

            Console.Write("Путь к папке (например C:\\MyFolder): ");
            string path = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(path))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\nВсе поля обязательны!");
                Console.ForegroundColor = ConsoleColor.White;
                return;
            }

            string message = $"register {login} {password} {path}";
            ViewModelMessage response = SendRequest(message);

            if (response != null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n{response.Message}");
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        static void Login()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n--- ВХОД ---");
            Console.ForegroundColor = ConsoleColor.White;

            Console.Write("Логин: ");
            string login = Console.ReadLine();

            Console.Write("Пароль: ");
            string password = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\nЛогин и пароль обязательны!");
                Console.ForegroundColor = ConsoleColor.White;
                return;
            }

            string message = $"connect {login} {password}";
            ViewModelMessage response = SendRequest(message);

            if (response != null)
            {
                if (response.TypeMessage == "authorization")
                {
                    userId = int.Parse(response.Message);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\n✓ Вход выполнен! ID: {userId}");
                    Console.ForegroundColor = ConsoleColor.White;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n✗ {response.Message}");
                    Console.ForegroundColor = ConsoleColor.White;
                }
            }
        }

        static void ShowFiles()
        {
            if (userId == -1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\nСначала войдите в систему!");
                Console.ForegroundColor = ConsoleColor.White;
                return;
            }

            ViewModelMessage response = SendRequest("cd");

            if (response != null)
            {
                if (response.TypeMessage == "cd")
                {
                    List<string> files = JsonConvert.DeserializeObject<List<string>>(response.Message);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("\n--- СОДЕРЖИМОЕ ПАПКИ ---");
                    if (files.Count == 0)
                    {
                        Console.WriteLine("  (пусто)");
                    }
                    else
                    {
                        foreach (string file in files)
                        {
                            Console.WriteLine($"  {file}");
                        }
                    }
                    Console.ForegroundColor = ConsoleColor.White;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n{response.Message}");
                    Console.ForegroundColor = ConsoleColor.White;
                }
            }
        }

        static void ShowHistory()
        {
            if (userId == -1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\nСначала войдите в систему!");
                Console.ForegroundColor = ConsoleColor.White;
                return;
            }

            Console.WriteLine("\nЗапрос истории команд...");
            ViewModelMessage response = SendRequest("history");

            if (response != null)
            {
                if (response.TypeMessage == "history")
                {
                    List<string> history = JsonConvert.DeserializeObject<List<string>>(response.Message);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.ForegroundColor = ConsoleColor.White;

                    if (history.Count == 0)
                    {
                        Console.WriteLine("\n  История команд пуста");
                    }
                    else
                    {
                        int number = 1;
                        foreach (string record in history)
                        {
                            Console.WriteLine($"{number}. {record}");
                            number++;
                        }
                    }
                    Console.ForegroundColor = ConsoleColor.White;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n{response.Message}");
                    Console.ForegroundColor = ConsoleColor.White;
                }
            }
        }
    }
}
