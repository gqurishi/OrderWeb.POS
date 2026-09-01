using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Dashboards;
using OrderWeb.Client.Services;
using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.Client.Pages.Auth;

public partial class LoginPage : ContentPage
{
    private readonly MotherAuthClient _authClient = new();
    private readonly IDispatcherTimer _clockTimer;
    private string _pin = string.Empty;

    public LoginPage()
    {
        InitializeComponent();
        BuildPinDots();
        BuildKeypad();

        _clockTimer = Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _clockTimer.Stop();
    }

    private void BuildPinDots()
    {
        PinDots.Children.Clear();
        for (var i = 0; i < 4; i++)
        {
            PinDots.Children.Add(new Border
            {
                WidthRequest = 20,
                HeightRequest = 20,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                BackgroundColor = i < _pin.Length ? Color.FromArgb("#6366F1") : Color.FromArgb("#E5E7EB")
            });
        }
    }

    private void BuildKeypad()
    {
        KeypadGrid.Children.Clear();
        var values = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "Clear", "0", "X" };
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var key = RoundKey(value);
            KeypadGrid.Children.Add(key);
            Grid.SetRow(key, i / 3);
            Grid.SetColumn(key, i % 3);
        }
    }

    private Border RoundKey(string text)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Colors.Transparent,
            TextColor = text == "X" ? Color.FromArgb("#DC2626") : Color.FromArgb("#1F2937"),
            FontFamily = text.Length == 1 ? "InterBold" : "OpenSansRegular",
            FontSize = text == "X" ? 32 : text.Length == 1 ? 36 : 18
        };
        button.Clicked += (_, _) => OnPinKey(text);

        return new Border
        {
            WidthRequest = 100,
            HeightRequest = 100,
            Stroke = Color.FromArgb("#222222"),
            StrokeThickness = 3,
            StrokeShape = new RoundRectangle { CornerRadius = 50 },
            BackgroundColor = Colors.White,
            Content = button,
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.12f, Radius = 8, Offset = new Point(0, 3) }
        };
    }

    private async void OnPinKey(string value)
    {
        HideStatus();
        if (value == "Clear")
        {
            _pin = string.Empty;
        }
        else if (value == "X")
        {
            _pin = _pin.Length > 0 ? _pin[..^1] : string.Empty;
        }
        else if (_pin.Length < 4)
        {
            _pin += value;
        }

        BuildPinDots();
        if (_pin.Length == 4)
        {
            await LoginAsync();
        }
    }

    private async Task LoginAsync()
    {
        ConnectionStatus.Status = "Syncing";
        ShowStatus("Checking PIN...", "#374151");
        var result = await _authClient.ValidatePinAsync(_pin);
        if (!result.Success || result.User == null)
        {
            _pin = string.Empty;
            BuildPinDots();
            ConnectionStatus.Status = "Connected";
            ShowStatus(string.IsNullOrWhiteSpace(result.Message) ? "Wrong PIN" : result.Message, "#DC2626");
            return;
        }

        _pin = string.Empty;
        BuildPinDots();
        ConnectionStatus.Status = "Connected";
        if (result.User.Role == "Staff")
        {
            ShowStatus("Staff PIN is for Clock In/Out only.", "#DC2626");
            return;
        }

        await Navigation.PushAsync(result.User.Role switch
        {
            "Manager" => new ManagerDashboardPage(),
            "Admin" => new AdminDashboardPage(),
            _ => new UserDashboardPage()
        }, false);
    }

    private void OnClockInOutClicked(object sender, EventArgs e)
    {
        ShowStatus("Clock In/Out uses staff PIN only.", "#374151");
    }

    private void ShowStatus(string message, string color)
    {
        StatusLabel.Text = message;
        StatusLabel.TextColor = Color.FromArgb(color);
        StatusLabel.IsVisible = true;
    }

    private void HideStatus()
    {
        StatusLabel.IsVisible = false;
    }

    private void UpdateClock()
    {
        LoginDateLabel.Text = DateTime.Now.ToString("dddd, MMMM d, yyyy");
        LoginTimeLabel.Text = DateTime.Now.ToString("HH:mm:ss");
    }
}
