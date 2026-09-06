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
        Page page = session.User.Role switch
        {
            "Manager" => new ManagerDashboardPage(session),
            "Admin" => new AdminDashboardPage(session),
            "SuperAdmin" => new AdminDashboardPage(session),
            _ => new UserDashboardPage(session)
        };
        await Navigation.PushAsync(page, false);
    }
}
