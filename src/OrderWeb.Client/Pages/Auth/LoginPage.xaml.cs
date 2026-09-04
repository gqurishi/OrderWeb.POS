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
                WidthRequest = 18,
                HeightRequest = 18,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 9 },
                BackgroundColor = i < _pin.Length ? Color.FromArgb("#6366F1") : Color.FromArgb("#E5E7EB")
            });
        }
    }

    private void OnSharedKeyPressed(object? sender, OrderWeb.SharedUI.Controls.KeypadKeyEventArgs e) => OnPinKey(e.Key);
    private void OnSharedClearPressed(object? sender, EventArgs e) => OnPinKey("Clear");
    private void OnSharedBackspacePressed(object? sender, EventArgs e) => OnPinKey("X");

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

    private void OnMinimizeClicked(object sender, EventArgs e)
    {
        ClientWindowService.MinimizeMainWindow();
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
