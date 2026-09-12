using OrderWeb.Client.Services;
using OrderWeb.Contracts.Dtos;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client;

public sealed class ClockTimeModal : ContentPage
{
    private static readonly TimeSpan SuccessCloseDelay = TimeSpan.FromMilliseconds(900);

    private readonly MotherTimeClockClient _timeClock = new();
    private readonly StaffClockView _clock = new();

    private string? _pin;
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
        _pin = null;
        _clock.Begin();
    }

    protected override void OnDisappearing()
    {
        _pin = null;
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
            var result = await _timeClock.StatusAsync(pin);
            if (!result.Success)
            {
                if (string.Equals(result.ErrorCode, TimeClockErrorCodes.Unavailable, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(result.StaffName))
                {
                    _pin = pin;
                    ShowShift(result);
                    _clock.ShowMessage(result.Message ?? result.Error ?? "Time clock is not available.", success: false);
                    _clock.SetActionsEnabled(false);
                    return;
                }

                _pin = null;
                _clock.ShowPinError(result.Message ?? result.Error ?? "Wrong PIN");
                return;
            }

            _pin = pin;
            ShowShift(result);
        }
        finally
        {
            _busy = false;
            _clock.SetChecking(false);
        }
    }

    private async Task ExecuteClockActionAsync(bool clockOut)
    {
        if (_busy || string.IsNullOrWhiteSpace(_pin))
        {
            return;
        }

        _busy = true;
        _clock.SetActionsEnabled(false);
        try
        {
            var result = clockOut
                ? await _timeClock.ClockOutAsync(_pin)
                : await _timeClock.ClockInAsync(_pin);

            if (result.Success)
            {
                _clock.ShowMessage(result.Message ?? (clockOut ? "Clocked out." : "Clocked in."), success: true);
                await Task.Delay(SuccessCloseDelay);
                await CloseModalAsync();
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.StaffName))
            {
                ShowShift(result);
            }
            else
            {
                _clock.SetActionsEnabled(true);
            }

            _clock.ShowMessage(result.Message ?? result.Error ?? "Could not update the clock.", success: false);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowShift(ClientTimeClockResponseDto result)
    {
        _clock.ShowShift(new StaffClockShift(
            result.StaffName ?? "Staff",
            result.IsClockedIn,
            result.ClockInTimeDisplay,
            result.TodayHoursDisplay ?? "0h 0m"));
    }

    private async Task CloseModalAsync()
    {
        if (Navigation.ModalStack.Count > 0)
        {
            await Navigation.PopModalAsync(false);
        }
    }
}
