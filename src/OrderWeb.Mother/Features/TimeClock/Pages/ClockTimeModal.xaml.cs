using OrderWeb.SharedUI.Views;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public class ClockTimeModal : ContentPage
{
    private static readonly TimeSpan SuccessCloseDelay = TimeSpan.FromMilliseconds(900);

    private readonly AuthenticationService _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
    private readonly TimeClockService _timeClockService = ServiceHelper.GetService<TimeClockService>() ?? new TimeClockService(new DatabaseService());
    private readonly StaffClockView _clock = new();

    private User? _authenticatedUser;
    private bool _busy;

    public ClockTimeModal()
    {
        BackgroundColor = Color.FromArgb("#F1F5F9");
        Shell.SetNavBarIsVisible(this, false);
        Content = _clock;
        _clock.PinSubmitted += async (_, pin) => await ValidatePinAsync(pin);
        _clock.ClockInRequested += async (_, _) => await ExecuteClockActionAsync(clockOut: false);
        _clock.ClockOutRequested += async (_, _) => await ExecuteClockActionAsync(clockOut: true);
        _clock.CloseRequested += async (_, _) => await CloseModalAsync();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _authenticatedUser = null;
        _clock.Begin();
    }

    protected override void OnDisappearing()
    {
        _clock.End();
        base.OnDisappearing();
    }

    private async Task ValidatePinAsync(string pin)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _clock.SetChecking(true);
        try
        {
            var result = await _authService.ValidatePinAsync(pin);
            if (!result.Success || result.User == null)
            {
                _clock.ShowPinError(string.IsNullOrWhiteSpace(result.Message) ? "Wrong PIN" : result.Message);
                return;
            }

            _authenticatedUser = result.User;
            await ShowShiftAsync();
        }
        finally
        {
            _busy = false;
            _clock.SetChecking(false);
        }
    }

    private async Task ShowShiftAsync()
    {
        if (_authenticatedUser == null)
        {
            return;
        }

        var state = await _timeClockService.GetDashboardStateAsync(_authenticatedUser);
        if (state == null)
        {
            _clock.ShowShift(new StaffClockShift(
                StaffName(_authenticatedUser),
                IsClockedIn: false,
                ClockInTimeDisplay: null,
                TodayHoursDisplay: "0h 0m"));
            _clock.ShowMessage("Time clock is not available. Check database connection on the mother terminal.", success: false);
            _clock.SetActionsEnabled(false);
            return;
        }

        _clock.ShowShift(new StaffClockShift(
            StaffName(state.User),
            state.IsClockedIn,
            state.IsClockedIn ? state.OpenSession!.ClockInAt.ToString("h:mm tt") : null,
            state.TodayHoursDisplay));
    }

    private async Task ExecuteClockActionAsync(bool clockOut)
    {
        if (_busy || _authenticatedUser == null)
        {
            return;
        }

        _busy = true;
        _clock.SetActionsEnabled(false);
        try
        {
            var result = clockOut
                ? await _timeClockService.ClockOutAsync(_authenticatedUser)
                : await _timeClockService.ClockInAsync(_authenticatedUser);

            if (result.Success)
            {
                _clock.ShowMessage(result.Message, success: true);
                await Task.Delay(SuccessCloseDelay);
                await CloseModalAsync();
                return;
            }

            _clock.ShowMessage(result.Message, success: false);
            await ShowShiftAsync();
        }
        catch (Exception ex)
        {
            _clock.ShowMessage(ex.Message, success: false);
            await ShowShiftAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task CloseModalAsync()
    {
        if (Navigation.ModalStack.Count > 0)
        {
            await Navigation.PopModalAsync(false);
        }
    }

    private static string StaffName(User user) =>
        string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name;
}
