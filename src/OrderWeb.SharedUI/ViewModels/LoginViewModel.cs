using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;

namespace OrderWeb.SharedUI.ViewModels;

public sealed class LoginViewModel : INotifyPropertyChanged
{
    private readonly IAuthenticationService _authentication;
    private string _pin = string.Empty;
    private string _restaurantName = "Restaurant Name";
    private string _statusMessage = string.Empty;
    private bool _hasError;
    private bool _isBusy;
    private string _dateText = string.Empty;
    private string _timeText = string.Empty;

    public LoginViewModel(IAuthenticationService authentication)
    {
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        KeyCommand = new Command<string>(OnKey, _ => !IsBusy);
        ClearCommand = new Command(() => { Pin = string.Empty; ClearStatus(); }, () => !IsBusy);
        BackspaceCommand = new Command(() =>
        {
            if (Pin.Length > 0) Pin = Pin[..^1];
            ClearStatus();
        }, () => !IsBusy);
        ClockInOutCommand = new Command(() => ClockInOutRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy);
        MinimizeCommand = new Command(() => MinimizeRequested?.Invoke(this, EventArgs.Empty));
        UpdateClock();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<UserSession>? LoginSucceeded;
    public event EventHandler? ClockInOutRequested;
    public event EventHandler? MinimizeRequested;

    public ICommand KeyCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand BackspaceCommand { get; }
    public ICommand ClockInOutCommand { get; }
    public ICommand MinimizeCommand { get; }

    public string Pin
    {
        get => _pin;
        private set
        {
            if (_pin == value) return;
            _pin = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PinLength));
        }
    }

    public int PinLength => Pin.Length;

    public string RestaurantName
    {
        get => _restaurantName;
        set { if (_restaurantName == value) return; _restaurantName = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set { if (_statusMessage == value) return; _statusMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasStatus)); }
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool HasError
    {
        get => _hasError;
        private set { if (_hasError == value) return; _hasError = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            ((Command)KeyCommand).ChangeCanExecute();
            ((Command)ClearCommand).ChangeCanExecute();
            ((Command)BackspaceCommand).ChangeCanExecute();
            ((Command)ClockInOutCommand).ChangeCanExecute();
        }
    }

    public string DateText
    {
        get => _dateText;
        private set { if (_dateText == value) return; _dateText = value; OnPropertyChanged(); }
    }

    public string TimeText
    {
        get => _timeText;
        private set { if (_timeText == value) return; _timeText = value; OnPropertyChanged(); }
    }

    public void UpdateClock()
    {
        var now = DateTime.Now;
        DateText = now.ToString("dddd, MMM d, yyyy");
        TimeText = now.ToString("h:mm tt").ToLowerInvariant();
    }

    public void SetRestaurantName(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            RestaurantName = name.Trim();
        }
    }

    private async void OnKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || IsBusy) return;
        ClearStatus();
        if (key.Length == 1 && char.IsDigit(key[0]) && Pin.Length < 4)
        {
            Pin += key;
            if (Pin.Length == 4)
            {
                await LoginAsync();
            }
        }
    }

    private async Task LoginAsync()
    {
        IsBusy = true;
        StatusMessage = "Logging in...";
        HasError = false;
        try
        {
            var result = await _authentication.LoginAsync(new AuthenticationRequest("PIN", Pin));
            if (result.IsSuccess && result.Value is not null)
            {
                Pin = string.Empty;
                ClearStatus();
                LoginSucceeded?.Invoke(this, result.Value);
                return;
            }

            Pin = string.Empty;
            HasError = true;
            StatusMessage = result.Error?.Message ?? "Wrong PIN. Try again.";
        }
        catch (Exception ex)
        {
            Pin = string.Empty;
            HasError = true;
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
        HasError = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
