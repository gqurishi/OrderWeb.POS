using OrderWeb.Client.Models;
using OrderWeb.Client.Services;
using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.Client;

public sealed class ClockTimeModal : ContentPage
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SuccessCloseDelay = TimeSpan.FromMilliseconds(900);

    private readonly MotherAuthClient _authClient = new();
    private readonly ClientTimeClockService _timeClockService = new();
    private readonly Grid _panel = new();
    private readonly Label _subtitleLabel;
    private readonly Label _idleCountdownLabel;
    private readonly Label _pinErrorLabel;
    private readonly Border _pinErrorFrame;
    private readonly Border[] _pinDots;
    private readonly ActivityIndicator _pinLoadingIndicator;
    private readonly Label _staffNameLabel;
    private readonly Label _staffStatusLabel;
    private readonly Label _clockInTimeLabel;
    private readonly Label _staffHoursLabel;
    private readonly Border _shiftInfoCard;
    private readonly VerticalStackLayout _clockInTimePanel;
    private readonly Button _clockInButton;
    private readonly Button _clockOutButton;
    private readonly Border _dashboardMessageFrame;
    private readonly Label _dashboardMessageLabel;
    private readonly IDispatcherTimer _idleTimer;

    private string _pin = string.Empty;
    private LoginSession? _authenticatedUser;
    private bool _isBusy;
    private DateTime _lastActivityUtc = DateTime.UtcNow;

    public ClockTimeModal()
    {
        BackgroundColor = Color.FromArgb("#EEF2F7");

        _subtitleLabel = new Label { FontSize = 18, TextColor = Color.FromArgb("#64748B") };
        _idleCountdownLabel = new Label { Text = "Closing in 30s", FontSize = 16, TextColor = Color.FromArgb("#94A3B8"), VerticalTextAlignment = TextAlignment.Center };
        _pinDots = Enumerable.Range(0, 4).Select(_ => PinDot()).ToArray();
        _pinErrorLabel = new Label { FontSize = 15, TextColor = Color.FromArgb("#B91C1C"), HorizontalTextAlignment = TextAlignment.Center };
        _pinErrorFrame = new Border
        {
            IsVisible = false,
            Padding = new Thickness(12, 8),
            Stroke = Color.FromArgb("#EF4444"),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Color.FromArgb("#FEF2F2"),
            Content = _pinErrorLabel
        };
        _pinLoadingIndicator = new ActivityIndicator { IsVisible = false, IsRunning = true, Color = Color.FromArgb("#6366F1"), HeightRequest = 26 };

        _staffNameLabel = new Label { FontSize = 30, TextColor = Color.FromArgb("#020617") };
        _staffStatusLabel = new Label { FontSize = 18, TextColor = Color.FromArgb("#64748B") };
        _clockInTimeLabel = new Label { FontSize = 38, TextColor = Color.FromArgb("#16A34A") };
        _staffHoursLabel = new Label { FontSize = 30, TextColor = Color.FromArgb("#4338CA") };
        _clockInTimePanel = new VerticalStackLayout
        {
            IsVisible = false,
            Spacing = 4,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = "Clocked in at", FontSize = 16, TextColor = Color.FromArgb("#64748B"), HorizontalTextAlignment = TextAlignment.Center },
                _clockInTimeLabel
            }
        };
        _shiftInfoCard = new Border
        {
            Padding = new Thickness(34, 30),
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 0 }
        };
        _clockInButton = ActionButton("Clock In", "#6366F1");
        _clockOutButton = ActionButton("Clock Out", "#DC2626");
        _clockInButton.Clicked += async (_, _) => await ExecuteClockActionAsync(clockOut: false);
        _clockOutButton.Clicked += async (_, _) => await ExecuteClockActionAsync(clockOut: true);

        _dashboardMessageLabel = new Label { FontSize = 15, HorizontalTextAlignment = TextAlignment.Center };
        _dashboardMessageFrame = new Border
        {
            IsVisible = false,
            Padding = new Thickness(12, 8),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = _dashboardMessageLabel
        };

        Content = new Grid
        {
            Padding = 36,
            Children =
            {
                new Border
                {
                    Stroke = Color.FromArgb("#CBD5E1"),
                    StrokeThickness = 2,
                    StrokeShape = new RoundRectangle { CornerRadius = 0 },
                    BackgroundColor = Colors.White,
                    WidthRequest = 1120,
                    HeightRequest = 640,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Padding = 36,
                    Content = BuildModalContent()
                }
            }
        };

        _idleTimer = Dispatcher.CreateTimer();
        _idleTimer.Interval = TimeSpan.FromSeconds(1);
        _idleTimer.Tick += async (_, _) => await OnIdleTimerTickAsync();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ResetPinEntry();
        ShowPinStep();
        RegisterActivity();
        _idleTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _idleTimer.Stop();
    }

    private View BuildModalContent()
    {
        var closeButton = new Button
        {
            Text = "X",
            TextColor = Color.FromArgb("#DC2626"),
            BackgroundColor = Color.FromArgb("#FFF1F2"),
            BorderColor = Color.FromArgb("#FCA5A5"),
            BorderWidth = 1,
            FontSize = 22,
            WidthRequest = 58,
            HeightRequest = 58,
            CornerRadius = 6
        };
        closeButton.Clicked += async (_, _) => await CloseModalAsync();

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        new Label { Text = "Staff Clock", FontSize = 30, TextColor = Color.FromArgb("#020617") },
                        _subtitleLabel
                    }
                },
                _idleCountdownLabel,
                closeButton
            }
        };
        SetColumn(_idleCountdownLabel, 1);
        SetColumn(closeButton, 2);

        var shell = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(74),
                new RowDefinition(GridLength.Star)
            },
            Children =
            {
                header,
                _panel
            }
        };
        SetRow(_panel, 1);
        return shell;
    }

    private void ShowPinStep()
    {
        _panel.Children.Clear();
        _subtitleLabel.Text = "Enter your PIN";
        _idleCountdownLabel.IsVisible = true;

        var pinPanel = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(480)
            },
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 24,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = "Enter 4-digit PIN", FontSize = 22, TextColor = Color.FromArgb("#334155"), HorizontalTextAlignment = TextAlignment.Center },
                        new HorizontalStackLayout
                        {
                            Spacing = 26,
                            HorizontalOptions = LayoutOptions.Center,
                            Children = { _pinDots[0], _pinDots[1], _pinDots[2], _pinDots[3] }
                        },
                        _pinLoadingIndicator,
                        _pinErrorFrame
                    }
                },
                BuildSquareKeypad()
            }
        };
        SetColumn(pinPanel.Children[1], 1);
        _panel.Children.Add(pinPanel);
    }

    private async Task ShowDashboardStepAsync()
    {
        if (_authenticatedUser == null)
        {
            return;
        }

        var state = await _timeClockService.GetDashboardStateAsync(_authenticatedUser);
        var isClockedIn = state.IsClockedIn;
        _subtitleLabel.Text = isClockedIn
            ? "You are on shift - clock out when finished"
            : "Start your shift for today";

        _staffNameLabel.Text = state.User.Role;
        _staffStatusLabel.Text = "Ready to clock in";
        _staffStatusLabel.IsVisible = !isClockedIn;
        _staffHoursLabel.Text = state.TodayHoursDisplay;
        _clockInTimePanel.IsVisible = isClockedIn;

        if (isClockedIn)
        {
            _clockInTimeLabel.Text = state.OpenSession!.ClockInAt.ToString("h:mm");
            _shiftInfoCard.BackgroundColor = Color.FromArgb("#F0FDF4");
            _shiftInfoCard.Stroke = Color.FromArgb("#86EFAC");
        }
        else
        {
            _shiftInfoCard.BackgroundColor = Color.FromArgb("#EEF2FF");
            _shiftInfoCard.Stroke = Color.FromArgb("#C7D2FE");
        }

        _clockInButton.IsVisible = !isClockedIn;
        _clockOutButton.IsVisible = isClockedIn;

        _panel.Children.Clear();
        _panel.Children.Add(BuildDashboardPanel());
    }

    private View BuildDashboardPanel()
    {
        _shiftInfoCard.Content = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(150)
            },
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 44,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        _staffNameLabel,
                        _staffStatusLabel
                    }
                },
                _clockInTimePanel,
                new VerticalStackLayout
                {
                    Spacing = 4,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = "Today", FontSize = 16, TextColor = Color.FromArgb("#64748B"), HorizontalTextAlignment = TextAlignment.Center },
                        _staffHoursLabel
                    }
                }
            }
        };
        SetColumn(((Grid)_shiftInfoCard.Content).Children[1], 1);
        SetColumn(((Grid)_shiftInfoCard.Content).Children[2], 2);

        var doneButton = ActionButton("Done", "#F8FAFC", Color.FromArgb("#334155"));
        doneButton.Clicked += async (_, _) => await CloseModalAsync();

        var actionStack = new VerticalStackLayout
        {
            Spacing = 16,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                _clockInButton,
                _clockOutButton,
                doneButton,
                _dashboardMessageFrame
            }
        };

        var dashboard = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(300)
            },
            ColumnSpacing = 28,
            Padding = new Thickness(0, 20, 0, 0),
            Children =
            {
                _shiftInfoCard,
                actionStack
            }
        };
        SetColumn(actionStack, 1);
        return dashboard;
    }

    private View BuildSquareKeypad()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(112),
                new ColumnDefinition(112),
                new ColumnDefinition(112)
            },
            RowDefinitions =
            {
                new RowDefinition(112),
                new RowDefinition(112),
                new RowDefinition(112),
                new RowDefinition(112)
            },
            ColumnSpacing = 20,
            RowSpacing = 20,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        var values = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "Clear", "0", "X" };
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var key = SquareKey(value);
            grid.Children.Add(key);
            SetRow(key, i / 3);
            SetColumn(key, i % 3);
        }

        return grid;
    }

    private Button SquareKey(string text)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = text == "X" ? Color.FromArgb("#FFF1F2") : Colors.White,
            BorderColor = Color.FromArgb("#222222"),
            BorderWidth = 2,
            CornerRadius = 0,
            TextColor = text == "X" ? Color.FromArgb("#DC2626") : Color.FromArgb("#111827"),
            FontSize = text.Length == 1 ? 34 : 16,
            WidthRequest = 112,
            HeightRequest = 112
        };
        button.Clicked += (_, _) => OnPinKey(text);
        return button;
    }

    private static Border PinDot() =>
        new()
        {
            WidthRequest = 24,
            HeightRequest = 24,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            BackgroundColor = Color.FromArgb("#E5E7EB")
        };

    private static Button ActionButton(string text, string color) =>
        ActionButton(text, color, Colors.White);

    private static Button ActionButton(string text, string color, Color textColor) =>
        new()
        {
            Text = text,
            BackgroundColor = Color.FromArgb(color),
            TextColor = textColor,
            FontSize = 20,
            HeightRequest = 72,
            CornerRadius = 4
        };

    private void OnPinKey(string value)
    {
        if (_isBusy)
        {
            return;
        }

        RegisterActivity();
        if (value == "Clear")
        {
            ResetPinEntry();
        }
        else if (value == "X")
        {
            _pin = _pin.Length > 0 ? _pin[..^1] : string.Empty;
            UpdatePinDots();
        }
        else if (_pin.Length < 4)
        {
            _pin += value;
            UpdatePinDots();
        }
    }

    private void ResetPinEntry()
    {
        _pin = string.Empty;
        _pinErrorFrame.IsVisible = false;
        UpdatePinDots();
    }

    private void UpdatePinDots()
    {
        for (var i = 0; i < _pinDots.Length; i++)
        {
            _pinDots[i].BackgroundColor = i < _pin.Length
                ? Color.FromArgb("#6366F1")
                : Color.FromArgb("#E5E7EB");
        }

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
        _pinLoadingIndicator.IsVisible = true;
        _pinErrorFrame.IsVisible = false;

        try
        {
            var result = await _authClient.ValidatePinAsync(_pin);
            if (!result.Success || result.User == null)
            {
                _pin = string.Empty;
                UpdatePinDots();
                ShowPinError(string.IsNullOrWhiteSpace(result.Message) ? "Wrong PIN" : result.Message);
                return;
            }

            _authenticatedUser = result.User;
            _dashboardMessageFrame.IsVisible = false;
            await ShowDashboardStepAsync();
        }
        finally
        {
            _pinLoadingIndicator.IsVisible = false;
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
        _clockInButton.IsEnabled = false;
        _clockOutButton.IsEnabled = false;
        _idleTimer.Stop();

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
            await ShowDashboardStepAsync();
            _idleTimer.Start();
        }
        catch (Exception ex)
        {
            ShowDashboardMessage(ex.Message, success: false);
            await ShowDashboardStepAsync();
            _idleTimer.Start();
        }
        finally
        {
            _isBusy = false;
            _clockInButton.IsEnabled = true;
            _clockOutButton.IsEnabled = true;
        }
    }

    private void ShowPinError(string message)
    {
        _pinErrorLabel.Text = message;
        _pinErrorFrame.IsVisible = true;
        foreach (var dot in _pinDots)
        {
            dot.BackgroundColor = Color.FromArgb("#EF4444");
        }
    }

    private void ShowDashboardMessage(string message, bool success)
    {
        _dashboardMessageLabel.Text = message;
        _dashboardMessageFrame.IsVisible = true;
        _dashboardMessageFrame.BackgroundColor = success ? Color.FromArgb("#ECFDF5") : Color.FromArgb("#FEF2F2");
        _dashboardMessageFrame.Stroke = success ? Color.FromArgb("#10B981") : Color.FromArgb("#EF4444");
        _dashboardMessageLabel.TextColor = success ? Color.FromArgb("#047857") : Color.FromArgb("#B91C1C");
    }

    private void RegisterActivity()
    {
        _lastActivityUtc = DateTime.UtcNow;
    }

    private async Task OnIdleTimerTickAsync()
    {
        var remaining = IdleTimeout - (DateTime.UtcNow - _lastActivityUtc);
        if (remaining <= TimeSpan.Zero)
        {
            await CloseModalAsync();
            return;
        }

        _idleCountdownLabel.Text = $"Closing in {Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))}s";
    }

    private async Task CloseModalAsync()
    {
        if (Navigation.ModalStack.Count > 0)
        {
            await Navigation.PopModalAsync(false);
        }
    }

    private static void SetColumn(IView view, int column)
    {
        if (view is BindableObject bindable)
        {
            bindable.SetValue(Grid.ColumnProperty, column);
        }
    }

    private static void SetRow(IView view, int row)
    {
        if (view is BindableObject bindable)
        {
            bindable.SetValue(Grid.RowProperty, row);
        }
    }
}
