using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.ViewModels;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Canonical Mother-style PIN login used by Mother and Client.
/// Hosts supply <see cref="LoginViewModel"/> via DI / constructor.
/// </summary>
public class LoginView : ContentView
{
    private LoginViewModel? _viewModel;
    private readonly Label _restaurantName;
    private readonly Label _status;
    private readonly Border _statusFrame;
    private readonly HorizontalStackLayout _pinDots;
    private readonly Label _date;
    private readonly Label _time;
    private readonly ChefLoaderView _busy;
    private readonly IDispatcherTimer _clock;

    public LoginView()
    {
        _restaurantName = MotherLabel("Restaurant Name", 24, true, "OwTextPrimary");
        _restaurantName.Opacity = 0.72;
        _restaurantName.HorizontalTextAlignment = TextAlignment.Center;
        _restaurantName.MaximumWidthRequest = 300;

        _status = MotherLabel(string.Empty, 16, true, "OwError");
        _status.HorizontalTextAlignment = TextAlignment.Center;
        _statusFrame = new Border
        {
            IsVisible = false,
            Padding = new Thickness(16, 10),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            HorizontalOptions = LayoutOptions.Center,
            Content = _status
        };
        _statusFrame.Use(Border.BackgroundColorProperty, "OwErrorSoft");
        _statusFrame.Use(Border.StrokeProperty, "OwError");

        _pinDots = new HorizontalStackLayout { Spacing = 22, HorizontalOptions = LayoutOptions.Center };
        RebuildDots(0);

        _busy = new ChefLoaderView
        {
            Mode = ChefLoaderMode.Inline,
            Size = ChefLoaderSize.Sm,
            Message = "Signing in",
            DelayMilliseconds = 0,
            IsLoading = false,
            HorizontalOptions = LayoutOptions.Center
        };

        var keypad = new Controls.NumberKeypad { KeySize = 100 };
        keypad.KeyPressed += (_, e) => _viewModel?.KeyCommand.Execute(e.Key);
        keypad.ClearPressed += (_, _) => _viewModel?.ClearCommand.Execute(null);
        keypad.BackspacePressed += (_, _) => _viewModel?.BackspaceCommand.Execute(null);

        var clockIn = new Button
        {
            Text = "Clock In/Out",
            FontSize = 22,
            WidthRequest = 334,
            HeightRequest = 58,
            CornerRadius = 29,
            Padding = 0,
            HorizontalOptions = LayoutOptions.Center
        };
        clockIn.Use(Button.BackgroundColorProperty, "OwPrimary");
        clockIn.Use(Button.TextColorProperty, "OwTextOnPrimary");
        clockIn.Clicked += (_, _) => _viewModel?.ClockInOutCommand.Execute(null);

        var minimize = new Button
        {
            Text = "-",
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 0,
            CornerRadius = 0,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 14, 18, 0),
            ZIndex = 10
        };
        minimize.Use(Button.TextColorProperty, "OwTextPrimary");
        minimize.Clicked += (_, _) => _viewModel?.MinimizeCommand.Execute(null);

        var welcome = new VerticalStackLayout
        {
            Spacing = 58,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                MotherLabel("POS", 58, true, "OwPrimary"),
                new VerticalStackLayout
                {
                    Spacing = 10,
                    HorizontalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 20, 0, 0),
                    Children =
                    {
                        MotherLabel("Welcome Back!", 32, true, "OwTextPrimary"),
                        _restaurantName
                    }
                }
            }
        };

        var left = new Grid { Padding = new Thickness(48, 40), Children = { welcome } };
        left.Use(Grid.BackgroundColorProperty, "OwSurfaceMuted");

        _date = MotherLabel(string.Empty, 13, false, "OwTextSecondary");
        _date.HorizontalTextAlignment = TextAlignment.End;
        _time = MotherLabel(string.Empty, 24, true, "OwPrimary");
        _time.HorizontalTextAlignment = TextAlignment.End;

        var rightContent = new VerticalStackLayout
        {
            Spacing = 46,
            HorizontalOptions = LayoutOptions.Center,
            MaximumWidthRequest = 430,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 28,
                    HorizontalOptions = LayoutOptions.Center,
                    Children =
                    {
                        MotherLabel("Enter your PIN or swipe employee card", 18, false, "OwTextSecondary"),
                        _pinDots,
                        _statusFrame,
                        _busy
                    }
                },
                new VerticalStackLayout
                {
                    Spacing = 36,
                    HorizontalOptions = LayoutOptions.Center,
                    Children = { keypad, clockIn }
                }
            }
        };

        var right = new Grid { Padding = new Thickness(44, 34) };
        right.Use(Grid.BackgroundColorProperty, "OwSurface");
        right.Add(new ScrollView
        {
            VerticalOptions = LayoutOptions.Center,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = rightContent
        });
        right.Add(new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Spacing = 4,
            Margin = new Thickness(0, 20, 0, 0),
            Children = { _time, _date }
        });

        var root = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        root.Use(Grid.BackgroundColorProperty, "OwSurface");
        root.Add(left);
        root.Add(right, 1);
        root.Add(minimize);
        Content = root;

        _clock = Dispatcher.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) =>
        {
            _viewModel?.UpdateClock();
            SyncClockLabels();
        };
        _clock.Start();
    }

    public LoginViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel == value) return;
            if (_viewModel is not null) _viewModel.PropertyChanged -= OnVmChanged;
            _viewModel = value;
            BindingContext = value;
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged += OnVmChanged;
                SyncFromVm();
            }
        }
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent is null) _clock.Stop();
        else if (!_clock.IsRunning) _clock.Start();
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => SyncFromVm();

    private void SyncFromVm()
    {
        if (_viewModel is null) return;
        _restaurantName.Text = _viewModel.RestaurantName;
        _status.Text = _viewModel.StatusMessage;
        _statusFrame.IsVisible = _viewModel.HasStatus;
        if (_viewModel.HasError)
        {
            _statusFrame.Use(Border.BackgroundColorProperty, "OwErrorSoft");
            _statusFrame.Use(Border.StrokeProperty, "OwError");
            _status.Use(Label.TextColorProperty, "OwError");
        }
        else
        {
            _statusFrame.Use(Border.BackgroundColorProperty, "OwInfoSoft");
            _statusFrame.Use(Border.StrokeProperty, "OwPrimarySoftBorder");
            _status.Use(Label.TextColorProperty, "OwInfoText");
        }
        _busy.IsLoading = _viewModel.IsBusy;
        _busy.Message = _viewModel.IsBusy ? "Signing in" : "Signing in";
        RebuildDots(_viewModel.PinLength);
        SyncClockLabels();
    }

    private void SyncClockLabels()
    {
        if (_viewModel is null) return;
        _date.Text = _viewModel.DateText;
        _time.Text = _viewModel.TimeText;
    }

    private void RebuildDots(int filled)
    {
        _pinDots.Children.Clear();
        for (var i = 0; i < 4; i++)
        {
            var dot = new Border
            {
                WidthRequest = 18,
                HeightRequest = 18,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 9 }
            };
            if (i < filled) dot.Use(Border.BackgroundColorProperty, "OwPrimary");
            else dot.BackgroundColor = Color.FromArgb("#E5E7EB");
            _pinDots.Children.Add(dot);
        }
    }

    private static Label MotherLabel(string text, double size, bool bold, string colorKey)
    {
        var label = new Label
        {
            Text = text,
            FontSize = size,
            FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
            HorizontalOptions = LayoutOptions.Center
        };
        label.Use(Label.TextColorProperty, colorKey);
        return label;
    }
}
