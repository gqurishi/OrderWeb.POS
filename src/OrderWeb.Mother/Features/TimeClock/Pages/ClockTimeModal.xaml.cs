using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class ClockTimeModal : ContentPage
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SuccessCloseDelay = TimeSpan.FromMilliseconds(900);

    private readonly AuthenticationService _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
    private readonly TimeClockService _timeClockService = ServiceHelper.GetService<TimeClockService>() ?? new TimeClockService(new DatabaseService());

    private string _pin = string.Empty;
    private User? _authenticatedUser;
    private TimeClockDashboardState? _dashboardState;
    private IDispatcherTimer? _idleTimer;
    private DateTime _lastActivityUtc = DateTime.UtcNow;
    private bool _isBusy;

    public ClockTimeModal()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ResetPinEntry();
        ShowPinStep();
        StartIdleTimer();
        RegisterActivity();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopIdleTimer();
    }

    private void StartIdleTimer()
    {
        StopIdleTimer();
        _idleTimer = Dispatcher.CreateTimer();
        _idleTimer.Interval = TimeSpan.FromSeconds(1);
        _idleTimer.Tick += OnIdleTimerTick;
        _idleTimer.Start();
    }

    private void StopIdleTimer()
    {
        if (_idleTimer == null)
        {
            return;
        }

        _idleTimer.Tick -= OnIdleTimerTick;
        _idleTimer.Stop();
        _idleTimer = null;
    }

    private async void OnIdleTimerTick(object? sender, EventArgs e)
    {
        var remaining = IdleTimeout - (DateTime.UtcNow - _lastActivityUtc);
        if (remaining <= TimeSpan.Zero)
        {
            await CloseModalAsync();
            return;
        }

        IdleCountdownLabel.Text = $"Closing in {Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))}s";
    }

    private void RegisterActivity()
    {
        _lastActivityUtc = DateTime.UtcNow;
    }

    private async Task CloseModalAsync()
    {
        if (Navigation.ModalStack.Count > 0)
        {
            await Navigation.PopModalAsync(false);
        }
    }

    private void ShowPinStep()
    {
        PinPanel.IsVisible = true;
        DashboardPanel.IsVisible = false;
        HeaderTitleLabel.Text = "Staff Clock";
        HeaderSubtitleLabel.Text = "Enter your PIN";
        IdleCountdownLabel.IsVisible = true;
    }

    private void ShowDashboardStep()
    {
        PinPanel.IsVisible = false;
        DashboardPanel.IsVisible = true;
        HeaderTitleLabel.Text = "Staff Clock";
        HeaderSubtitleLabel.Text = "Clock in or clock out";
        IdleCountdownLabel.IsVisible = true;
    }

    private async Task RefreshDashboardAsync()
    {
        if (_authenticatedUser == null)
        {
            return;
        }

        _dashboardState = await _timeClockService.GetDashboardStateAsync(_authenticatedUser);
        if (_dashboardState == null)
        {
            ShowDashboardMessage("Time clock is not available. Check database connection on the mother terminal.", success: false);
            ApplyActionButtons(isClockedIn: false, allowActions: false);
            return;
        }

        StaffNameLabel.Text = string.IsNullOrWhiteSpace(_dashboardState.User.Name)
            ? _dashboardState.User.Username
            : _dashboardState.User.Name;
        StaffHoursLabel.Text = _dashboardState.TodayHoursDisplay;

        var isClockedIn = _dashboardState.IsClockedIn;
        ClockInTimePanel.IsVisible = isClockedIn;
        StaffStatusLabel.IsVisible = !isClockedIn;

        if (isClockedIn)
        {
            ClockInTimeLabel.Text = _dashboardState.OpenSession!.ClockInAt.ToString("h:mm tt");
            ShiftInfoCard.BackgroundColor = Color.FromArgb("#F0FDF4");
            ShiftInfoCard.Stroke = Color.FromArgb("#86EFAC");
        }
        else
        {
            StaffStatusLabel.Text = "Ready to clock in";
            ShiftInfoCard.BackgroundColor = Color.FromArgb("#EEF2FF");
            ShiftInfoCard.Stroke = Color.FromArgb("#C7D2FE");
        }

        HeaderSubtitleLabel.Text = isClockedIn
            ? "You are on shift — clock out when finished"
            : "Start your shift for today";

        ApplyActionButtons(isClockedIn, allowActions: true);
    }

    private void ApplyActionButtons(bool isClockedIn, bool allowActions)
    {
        ClockInButton.IsVisible = !isClockedIn;
        ClockOutButton.IsVisible = isClockedIn;
        ClockInButton.IsEnabled = allowActions && !isClockedIn;
        ClockOutButton.IsEnabled = allowActions && isClockedIn;
    }

    private void ShowDashboardMessage(string message, bool success)
    {
        DashboardMessageLabel.Text = message;
        DashboardMessageFrame.IsVisible = true;
        DashboardMessageFrame.BackgroundColor = success ? Color.FromArgb("#ECFDF5") : Color.FromArgb("#FEF2F2");
        DashboardMessageFrame.Stroke = success ? Color.FromArgb("#10B981") : Color.FromArgb("#EF4444");
        DashboardMessageLabel.TextColor = success ? Color.FromArgb("#047857") : Color.FromArgb("#B91C1C");
    }

    private void ResetPinEntry(bool hideError = true)
    {
        _pin = string.Empty;
        if (hideError)
        {
            PinErrorFrame.IsVisible = false;
        }

        UpdatePinDots();
    }

    private void ShowPinError(string message)
    {
        PinErrorLabel.Text = message;
        PinErrorFrame.IsVisible = true;
        SetPinDotsErrorState(true);
        RegisterActivity();
    }

    private void SetPinDotsErrorState(bool isError)
    {
        var color = isError ? Color.FromArgb("#EF4444") : Color.FromArgb("#E5E7EB");
        var filled = Color.FromArgb("#6366F1");
        Dot1.BackgroundColor = _pin.Length >= 1 ? filled : color;
        Dot2.BackgroundColor = _pin.Length >= 2 ? filled : color;
        Dot3.BackgroundColor = _pin.Length >= 3 ? filled : color;
        Dot4.BackgroundColor = _pin.Length >= 4 ? filled : color;
    }

    private void UpdatePinDots()
    {
        SetPinDotsErrorState(isError: false);

        if (_pin.Length == 4)
        {
            _ = ValidatePinAndOpenDashboardAsync();
        }
    }

    private async Task ValidatePinAndOpenDashboardAsync()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        PinLoadingIndicator.IsLoading = true;
        PinErrorFrame.IsVisible = false;
        RegisterActivity();

        try
        {
            var result = await _authService.ValidatePinAsync(_pin);
            if (!result.Success || result.User == null)
            {
                _pin = string.Empty;
                UpdatePinDots();
                ShowPinError(string.IsNullOrWhiteSpace(result.Message) ? "Wrong PIN" : result.Message);
                return;
            }

            _authenticatedUser = result.User;
            ShowDashboardStep();
            DashboardMessageFrame.IsVisible = false;
            await RefreshDashboardAsync();
        }
        finally
        {
            PinLoadingIndicator.IsLoading = false;
            _isBusy = false;
        }
    }

    private async Task ExecuteClockActionAsync(bool clockOut)
    {
        if (_isBusy || _authenticatedUser == null)
        {
            return;
        }

        RegisterActivity();
        _isBusy = true;
        ClockInButton.IsEnabled = false;
        ClockOutButton.IsEnabled = false;
        StopIdleTimer();

        try
        {
            var result = clockOut
                ? await _timeClockService.ClockOutAsync(_authenticatedUser)
                : await _timeClockService.ClockInAsync(_authenticatedUser);

            if (result.Success)
            {
                ShowDashboardMessage(result.Message, success: true);
                await Task.Delay(SuccessCloseDelay);
                await CloseModalAsync();
                return;
            }

            ShowDashboardMessage(result.Message, success: false);
            await RefreshDashboardAsync();
            StartIdleTimer();
        }
        catch (Exception ex)
        {
            ShowDashboardMessage(ex.Message, success: false);
            await RefreshDashboardAsync();
            StartIdleTimer();
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async void OnClockInClicked(object sender, EventArgs e) =>
        await ExecuteClockActionAsync(clockOut: false);

    private async void OnClockOutClicked(object sender, EventArgs e) =>
        await ExecuteClockActionAsync(clockOut: true);

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        RegisterActivity();
        await CloseModalAsync();
    }

    private void OnKeyClicked(object sender, EventArgs e)
    {
        if (_isBusy || !PinPanel.IsVisible || _pin.Length >= 4)
        {
            return;
        }

        if (sender is Button { Text: { } digit } && digit.Length == 1 && char.IsDigit(digit[0]))
        {
            PinErrorFrame.IsVisible = false;
            RegisterActivity();
            _pin += digit;
            UpdatePinDots();
        }
    }

    private void OnClearClicked(object sender, EventArgs e)
    {
        RegisterActivity();
        ResetPinEntry();
    }

    private void OnBackspaceClicked(object sender, EventArgs e)
    {
        if (_isBusy || _pin.Length == 0)
        {
            return;
        }

        RegisterActivity();
        _pin = _pin[..^1];
        UpdatePinDots();
    }
}
