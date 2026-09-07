using OrderWeb.Client.Pages.Dashboards;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Dtos;
using OrderWeb.SharedUI.ViewModels;

namespace OrderWeb.Client.Pages.Auth;

public partial class LoginPage : ContentPage
{
    private readonly ClientAuthenticationService _auth;
    private readonly LoginViewModel _loginViewModel;

    public LoginPage()
    {
        InitializeComponent();
        _auth = new ClientAuthenticationService(new MotherAuthClient());
        _loginViewModel = new LoginViewModel(_auth);
        SharedLogin.ViewModel = _loginViewModel;
        _loginViewModel.SetRestaurantName("Restaurant POS");
        _loginViewModel.LoginSucceeded += OnLoginSucceeded;
        _loginViewModel.ClockInOutRequested += (_, _) => { /* staff clock handled by Mother modal on Mother; Client shows status via VM errors */ };
        _loginViewModel.MinimizeRequested += (_, _) => ClientWindowService.MinimizeMainWindow();
    }

    private async void OnLoginSucceeded(object? sender, UserSession session)
    {
        if (string.Equals(session.User.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(session.User.Role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
        {
            await DisplayAlertAsync(
                "Mother POS only",
                "Administrator access is available on the Mother POS only. Please use the Mother POS terminal.",
                "OK");
            return;
        }

        Page page = session.User.Role switch
        {
            "Manager" => new ManagerDashboardPage(session),
            _ => new UserDashboardPage(session)
        };
        await Navigation.PushAsync(page, false);
    }
}
