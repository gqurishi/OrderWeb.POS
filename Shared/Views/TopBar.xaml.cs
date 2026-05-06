using POS_in_NET.Services;
using POS_in_NET.Pages;

namespace POS_in_NET.Views;

public partial class TopBar : ContentView
{
    private System.Timers.Timer? _timer;

    public TopBar()
    {
        InitializeComponent();
        
        // Initialize date/time
        UpdateDateTime();
        
        // Setup timer to update every second
        _timer = new System.Timers.Timer(1000);
        _timer.Elapsed += (s, e) => UpdateDateTime();
        _timer.Start();
    }

    private void UpdateDateTime()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var now = DateTime.Now;
            DateTimeLabel.Text = now.ToString("dddd, MMMM dd, yyyy");
            TimeLabel.Text = now.ToString("HH:mm:ss");
        });
    }

    private void OnMenuClicked(object sender, EventArgs e)
    {
        // Open the Shell flyout menu
        Shell.Current.FlyoutIsPresented = true;
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("🔴 Logout button clicked");
            
            // Clear authentication immediately - no confirmation
            var authService = ServiceHelper.GetService<AuthenticationService>();
            if (authService != null)
            {
                await authService.LogoutAsync();
                System.Diagnostics.Debug.WriteLine("✅ Logout successful");
            }
            
            // Navigate to login page
            await Shell.Current.GoToAsync("//login");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Logout error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"❌ Stack trace: {ex.StackTrace}");
            // Still navigate to login even if logout service fails
            try
            {
                await Shell.Current.GoToAsync("//login");
            }
            catch (Exception navEx)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Navigation error: {navEx.Message}");
            }
        }
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        
        // Stop timer when control is removed
        if (Handler == null)
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void SetPageTitle(string title)
    {
        PageTitleLabel.Text = title;
    }

    public void SetCustomContent(View? content)
    {
        if (content != null)
        {
            CustomContentPresenter.Content = content;
            CustomContentPresenter.IsVisible = true;
        }
        else
        {
            CustomContentPresenter.Content = null;
            CustomContentPresenter.IsVisible = false;
        }
    }
}
