using OrderWeb.Client.Pages.Dashboards;
using OrderWeb.Client.Services;

namespace OrderWeb.Client.Pages.Auth;

public partial class LoginPage : ContentPage
{
    private readonly ClientAuthenticationService _authService = new();
    private IDispatcherTimer? _clockTimer;

    public LoginPage()
    {
        InitializeComponent();
        _clockTimer = Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => LoginView.UpdateClock();
        _clockTimer.Start();
        LoginView.UpdateClock();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _clockTimer?.Stop();
    }

    private async void OnPinCompleted(object? sender, string pin)
        => await LoginAsync(pin);

    private void OnClockActionRequested(object? sender, EventArgs e)
        => LoginView.StatusMessage = "Clock In/Out uses staff PIN only.";

    private void OnMinimizeRequested(object? sender, EventArgs e)
        => ClientWindowService.MinimizeMainWindow();

    private async Task LoginAsync(string pin)
    {
        LoginView.ClearMessages();
        LoginView.BusyMessage = "Checking PIN...";
        LoginView.IsBusy = true;
        try
        {
            var result = await _authService.LoginWithPinAsync(pin);
            if (!result.Success || result.Session is null)
            {
                LoginView.ClearPin();
                LoginView.ErrorMessage = string.IsNullOrWhiteSpace(result.Message) ? "Wrong PIN" : result.Message;
                return;
            }

            LoginView.ClearPin();
            if (string.Equals(result.Session.Role, "Staff", StringComparison.OrdinalIgnoreCase))
            {
                LoginView.ErrorMessage = "Staff PIN is for Clock In/Out only.";
                return;
            }

            await Navigation.PushAsync(result.Session.Role switch
            {
                "Manager" => new ManagerDashboardPage(),
                "Admin" => new AdminDashboardPage(),
                _ => new UserDashboardPage()
            }, false);
        }
        catch (Exception ex)
        {
            LoginView.ClearPin();
            LoginView.ErrorMessage = ex.Message;
        }
        finally
        {
            LoginView.IsBusy = false;
        }
    }
}
