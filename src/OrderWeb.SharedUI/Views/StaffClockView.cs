using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>Shift shown after a PIN is accepted. Hosts supply the text; SharedUI only draws it.</summary>
public sealed record StaffClockShift(
    string StaffName,
    bool IsClockedIn,
    string? ClockInTimeDisplay,
    string TodayHoursDisplay);

/// <summary>
/// Mother Staff Clock dialog. PIN pad, shift card, and Clock In / Clock Out.
/// Hosts own PIN check and clock actions.
/// </summary>
public sealed class StaffClockView : ContentView
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    private readonly Label _subtitle;
    private readonly Label _countdown;
    private readonly Grid _pinPanel;
    private readonly Grid _shiftPanel;
    private readonly Border _errorFrame;
    private readonly Label _errorLabel;
    private readonly ChefLoaderView _checking;
    private readonly Border[] _dots;
    private readonly Border _shiftCard;
    private readonly Label _staffName;
    private readonly Label _staffStatus;
    private readonly VerticalStackLayout _clockedInPanel;
    private readonly Label _clockInTime;
    private readonly Label _todayHours;
    private readonly Border _messageFrame;
    private readonly Label _messageLabel;
    private readonly Button _clockInButton;
    private readonly Button _clockOutButton;

    private readonly IDispatcherTimer _idleTimer;
    private DateTime _lastActivityUtc = DateTime.UtcNow;
    private string _pin = string.Empty;
    private bool _busy;

    public StaffClockView()
    {
        _subtitle = Label("Enter your PIN", 15, false, "#64748B");
        _countdown = Label("Closing in 30s", 13, false, "#94A3B8");
        _countdown.VerticalOptions = LayoutOptions.Center;

        _errorLabel = Label("Wrong PIN", 20, true, "#DC2626");
        _errorLabel.HorizontalTextAlignment = TextAlignment.Center;
        _errorFrame = new Border
        {
            IsVisible = false,
            BackgroundColor = Color.FromArgb("#FEE2E2"),
            Stroke = Color.FromArgb("#EF4444"),
            StrokeThickness = 2,
            Padding = new Thickness(16, 12),
            StrokeShape = new Rectangle(),
            Content = _errorLabel
        };

        _checking = new ChefLoaderView
        {
            Mode = ChefLoaderMode.Inline,
            Size = ChefLoaderSize.Sm,
            Message = "Checking PIN",
            DelayMilliseconds = 0,
            IsLoading = false,
            HorizontalOptions = LayoutOptions.Center
        };

        _dots = [Dot(), Dot(), Dot(), Dot()];
        _pinPanel = BuildPinPanel();
        _shiftCard = new Border
        {
            StrokeThickness = 2,
            StrokeShape = new Rectangle(),
            Padding = new Thickness(24, 20),
            VerticalOptions = LayoutOptions.Fill
        };
        _staffName = Label("Staff", 26, true, "#0F172A");
        _staffName.LineBreakMode = LineBreakMode.WordWrap;
        _staffName.MaxLines = 2;
        _staffStatus = Label("Ready to clock in", 18, false, "#64748B");
        _staffStatus.VerticalOptions = LayoutOptions.Center;
        _clockInTime = Label("1:21 PM", 38, true, "#16A34A");
        _clockInTime.HorizontalTextAlignment = TextAlignment.Center;
        _clockedInPanel = new VerticalStackLayout
        {
            IsVisible = false,
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = 150,
            Children =
            {
                CenteredCaption("Clocked in at"),
                _clockInTime
            }
        };
        _todayHours = Label("0h 0m", 28, true, "#4338CA");
        _todayHours.HorizontalTextAlignment = TextAlignment.Center;
        _messageLabel = Label(string.Empty, 13, true, "#047857");
        _messageLabel.HorizontalTextAlignment = TextAlignment.Center;
        _messageLabel.LineBreakMode = LineBreakMode.WordWrap;
        _messageFrame = new Border
        {
            IsVisible = false,
            BackgroundColor = Color.FromArgb("#ECFDF5"),
            Stroke = Color.FromArgb("#10B981"),
            StrokeThickness = 1,
            Padding = new Thickness(10, 8),
            StrokeShape = new Rectangle(),
            Content = _messageLabel
        };
        _clockInButton = ActionButton("Clock In", "#6366F1", Colors.White, 56, 18);
        _clockOutButton = ActionButton("Clock Out", "#DC2626", Colors.White, 56, 18);
        _clockInButton.Clicked += (_, _) => RaiseIfIdle(ClockInRequested);
        _clockOutButton.Clicked += (_, _) => RaiseIfIdle(ClockOutRequested);
        var done = ActionButton("Done", "#F8FAFC", Color.FromArgb("#475569"), 48, 16);
        done.Clicked += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        _shiftPanel = BuildShiftPanel(done);

        var close = new Button
        {
            Text = "X",
            BackgroundColor = Color.FromArgb("#FEF2F2"),
            TextColor = Color.FromArgb("#DC2626"),
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            WidthRequest = 44,
            HeightRequest = 44,
            CornerRadius = 4,
            BorderColor = Color.FromArgb("#FCA5A5"),
            BorderWidth = 1,
            Padding = 0
        };
        close.Clicked += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 4,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        Label("Staff Clock", 24, true, "#0F172A"),
                        _subtitle
                    }
                },
                _countdown,
                close
            }
        };
        Grid.SetColumn(_countdown, 1);
        Grid.SetColumn(close, 2);

        var card = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#CBD5E1"),
            StrokeThickness = 2,
            StrokeShape = new Rectangle(),
            Padding = new Thickness(28, 22),
            MaximumWidthRequest = 820,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            Shadow = new Shadow { Brush = Colors.Black, Radius = 8, Opacity = 0.10f, Offset = new Point(0, 4) },
            Content = new VerticalStackLayout
            {
                Spacing = 18,
                Children = { header, _pinPanel, _shiftPanel }
            }
        };

        BackgroundColor = Color.FromArgb("#F1F5F9");
        Content = new Grid
        {
            Padding = 24,
            Children = { card }
        };

        _idleTimer = Dispatcher.CreateTimer();
        _idleTimer.Interval = TimeSpan.FromSeconds(1);
        _idleTimer.Tick += (_, _) => OnIdleTick();
        ShowPinStep();
    }

    public event EventHandler<string>? PinSubmitted;
    public event EventHandler? ClockInRequested;
    public event EventHandler? ClockOutRequested;
    public event EventHandler? CloseRequested;

    public void Begin()
    {
        ShowPinStep();
        _lastActivityUtc = DateTime.UtcNow;
        if (!_idleTimer.IsRunning)
        {
            _idleTimer.Start();
        }
    }

    public void End()
    {
        if (_idleTimer.IsRunning)
        {
            _idleTimer.Stop();
        }
    }

    public void ShowPinError(string message)
    {
        _pin = string.Empty;
        PaintDots(error: true);
        _errorLabel.Text = string.IsNullOrWhiteSpace(message) ? "Wrong PIN" : message;
        _errorFrame.IsVisible = true;
        SetChecking(false);
        Touch();
    }

    public void SetChecking(bool checking)
    {
        _busy = checking;
        _checking.IsLoading = checking;
        if (checking)
        {
            _errorFrame.IsVisible = false;
        }
    }

    public void ShowShift(StaffClockShift shift)
    {
        _busy = false;
        _checking.IsLoading = false;
        _pinPanel.IsVisible = false;
        _shiftPanel.IsVisible = true;
        _messageFrame.IsVisible = false;
        _staffName.Text = string.IsNullOrWhiteSpace(shift.StaffName) ? "Staff" : shift.StaffName;
        _todayHours.Text = string.IsNullOrWhiteSpace(shift.TodayHoursDisplay) ? "0h 0m" : shift.TodayHoursDisplay;
        _clockedInPanel.IsVisible = shift.IsClockedIn;
        _staffStatus.IsVisible = !shift.IsClockedIn;
        _clockInButton.IsVisible = !shift.IsClockedIn;
        _clockOutButton.IsVisible = shift.IsClockedIn;
        _clockInButton.IsEnabled = !shift.IsClockedIn;
        _clockOutButton.IsEnabled = shift.IsClockedIn;

        if (shift.IsClockedIn)
        {
            _clockInTime.Text = shift.ClockInTimeDisplay ?? string.Empty;
            _shiftCard.BackgroundColor = Color.FromArgb("#F0FDF4");
            _shiftCard.Stroke = Color.FromArgb("#86EFAC");
            _subtitle.Text = "You are on shift — clock out when finished";
        }
        else
        {
            _staffStatus.Text = "Ready to clock in";
            _shiftCard.BackgroundColor = Color.FromArgb("#EEF2FF");
            _shiftCard.Stroke = Color.FromArgb("#C7D2FE");
            _subtitle.Text = "Start your shift for today";
        }

        Touch();
        if (!_idleTimer.IsRunning)
        {
            _idleTimer.Start();
        }
    }

    public void ShowMessage(string message, bool success)
    {
        _messageLabel.Text = message;
        _messageFrame.IsVisible = !string.IsNullOrWhiteSpace(message);
        _messageFrame.BackgroundColor = success ? Color.FromArgb("#ECFDF5") : Color.FromArgb("#FEF2F2");
        _messageFrame.Stroke = success ? Color.FromArgb("#10B981") : Color.FromArgb("#EF4444");
        _messageLabel.TextColor = success ? Color.FromArgb("#047857") : Color.FromArgb("#B91C1C");
        Touch();
    }

    public void SetActionsEnabled(bool enabled)
    {
        _busy = !enabled;
        _clockInButton.IsEnabled = enabled && _clockInButton.IsVisible;
        _clockOutButton.IsEnabled = enabled && _clockOutButton.IsVisible;
        if (!enabled)
        {
            _idleTimer.Stop();
        }
        else if (!_idleTimer.IsRunning)
        {
            Touch();
            _idleTimer.Start();
        }
    }

    private void ShowPinStep()
    {
        _pin = string.Empty;
        _busy = false;
        _checking.IsLoading = false;
        _errorFrame.IsVisible = false;
        _pinPanel.IsVisible = true;
        _shiftPanel.IsVisible = false;
        _subtitle.Text = "Enter your PIN";
        _countdown.IsVisible = true;
        PaintDots(error: false);
    }

    private Grid BuildPinPanel()
    {
        var keys = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(88), new ColumnDefinition(88), new ColumnDefinition(88) },
            RowDefinitions = { new RowDefinition(88), new RowDefinition(88), new RowDefinition(88), new RowDefinition(88) },
            ColumnSpacing = 12,
            RowSpacing = 12,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        AddDigit(keys, "1", 0, 0);
        AddDigit(keys, "2", 0, 1);
        AddDigit(keys, "3", 0, 2);
        AddDigit(keys, "4", 1, 0);
        AddDigit(keys, "5", 1, 1);
        AddDigit(keys, "6", 1, 2);
        AddDigit(keys, "7", 2, 0);
        AddDigit(keys, "8", 2, 1);
        AddDigit(keys, "9", 2, 2);
        keys.Add(Key("Clear", 16, false, (_, _) =>
        {
            if (_busy) return;
            _pin = string.Empty;
            _errorFrame.IsVisible = false;
            PaintDots(error: false);
            Touch();
        }), 0, 3);
        AddDigit(keys, "0", 3, 1);
        keys.Add(Key("X", 28, true, (_, _) =>
        {
            if (_busy || _pin.Length == 0) return;
            _pin = _pin[..^1];
            _errorFrame.IsVisible = false;
            PaintDots(error: false);
            Touch();
        }), 2, 3);

        var panel = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(260), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 28,
            MinimumHeightRequest = 360,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 18,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        Centered("Enter 4-digit PIN", 18, true, "#334155"),
                        new HorizontalStackLayout
                        {
                            Spacing = 20,
                            HorizontalOptions = LayoutOptions.Center,
                            Children = { _dots[0], _dots[1], _dots[2], _dots[3] }
                        },
                        _errorFrame,
                        _checking
                    }
                },
                keys
            }
        };
        Grid.SetColumn(keys, 1);
        return panel;
    }

    private Grid BuildShiftPanel(Button done)
    {
        _shiftCard.Content = new VerticalStackLayout
        {
            Spacing = 14,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                _staffName,
                new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto),
                        new ColumnDefinition(GridLength.Auto)
                    },
                    ColumnSpacing = 20,
                    Children =
                    {
                        _staffStatus,
                        _clockedInPanel,
                        new VerticalStackLayout
                        {
                            WidthRequest = 100,
                            VerticalOptions = LayoutOptions.Center,
                            Children =
                            {
                                CenteredCaption("Today"),
                                _todayHours
                            }
                        }
                    }
                }
            }
        };
        var todayColumn = (VerticalStackLayout)((Grid)((VerticalStackLayout)_shiftCard.Content).Children[1]).Children[2];
        Grid.SetColumn(_clockedInPanel, 1);
        Grid.SetColumn(todayColumn, 2);

        var actions = new VerticalStackLayout
        {
            Spacing = 12,
            VerticalOptions = LayoutOptions.Center,
            Children = { _messageFrame, _clockInButton, _clockOutButton, done }
        };
        var panel = new Grid
        {
            IsVisible = false,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(220) },
            ColumnSpacing = 20,
            MinimumHeightRequest = 220,
            Children = { _shiftCard, actions }
        };
        Grid.SetColumn(actions, 1);
        return panel;
    }

    private void AddDigit(Grid grid, string digit, int row, int column)
    {
        grid.Add(Key(digit, 32, false, (_, _) => OnDigit(digit)), column, row);
    }

    private void OnDigit(string digit)
    {
        if (_busy || !_pinPanel.IsVisible || _pin.Length >= 4)
        {
            return;
        }

        _errorFrame.IsVisible = false;
        _pin += digit;
        PaintDots(error: false);
        Touch();
        if (_pin.Length == 4)
        {
            PinSubmitted?.Invoke(this, _pin);
        }
    }

    private void PaintDots(bool error)
    {
        var empty = error ? Color.FromArgb("#EF4444") : Color.FromArgb("#E5E7EB");
        var filled = Color.FromArgb("#6366F1");
        for (var i = 0; i < _dots.Length; i++)
        {
            _dots[i].BackgroundColor = i < _pin.Length ? filled : empty;
        }
    }

    private void OnIdleTick()
    {
        var remaining = IdleTimeout - (DateTime.UtcNow - _lastActivityUtc);
        if (remaining <= TimeSpan.Zero)
        {
            _idleTimer.Stop();
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _countdown.Text = $"Closing in {Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))}s";
    }

    private void RaiseIfIdle(EventHandler? handler)
    {
        if (_busy)
        {
            return;
        }

        Touch();
        handler?.Invoke(this, EventArgs.Empty);
    }

    private void Touch() => _lastActivityUtc = DateTime.UtcNow;

    private static Border Key(string text, int fontSize, bool danger, EventHandler clicked)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = danger ? Color.FromArgb("#FEF2F2") : Colors.Transparent,
            TextColor = danger ? Color.FromArgb("#DC2626") : Color.FromArgb("#1A1A1A"),
            FontSize = fontSize,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 0,
            Padding = 0
        };
        button.Clicked += clicked;
        return new Border
        {
            BackgroundColor = danger ? Color.FromArgb("#FEF2F2") : Colors.White,
            Stroke = Color.FromArgb("#2C2C2C"),
            StrokeThickness = 2,
            StrokeShape = new Rectangle(),
            Padding = 0,
            Content = button
        };
    }

    private static Border Dot() =>
        new()
        {
            WidthRequest = 18,
            HeightRequest = 18,
            StrokeThickness = 0,
            Padding = 0,
            BackgroundColor = Color.FromArgb("#E5E7EB"),
            StrokeShape = new RoundRectangle { CornerRadius = 2 }
        };

    private static Button ActionButton(string text, string background, Color textColor, int height, int fontSize) =>
        new()
        {
            Text = text,
            BackgroundColor = Color.FromArgb(background),
            TextColor = textColor,
            FontSize = fontSize,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = height,
            CornerRadius = 4
        };

    private static Label Label(string text, int size, bool bold, string color) =>
        new()
        {
            Text = text,
            FontSize = size,
            FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
            TextColor = Color.FromArgb(color)
        };

    private static Label Centered(string text, int size, bool bold, string color)
    {
        var label = Label(text, size, bold, color);
        label.HorizontalTextAlignment = TextAlignment.Center;
        return label;
    }

    private static Label CenteredCaption(string text)
    {
        var label = Label(text, 14, false, "#64748B");
        label.HorizontalTextAlignment = TextAlignment.Center;
        return label;
    }
}
