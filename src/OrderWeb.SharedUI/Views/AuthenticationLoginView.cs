using OrderWeb.SharedUI.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Shared Mother-style authentication presentation: brand panel, PIN dots,
/// number keypad, clock action, loading, and error states.
/// Hosts keep authentication services and handle PinCompleted.
/// </summary>
public sealed class AuthenticationLoginView : ContentView
{
    private readonly Label _brandTitle = new()
    {
        Text = "POS",
        FontSize = 58,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.Center
    };

    private readonly Label _welcomeTitle = new()
    {
        Text = "Welcome Back!",
        FontSize = 32,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.Center
    };

    private readonly Label _restaurantName = new()
    {
        Text = "Restaurant POS",
        FontSize = 24,
        FontAttributes = FontAttributes.Bold,
        Opacity = 0.72,
        HorizontalTextAlignment = TextAlignment.Center,
        MaximumWidthRequest = 300
    };

    private readonly Label _prompt = new()
    {
        Text = "Enter your PIN or swipe employee card",
        FontSize = 18,
        HorizontalTextAlignment = TextAlignment.Center
    };

    private readonly Label _banner = new()
    {
        FontSize = 14,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.Center
    };

    private readonly Border _bannerFrame = new()
    {
        IsVisible = false,
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 10 },
        Padding = new Thickness(12, 10),
        HorizontalOptions = LayoutOptions.Center
    };

    private readonly Label _error = new()
    {
        IsVisible = false,
        FontSize = 16,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.Center
    };

    private readonly Border _errorFrame = new()
    {
        IsVisible = false,
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 12 },
        Padding = new Thickness(16, 10),
        HorizontalOptions = LayoutOptions.Center
    };

    private readonly Label _status = new()
    {
        IsVisible = false,
        FontSize = 16,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.Center
    };

    private readonly Label _dateLabel = new()
    {
        FontSize = 13,
        HorizontalTextAlignment = TextAlignment.End
    };

    private readonly Label _timeLabel = new()
    {
        FontSize = 18,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.End
    };

    private readonly PinDotsView _pinDots = new();
    private readonly NumberKeypad _keypad = new() { KeySize = 100 };
    private readonly SharedButton _clockAction = new()
    {
        Text = "Clock In/Out",
        HeightRequest = 58,
        WidthRequest = 334
    };
    private readonly AuthenticationBusyOverlay _busy = new();
    private readonly Button _minimize = new()
    {
        Text = "-",
        WidthRequest = 44,
        HeightRequest = 44,
        Padding = 0,
        CornerRadius = 0,
        BackgroundColor = Colors.Transparent,
        BorderColor = Colors.Transparent,
        BorderWidth = 0,
        FontSize = 24,
        FontAttributes = FontAttributes.Bold,
        HorizontalOptions = LayoutOptions.End,
        VerticalOptions = LayoutOptions.Start,
        Margin = new Thickness(0, 14, 18, 0),
        ZIndex = 10
    };

    private string _pin = string.Empty;

    public static readonly BindableProperty BrandTitleProperty = BindableProperty.Create(
        nameof(BrandTitle), typeof(string), typeof(AuthenticationLoginView), "POS",
        propertyChanged: (b, _, v) => ((AuthenticationLoginView)b)._brandTitle.Text = v?.ToString() ?? "POS");

    public static readonly BindableProperty WelcomeTitleProperty = BindableProperty.Create(
        nameof(WelcomeTitle), typeof(string), typeof(AuthenticationLoginView), "Welcome Back!",
        propertyChanged: (b, _, v) => ((AuthenticationLoginView)b)._welcomeTitle.Text = v?.ToString() ?? "Welcome Back!");

    public static readonly BindableProperty RestaurantNameProperty = BindableProperty.Create(
        nameof(RestaurantName), typeof(string), typeof(AuthenticationLoginView), "Restaurant POS",
        propertyChanged: (b, _, v) => ((AuthenticationLoginView)b)._restaurantName.Text =
            string.IsNullOrWhiteSpace(v?.ToString()) ? "Restaurant POS" : v!.ToString()!);

    public static readonly BindableProperty PromptProperty = BindableProperty.Create(
        nameof(Prompt), typeof(string), typeof(AuthenticationLoginView), "Enter your PIN or swipe employee card",
        propertyChanged: (b, _, v) => ((AuthenticationLoginView)b)._prompt.Text = v?.ToString() ?? string.Empty);

    public static readonly BindableProperty ClockActionTextProperty = BindableProperty.Create(
        nameof(ClockActionText), typeof(string), typeof(AuthenticationLoginView), "Clock In/Out",
        propertyChanged: (b, _, v) => ((AuthenticationLoginView)b)._clockAction.Text = v?.ToString() ?? "Clock In/Out");

    public static readonly BindableProperty ShowClockActionProperty = BindableProperty.Create(
        nameof(ShowClockAction), typeof(bool), typeof(AuthenticationLoginView), true,
        propertyChanged: (b, _, v) => ((AuthenticationLoginView)b)._clockAction.IsVisible = (bool)v);

    public static readonly BindableProperty ShowMinimizeProperty = BindableProperty.Create(
        nameof(ShowMinimize), typeof(bool), typeof(AuthenticationLoginView), true,
        propertyChanged: (b, _, v) => ((AuthenticationLoginView)b)._minimize.IsVisible = (bool)v);

    public static readonly BindableProperty PinLengthProperty = BindableProperty.Create(
        nameof(PinLength), typeof(int), typeof(AuthenticationLoginView), 4,
        propertyChanged: (b, _, v) => ((AuthenticationLoginView)b)._pinDots.Length = Math.Max(1, (int)v));

    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(
        nameof(IsBusy), typeof(bool), typeof(AuthenticationLoginView), false,
        propertyChanged: (b, _, v) => ((AuthenticationLoginView)b).ApplyBusy((bool)v));

    public static readonly BindableProperty BusyMessageProperty = BindableProperty.Create(
        nameof(BusyMessage), typeof(string), typeof(AuthenticationLoginView), "Checking PIN...",
        propertyChanged: (b, _, v) => ((AuthenticationLoginView)b)._busy.Message = v?.ToString() ?? "Checking PIN...");

    public static readonly BindableProperty ErrorMessageProperty = BindableProperty.Create(
        nameof(ErrorMessage), typeof(string), typeof(AuthenticationLoginView), string.Empty,
        propertyChanged: (b, _, v) => ((AuthenticationLoginView)b).ApplyError(v?.ToString()));

    public static readonly BindableProperty StatusMessageProperty = BindableProperty.Create(
        nameof(StatusMessage), typeof(string), typeof(AuthenticationLoginView), string.Empty,
        propertyChanged: (b, _, v) => ((AuthenticationLoginView)b).ApplyStatus(v?.ToString()));

    public static readonly BindableProperty BannerMessageProperty = BindableProperty.Create(
        nameof(BannerMessage), typeof(string), typeof(AuthenticationLoginView), string.Empty,
        propertyChanged: (b, _, v) => ((AuthenticationLoginView)b).ApplyBanner(v?.ToString()));

    public AuthenticationLoginView()
    {
        this.Use(BackgroundColorProperty, "PosSurface");
        _brandTitle.Use(Label.TextColorProperty, "PosPrimary");
        _welcomeTitle.Use(Label.TextColorProperty, "PosTextStrong");
        _restaurantName.Use(Label.TextColorProperty, "PosTextStrong");
        _prompt.Use(Label.TextColorProperty, "PosTextSecondary");
        _error.Use(Label.TextColorProperty, "PosErrorStrong");
        _status.Use(Label.TextColorProperty, "PosPrimary");
        _banner.Use(Label.TextColorProperty, "PosErrorText");
        _dateLabel.Use(Label.TextColorProperty, "PosTextSecondary");
        _timeLabel.Use(Label.TextColorProperty, "PosPrimary");
        _minimize.Use(Button.TextColorProperty, "PosTextStrong");
        _errorFrame.Use(Border.BackgroundColorProperty, "PosErrorSoft");
        _errorFrame.Use(Border.StrokeProperty, "PosError");
        _errorFrame.Content = _error;
        _bannerFrame.Use(Border.BackgroundColorProperty, "PosErrorSoft");
        _bannerFrame.Use(Border.StrokeProperty, "PosError");
        _bannerFrame.Content = _banner;
        _clockAction.CornerRadius = 29;

        _keypad.KeyPressed += (_, e) => AppendPin(e.Key);
        _keypad.ClearPressed += (_, _) => ClearPin();
        _keypad.BackspacePressed += (_, _) => BackspacePin();
        _clockAction.Clicked += (_, _) => ClockActionRequested?.Invoke(this, EventArgs.Empty);
        _minimize.Clicked += (_, _) => MinimizeRequested?.Invoke(this, EventArgs.Empty);

        var left = new Grid { Padding = new Thickness(48, 40) };
        left.Use(Grid.BackgroundColorProperty, "PosSurfaceMuted");
        left.Children.Add(new VerticalStackLayout
        {
            Spacing = 58,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                _brandTitle,
                new VerticalStackLayout
                {
                    Spacing = 10,
                    Margin = new Thickness(0, 20, 0, 0),
                    HorizontalOptions = LayoutOptions.Center,
                    Children = { _welcomeTitle, _restaurantName }
                }
            }
        });

        var rightStack = new VerticalStackLayout
        {
            Spacing = 46,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            MaximumWidthRequest = 430,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 28,
                    HorizontalOptions = LayoutOptions.Center,
                    Children = { _prompt, _bannerFrame, _errorFrame, _status, _pinDots }
                },
                new VerticalStackLayout
                {
                    Spacing = 36,
                    HorizontalOptions = LayoutOptions.Center,
                    Children = { _keypad, _clockAction }
                }
            }
        };

        var right = new Grid { Padding = new Thickness(44, 34) };
        right.Use(Grid.BackgroundColorProperty, "PosSurface");
        right.Children.Add(new ScrollView
        {
            VerticalOptions = LayoutOptions.Center,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = rightStack
        });
        right.Children.Add(new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Spacing = 4,
            Margin = new Thickness(0, 20, 0, 0),
            Children = { _dateLabel, _timeLabel }
        });

        var root = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            }
        };
        root.Add(left);
        root.Add(right, 1);
        root.Add(_minimize);
        Grid.SetColumnSpan(_minimize, 2);
        root.Add(_busy);
        Grid.SetColumnSpan(_busy, 2);

        Content = root;
        UpdateClock();
    }

    public string BrandTitle { get => (string)GetValue(BrandTitleProperty); set => SetValue(BrandTitleProperty, value); }
    public string WelcomeTitle { get => (string)GetValue(WelcomeTitleProperty); set => SetValue(WelcomeTitleProperty, value); }
    public string RestaurantName { get => (string)GetValue(RestaurantNameProperty); set => SetValue(RestaurantNameProperty, value); }
    public string Prompt { get => (string)GetValue(PromptProperty); set => SetValue(PromptProperty, value); }
    public string ClockActionText { get => (string)GetValue(ClockActionTextProperty); set => SetValue(ClockActionTextProperty, value); }
    public bool ShowClockAction { get => (bool)GetValue(ShowClockActionProperty); set => SetValue(ShowClockActionProperty, value); }
    public bool ShowMinimize { get => (bool)GetValue(ShowMinimizeProperty); set => SetValue(ShowMinimizeProperty, value); }
    public int PinLength { get => (int)GetValue(PinLengthProperty); set => SetValue(PinLengthProperty, value); }
    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
    public string BusyMessage { get => (string)GetValue(BusyMessageProperty); set => SetValue(BusyMessageProperty, value); }
    public string ErrorMessage { get => (string)GetValue(ErrorMessageProperty); set => SetValue(ErrorMessageProperty, value); }
    public string StatusMessage { get => (string)GetValue(StatusMessageProperty); set => SetValue(StatusMessageProperty, value); }
    public string BannerMessage { get => (string)GetValue(BannerMessageProperty); set => SetValue(BannerMessageProperty, value); }
    public string CurrentPin => _pin;

    public event EventHandler<string>? PinCompleted;
    public event EventHandler? ClockActionRequested;
    public event EventHandler? MinimizeRequested;

    public void ClearPin()
    {
        _pin = string.Empty;
        _pinDots.FilledCount = 0;
    }

    public void SetPin(string pin)
    {
        _pin = new string((pin ?? string.Empty).Where(char.IsDigit).Take(PinLength).ToArray());
        _pinDots.FilledCount = _pin.Length;
    }

    public void UpdateClock(DateTime? now = null)
    {
        var value = now ?? DateTime.Now;
        _dateLabel.Text = value.ToString("dddd, MMMM d, yyyy");
        _timeLabel.Text = value.ToString("HH:mm:ss");
    }

    public void ClearMessages()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
    }

    private void AppendPin(string key)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(key) || !char.IsDigit(key[0]) || _pin.Length >= PinLength)
        {
            return;
        }

        ClearMessages();
        _pin += key[0];
        _pinDots.FilledCount = _pin.Length;
        if (_pin.Length >= PinLength)
        {
            PinCompleted?.Invoke(this, _pin);
        }
    }

    private void BackspacePin()
    {
        if (IsBusy || _pin.Length == 0)
        {
            return;
        }

        ClearMessages();
        _pin = _pin[..^1];
        _pinDots.FilledCount = _pin.Length;
    }

    private void ApplyBusy(bool isBusy)
    {
        _keypad.IsEnabled = !isBusy;
        _clockAction.IsEnabled = !isBusy;
        if (isBusy)
        {
            _busy.Show(BusyMessage);
        }
        else
        {
            _busy.Hide();
        }
    }

    private void ApplyError(string? message)
    {
        var hasError = !string.IsNullOrWhiteSpace(message);
        _error.Text = message ?? string.Empty;
        _error.IsVisible = hasError;
        _errorFrame.IsVisible = hasError;
        if (hasError)
        {
            _status.IsVisible = false;
        }
    }

    private void ApplyStatus(string? message)
    {
        var hasStatus = !string.IsNullOrWhiteSpace(message);
        _status.Text = message ?? string.Empty;
        _status.IsVisible = hasStatus;
        if (hasStatus)
        {
            _errorFrame.IsVisible = false;
        }
    }

    private void ApplyBanner(string? message)
    {
        var hasBanner = !string.IsNullOrWhiteSpace(message);
        _banner.Text = message ?? string.Empty;
        _bannerFrame.IsVisible = hasBanner;
    }
}
