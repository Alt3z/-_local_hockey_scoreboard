using System;
using System.Linq; 
using System.Net; 
using System.Net.Sockets; 
using System.Text; 
using System.Threading.Tasks; 
using Avalonia; 
using Avalonia.Controls; 
using Avalonia.Markup.Xaml; 
using Avalonia.Threading; 

namespace TabloClient 
{
    public partial class MainWindow : Window 
    {
        private TextBlock _team1Name;
        private TextBlock _team2Name;
        private TextBlock _team1Score;
        private TextBlock _team2Score;
        private TextBlock _minutesText;
        private TextBlock _secondsText;
        private TextBlock _periodText;
        private TextBlock _team1Goals1;
        private TextBlock _team2Goals1;
        private TextBlock _team1Goals2;
        private TextBlock _team2Goals2;
        private TextBlock _team1Goals3;
        private TextBlock _team2Goals3;
        private TextBlock _team1Goals4;
        private TextBlock _team2Goals4;
        private TextBlock _team1Goals5;
        private TextBlock _team2Goals5;

        private string _serverIp;
        private int _serverPort;

        public MainWindow()
        {
            InitializeComponent();
            _team1Name = this.FindControl<TextBlock>("Team1Name");
            _team2Name = this.FindControl<TextBlock>("Team2Name");
            _team1Score = this.FindControl<TextBlock>("Team1Score");
            _team2Score = this.FindControl<TextBlock>("Team2Score");
            _minutesText = this.FindControl<TextBlock>("MinutesText");
            _secondsText = this.FindControl<TextBlock>("SecondsText");
            _periodText = this.FindControl<TextBlock>("PeriodText");
            _team1Goals1 = this.FindControl<TextBlock>("Team1Goals1");
            _team2Goals1 = this.FindControl<TextBlock>("Team2Goals1");
            _team1Goals2 = this.FindControl<TextBlock>("Team1Goals2");
            _team2Goals2 = this.FindControl<TextBlock>("Team2Goals2");
            _team1Goals3 = this.FindControl<TextBlock>("Team1Goals3");
            _team2Goals3 = this.FindControl<TextBlock>("Team2Goals3");
            _team1Goals4 = this.FindControl<TextBlock>("Team1Goals4");
            _team2Goals4 = this.FindControl<TextBlock>("Team2Goals4");
            _team1Goals5 = this.FindControl<TextBlock>("Team1Goals5");
            _team2Goals5 = this.FindControl<TextBlock>("Team2Goals5");

            
            Task.Run(async () => {
                await DiscoverServer();
                await ConnectToServer();
            });
        }

        private async Task DiscoverServer() // Метод для обнаружения сервера
        {
            UdpClient udpClient = new UdpClient(); // Создание UDP клиента
            udpClient.EnableBroadcast = true; // Включение широковещательной передачи

            var requestData = Encoding.UTF8.GetBytes("DISCOVER_SERVER"); 
            var serverEp = new IPEndPoint(IPAddress.Broadcast, 5001);

            udpClient.Send(requestData, requestData.Length, serverEp); // Отправка запроса

            var clientEp = new IPEndPoint(IPAddress.Any, 0);
            var serverResponseData = udpClient.Receive(ref clientEp); // Получение ответа от сервера

            var serverResponse = Encoding.UTF8.GetString(serverResponseData); 
            var parts = serverResponse.Split(':'); 
            _serverIp = parts[0];
            _serverPort = int.Parse(parts[1]);

            //Console.WriteLine($"Получен сервер: IP={_serverIp}, Port={_serverPort}"); // Логирование полученных данных
        }

        private async Task ConnectToServer() // Метод для подключения к серверу
        {
            var client = new TcpClient(); // Создание TCP клиента
            await client.ConnectAsync(_serverIp, _serverPort);
            var stream = client.GetStream();

            var buffer = new byte[1024]; // Буфер для получения данных

            while (true) 
            {
                try
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) continue;

                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    //Console.WriteLine($"Получено сообщение: {message}");

                    var parts = message.Split(new[] { '{', '}' }, StringSplitOptions.RemoveEmptyEntries);

                    string periodPart = parts[0].Trim().Split(' ')[1]; // Номера периода
                    string team1Name = parts[1].Trim(); // Названия команды 1
                    string team2Name = parts[2].Trim(); // Названия команды 2
                    int.TryParse(parts[3].Trim(), out int team1Score); // Счет команды 1
                    int.TryParse(parts[4].Trim(), out int team2Score); // Счет команды 2
                    int.TryParse(parts[5].Trim(), out int minutes); // Минуты
                    int.TryParse(parts[6].Trim(), out int seconds); // Секунды

                    string[] team1Goals = new string[5]; // Голы команды 1
                    string[] team2Goals = new string[5]; // Голы команды 2
                    if (parts.Length > 7) // Если есть голы
                    {
                        string[] goals = parts[7].Trim().Split(';');
                        var team1GoalsList = Array.FindAll(goals, goal => goal.Contains("Команда 1")).Select(g => FormatGoalEvent(g)).ToList(); 
                        var team2GoalsList = Array.FindAll(goals, goal => goal.Contains("Команда 2")).Select(g => FormatGoalEvent(g)).ToList();

                        for (int i = 0; i < 5; i++)
                        {
                            team1Goals[i] = team1GoalsList.ElementAtOrDefault(i) ?? "";
                            team2Goals[i] = team2GoalsList.ElementAtOrDefault(i) ?? "";
                        }
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        _periodText.Text = $"Период: {periodPart}";
                        _team1Name.Text = team1Name;
                        _team2Name.Text = team2Name; 
                        _team1Score.Text = team1Score.ToString(); 
                        _team2Score.Text = team2Score.ToString(); 
                        _minutesText.Text = minutes.ToString("D2"); 
                        _secondsText.Text = seconds.ToString("D2");

                        _team1Goals1.Text = team1Goals[0];
                        _team2Goals1.Text = team2Goals[0];
                        _team1Goals2.Text = team1Goals[1];
                        _team2Goals2.Text = team2Goals[1];
                        _team1Goals3.Text = team1Goals[2];
                        _team2Goals3.Text = team2Goals[2];
                        _team1Goals4.Text = team1Goals[3];
                        _team2Goals4.Text = team2Goals[3];
                        _team1Goals5.Text = team1Goals[4];
                        _team2Goals5.Text = team2Goals[4];
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                }
            }
        }

        private string FormatGoalEvent(string goalEvent) // Метод для форматирования событий голов
        {
            var parts = goalEvent.Split(',');
            var playerNumber = parts[1].Trim().Split('#')[1]; 
            var time = parts[2].Trim().Split(' ')[1]; 
            return $"№{playerNumber} {time}";
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this); 
            this.CanResize = false;
        }
    }
}
