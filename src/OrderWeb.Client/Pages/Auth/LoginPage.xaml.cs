using OrderWeb.Client.Models;
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
            return;
        }

        // Persist Mother features/routes from PIN login (UserSession alone drops them).
        var login = _auth.LastSuccessfulLogin;
        if (login is not null)
        {
            await new ClientCacheService().SaveLoginSessionAsync(login);
            ClientHostAccess.ApplyFromSession(login);
        }
        else
        {
            login = new LoginSession(
                session.User.UserId,
                session.User.DisplayName,
                session.User.Role,
                session.User.Permissions.ToList(),
                session.SessionId,
                session.ExpiresAtUtc);
        }

        Page page = login.Role switch
        {
            "Manager" => new ManagerDashboardPage(login),
            _ => new UserDashboardPage(login)
        };
        await Navigation.PushAsync(page, false);
    }
}
