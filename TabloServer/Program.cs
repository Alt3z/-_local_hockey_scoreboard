using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class Server
{
    // Порт для TCp
    private const int Port = 5000;
    // Порт для прослушивания широковещательных запросов
    private const int BroadcastPort = 5001;
    private static readonly IPAddress IpAddress = IPAddress.Parse("ip");
    private static TcpListener listener;
    // Список клиентов
    private static List<TcpClient> clients = new List<TcpClient>();
    // Номер текущего периода
    private static int period = 1;
    // Массив для хранения счетов команд
    private static int[] scores = new int[2];
    // Время начала текущего периода
    private static DateTime periodStartTime;
    // Продолжительность периода в секундах
    private static int periodDurationSeconds = 600;
    // Оставшееся время текущего периода в секундах
    private static int remainingTimeSeconds = 600;
    private static string team1 = "Команда 1";
    private static string team2 = "Команда 2";
    private static bool gameRunning = false;
    private static bool gamePaused = false;
    // Источник токенов отмены для управления игровым циклом
    private static CancellationTokenSource cts = new CancellationTokenSource();
    private static List<string> goalEvents = new List<string>();

    public static void Main(string[] args)
    {
        // Инициализируем и запускаем слушатель TCP соединений
        listener = new TcpListener(IpAddress, Port);
        listener.Start();
        Console.WriteLine("Сервер запущен...");
        // Выводим доступные команды на экран
        DisplayCommands();

        // Принятия клиентов, выполнения команд и прослушивание широковещательных запросов
        Task.Run(() => AcceptClientsAsync());
        Task.Run(() => CommandLoop());
        Task.Run(() => StartBroadcastListener());

        Thread.Sleep(Timeout.Infinite);
    }

    private static void DisplayCommands()
    {
        Console.WriteLine("Доступные команды:");
        Console.WriteLine("установить_команду1 <название> - Установить название первой команды");
        Console.WriteLine("установить_команду2 <название> - Установить название второй команды");
        Console.WriteLine("установить_период <номер> - Установить номер текущего периода");
        Console.WriteLine("установить_время <минуты> <секунды> - Установить продолжительность периода");
        Console.WriteLine("начать - Начать текущий период");
        Console.WriteLine("остановить - Остановить текущий период");
        Console.WriteLine("новая_игра - Начать новую игру");
        Console.WriteLine("добавить_гол <номер команды> <номер игрока> <фамилия> <минута> <секунда> - Добавить гол");
    }

    private static async Task AcceptClientsAsync()
    {
        while (true)
        {
            // Принимаем нового клиента
            var client = await listener.AcceptTcpClientAsync();
            // Добавляем клиента в список
            lock (clients)
            {
                clients.Add(client);
            }
            Console.WriteLine("Клиент подключен...");

            // Отправляем клиенту текущее состояние игры
            var gameState = GetGameState();
            var data = Encoding.UTF8.GetBytes(gameState);
            var stream = client.GetStream();
            await stream.WriteAsync(data, 0, data.Length);
        }
    }

    private static async Task StartBroadcastListener()
    {
        // Создаем UDP клиент для прослушивания широковещательных запросов
        using UdpClient udpClient = new UdpClient(BroadcastPort);

        try
        {
            while (true) // Бесконечный цикл для постоянного прослушивания запросов
            {
                //Console.WriteLine("Ожидание широковещательных запросов...");
                // Асинхронно принимаем UDP сообщение
                var receivedResult = await udpClient.ReceiveAsync();
                // Получаем массив байтов из принятого сообщения
                byte[] bytes = receivedResult.Buffer;
                // Получаем IP и порт отправителя
                IPEndPoint senderEp = receivedResult.RemoteEndPoint;

                string receivedMessage = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                //Console.WriteLine($"Получен широковещательный запрос от {senderEp}: {receivedMessage}");

                if (receivedMessage == "DISCOVER_SERVER")
                {
                    string responseData = $"{IpAddress}:{Port}";
                    byte[] responseBytes = Encoding.UTF8.GetBytes(responseData);
                    await udpClient.SendAsync(responseBytes, responseBytes.Length, senderEp);
                    //Console.WriteLine($"Отправлен ответ {responseData} на {senderEp}");
                }
            }

        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }

    private static async Task GameLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (gameRunning && !gamePaused)
            {
                var currentTime = DateTime.UtcNow;
                var elapsedSeconds = (int)(currentTime - periodStartTime).TotalSeconds;

                // Закончился ли период
                if (elapsedSeconds >= remainingTimeSeconds)
                {
                    gameRunning = false;
                    gamePaused = false;
                    remainingTimeSeconds = 0;
                    Console.WriteLine("Период завершен.");
                }

                var gameState = GetGameState(elapsedSeconds);
                //Console.WriteLine($"Отправка данных: {gameState}");
                var data = Encoding.UTF8.GetBytes(gameState);

                List<TcpClient> clientsCopy;
                lock (clients)
                {
                    clientsCopy = new List<TcpClient>(clients);
                }

                // Список отключившихся клиентов
                List<TcpClient> disconnectedClients = new List<TcpClient>();
                foreach (var client in clientsCopy)
                {
                    if (!client.Connected)
                    {
                        disconnectedClients.Add(client);
                        continue;
                    }

                    var stream = client.GetStream();
                    try
                    {
                        await stream.WriteAsync(data, 0, data.Length);
                    }
                    catch
                    {
                        client.Close();
                        disconnectedClients.Add(client);
                    }
                }

                // Удаляем отключившихся клиентов из основного списка
                lock (clients)
                {
                    foreach (var disconnectedClient in disconnectedClients)
                    {
                        clients.Remove(disconnectedClient);
                    }
                }

                await Task.Delay(1000);
            }
        }
    }

    private static string GetGameState(int elapsedSeconds = -1)
    {
        int displayTimeSeconds = remainingTimeSeconds;
        if (elapsedSeconds >= 0)
        {
            displayTimeSeconds = Math.Max(0, remainingTimeSeconds - elapsedSeconds);
        }
        int displayMinutes = displayTimeSeconds / 60;
        int displaySeconds = displayTimeSeconds % 60;

        var goals = string.Join("; ", goalEvents);

        // Формируем строку состояния игры
        return $"{{Период {period}}}{{{team1}}}{{{team2}}}{{{scores[0]}}}{{{scores[1]}}}{{{displayMinutes}}}{{{displaySeconds}}}{{{goals}}}";
    }

    private static async Task CommandLoop()
    {
        while (true)
        {
            try
            {
                // Считываем команду с консоли
                var command = Console.ReadLine();
                var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var action = parts[0].ToLower();

                switch (action)
                {
                    // Устанавливаем название первой команды
                    case "установить_команду1":
                        team1 = parts[1];
                        break;
                    // Устанавливаем название второй команды
                    case "установить_команду2":
                        team2 = parts[1];
                        break;
                    // Устанавливаем номер текущего периода
                    case "установить_период":
                        if (int.TryParse(parts[1], out int newPeriod))
                        {
                            period = newPeriod;
                        }
                        break;
                    // Устанавливаем продолжительность текущего периода
                    case "установить_время":
                        if (int.TryParse(parts[1], out int minutes) && int.TryParse(parts[2], out int seconds))
                        {
                            periodDurationSeconds = minutes * 60 + seconds;
                            remainingTimeSeconds = periodDurationSeconds;
                        }
                        break;
                    // Начинаем текущий период
                    case "начать":
                        if (!gameRunning)
                        {
                            periodStartTime = DateTime.UtcNow;
                            gameRunning = true;
                            gamePaused = false;
                            cts.Cancel();
                            cts = new CancellationTokenSource();
                            StartGameLoop();
                        }
                        else if (gamePaused)
                        {
                            periodStartTime = DateTime.UtcNow;
                            gamePaused = false;
                        }
                        break;
                    // Останавливаем текущий период
                    case "остановить":
                        if (gameRunning && !gamePaused)
                        {
                            remainingTimeSeconds -= (int)(DateTime.UtcNow - periodStartTime).TotalSeconds;
                            remainingTimeSeconds = Math.Max(remainingTimeSeconds, 0);
                            gamePaused = true;
                        }
                        break;
                    // Начинаем новую игру
                    case "новая_игра":
                        period = 1;
                        scores[0] = 0;
                        scores[1] = 0;
                        periodDurationSeconds = 600;
                        remainingTimeSeconds = periodDurationSeconds;
                        gameRunning = false;
                        gamePaused = false;
                        goalEvents.Clear();
                        break;
                    // Добавляем событие гола
                    case "добавить_гол":
                        if (parts.Length == 6 &&
                            int.TryParse(parts[1], out int teamNumber) &&
                            int.TryParse(parts[2], out int playerNumber) &&
                            !string.IsNullOrEmpty(parts[3]) &&
                            int.TryParse(parts[4], out int goalMinute) &&
                            int.TryParse(parts[5], out int goalSecond))
                        {
                            string goalEvent = $"Команда {teamNumber}, Игрок #{playerNumber} {parts[3]}, Время {goalMinute}:{goalSecond}";
                            goalEvents.Add(goalEvent);

                            if (teamNumber == 1)
                            {
                                scores[0]++;
                            }
                            else if (teamNumber == 2)
                            {
                                scores[1]++;
                            }
                        }
                        break;
                    default:
                        Console.WriteLine("Неизвестная команда");
                        break;
                }

                // Отправляем обновленное состояние игры клиентам
                var gameState = GetGameState(gameRunning && !gamePaused ? (int)(DateTime.UtcNow - periodStartTime).TotalSeconds : -1);
                var data = Encoding.UTF8.GetBytes(gameState);

                // Создаем копию списка клиентов для безопасной итерации
                List<TcpClient> clientsCopy;
                lock (clients)
                {
                    clientsCopy = new List<TcpClient>(clients);
                }

                // Список отключившихся клиентов
                List<TcpClient> disconnectedClients = new List<TcpClient>();
                foreach (var client in clientsCopy)
                {
                    if (!client.Connected)
                    {
                        disconnectedClients.Add(client);
                        continue;
                    }

                    var stream = client.GetStream();
                    try
                    {
                        await stream.WriteAsync(data, 0, data.Length);
                    }
                    catch
                    {
                        client.Close();
                        disconnectedClients.Add(client);
                    }
                }

                // Удаляем отключившихся клиентов из основного списка
                lock (clients)
                {
                    foreach (var disconnectedClient in disconnectedClients)
                    {
                        clients.Remove(disconnectedClient);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
        }
    }

    private static void StartGameLoop()
    {
        Task.Run(() => GameLoop(cts.Token));
    }
}
