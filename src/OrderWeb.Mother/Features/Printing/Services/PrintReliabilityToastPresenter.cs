namespace POS_in_NET.Services;

/// <summary>
/// Short Phase 3 toast when a printer comes back and failed tickets auto-retry.
/// </summary>
public sealed class PrintReliabilityToastPresenter
{
    private static readonly string[] HiddenRoutes = ["login", "terminalsetup", "initialadminsetup"];

    private readonly PrintReliabilityCoordinator _coordinator;
    private readonly Border _toast;
    private readonly Label _label;
    private readonly Grid _host = new()
    {
        InputTransparent = true,
        CascadeInputTransparent = true,
        HorizontalOptions = LayoutOptions.Fill,
        VerticalOptions = LayoutOptions.Fill,
        ZIndex = 850
    };
    private bool _started;
    private CancellationTokenSource? _hideCts;

    public PrintReliabilityToastPresenter(PrintReliabilityCoordinator coordinator)
    {
        _coordinator = coordinator;
        _label = new Label
        {
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#1E3A8A"),
            VerticalOptions = LayoutOptions.Center
        };
        _toast = new Border
        {
            Padding = new Thickness(14, 10),
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#BFDBFE"),
            BackgroundColor = Color.FromArgb("#EFF6FF"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(18, 56, 18, 0),
            IsVisible = false,
            Content = _label,
            Shadow = new Shadow
            {
                Brush = Colors.Black,
                Offset = new Point(0, 2),
                Radius = 8,
                Opacity = 0.14f
            }
        };
        _host.Children.Add(_toast);
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _coordinator.ToastRequested += OnToastRequested;
    }

    private void OnToastRequested(object? sender, PrintReliabilityToastEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Message))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => ShowToast(e.Message));
    }

    private void ShowToast(string message)
    {
        if (AuthenticationService.Instance.CurrentUser == null || IsHiddenRoute())
        {
            return;
        }

        Attach();
        _label.Text = message;
        _toast.IsVisible = true;

        _hideCts?.Cancel();
        _hideCts = new CancellationTokenSource();
        var token = _hideCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(4500, token);
                if (!token.IsCancellationRequested)
                {
                    MainThread.BeginInvokeOnMainThread(() => _toast.IsVisible = false);
                }
            }
            catch (OperationCanceledException)
            {
                // Newer toast replaced this one.
            }
        }, token);
    }

    private void Attach()
    {
        if (Shell.Current?.CurrentPage is not ContentPage page || IsHiddenRoute())
        {
            return;
        }

        if (page.Content is Grid root)
        {
            if (!ReferenceEquals(_host.Parent, root))
            {
                if (_host.Parent is Grid old)
                {
                    old.Children.Remove(_host);
                }

                if (!root.Children.Contains(_host))
                {
                    root.Children.Add(_host);
                }
            }
        }
    }

    private static bool IsHiddenRoute()
    {
        var route = Shell.Current?.CurrentState?.Location?.OriginalString ?? string.Empty;
        return HiddenRoutes.Any(hidden =>
            route.Contains(hidden, StringComparison.OrdinalIgnoreCase));
    }
}
