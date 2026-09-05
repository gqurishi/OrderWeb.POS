using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Controls;

/// <summary>
/// Shared loading overlay used by Mother and Client operational screens.
/// </summary>
public sealed class LoadingOverlayView : ContentView
{
    private readonly Label _message;

    public static readonly BindableProperty MessageProperty =
        BindableProperty.Create(nameof(Message), typeof(string), typeof(LoadingOverlayView), "Loading…",
            propertyChanged: (b, _, v) => ((LoadingOverlayView)b)._message.Text = v?.ToString() ?? "Loading…");

    public LoadingOverlayView()
    {
        IsVisible = false;
        InputTransparent = false;
        ZIndex = 900;
        this.Use(BackgroundColorProperty, "PosLoadingScrim");

        _message = new Label
        {
            Text = "Loading…",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center
        };
        _message.Use(Label.TextColorProperty, "PosTextSecondary");

        var indicator = new ActivityIndicator { IsRunning = true };
        indicator.Use(ActivityIndicator.ColorProperty, "PosPrimary");

        var card = new Border
        {
            Padding = new Thickness(24, 20),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children = { indicator, _message }
            }
        };
        card.Use(Border.BackgroundColorProperty, "PosSurface");
        card.Use(Border.StrokeProperty, "PosBorder");

        Content = card;
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public void Show(string? message = null)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Message = message;
        }

        IsVisible = true;
    }

    public void Hide() => IsVisible = false;
}

/// <summary>
/// Shared empty-state panel (no orders, no customers, no tables, etc.).
/// </summary>
public sealed class EmptyStateView : ContentView
{
    private readonly Label _title;
    private readonly Label _detail;

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(EmptyStateView), "Nothing here yet",
            propertyChanged: (b, _, v) => ((EmptyStateView)b)._title.Text = v?.ToString() ?? "Nothing here yet");

    public static readonly BindableProperty DetailProperty =
        BindableProperty.Create(nameof(Detail), typeof(string), typeof(EmptyStateView), string.Empty,
            propertyChanged: (b, _, v) =>
            {
                var view = (EmptyStateView)b;
                view._detail.Text = v?.ToString() ?? string.Empty;
                view._detail.IsVisible = !string.IsNullOrWhiteSpace(view._detail.Text);
            });

    public EmptyStateView()
    {
        _title = new Label
        {
            Text = "Nothing here yet",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center
        };
        _title.Use(Label.TextColorProperty, "PosTextStrong");

        _detail = new Label
        {
            FontSize = 14,
            HorizontalTextAlignment = TextAlignment.Center,
            IsVisible = false
        };
        _detail.Use(Label.TextColorProperty, "PosTextMuted");

        Content = new VerticalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Padding = new Thickness(24),
            Children = { _title, _detail }
        };
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }
}

/// <summary>
/// Shared recoverable error panel for operational screens.
/// </summary>
public sealed class ErrorStateView : ContentView
{
    private readonly Label _title;
    private readonly Label _detail;
    private readonly SharedButton _retry;

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(ErrorStateView), "Something went wrong",
            propertyChanged: (b, _, v) => ((ErrorStateView)b)._title.Text = v?.ToString() ?? "Something went wrong");

    public static readonly BindableProperty DetailProperty =
        BindableProperty.Create(nameof(Detail), typeof(string), typeof(ErrorStateView), string.Empty,
            propertyChanged: (b, _, v) => ((ErrorStateView)b)._detail.Text = v?.ToString() ?? string.Empty);

    public static readonly BindableProperty RetryTextProperty =
        BindableProperty.Create(nameof(RetryText), typeof(string), typeof(ErrorStateView), "Try again",
            propertyChanged: (b, _, v) => ((ErrorStateView)b)._retry.Text = v?.ToString() ?? "Try again");

    public ErrorStateView()
    {
        IsVisible = false;

        _title = new Label
        {
            Text = "Something went wrong",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center
        };
        _title.Use(Label.TextColorProperty, "PosErrorText");

        _detail = new Label
        {
            FontSize = 14,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };
        _detail.Use(Label.TextColorProperty, "PosTextMuted");

        _retry = new SharedButton { Text = "Try again", Variant = ButtonVariant.Secondary };
        _retry.Clicked += (_, _) => RetryRequested?.Invoke(this, EventArgs.Empty);

        var panel = new Border
        {
            Padding = new Thickness(20),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children = { _title, _detail, _retry }
            }
        };
        panel.Use(Border.BackgroundColorProperty, "PosErrorSoft");
        panel.Use(Border.StrokeProperty, "PosErrorBorder");

        Content = new Grid
        {
            Padding = new Thickness(24),
            Children = { panel }
        };
        panel.HorizontalOptions = LayoutOptions.Center;
        panel.VerticalOptions = LayoutOptions.Center;
        panel.MaximumWidthRequest = 420;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public string RetryText
    {
        get => (string)GetValue(RetryTextProperty);
        set => SetValue(RetryTextProperty, value);
    }

    public event EventHandler? RetryRequested;

    public void Show(string? title = null, string? detail = null)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title;
        }

        if (detail is not null)
        {
            Detail = detail;
        }

        IsVisible = true;
    }

    public void Hide() => IsVisible = false;
}

/// <summary>
/// Shared offline / reconnect / sync-status banner for Client and Mother hosts.
/// </summary>
public sealed class OfflineStatusBannerView : ContentView
{
    private readonly Label _label;
    private readonly Border _root;

    public static readonly BindableProperty MessageProperty =
        BindableProperty.Create(nameof(Message), typeof(string), typeof(OfflineStatusBannerView), string.Empty,
            propertyChanged: OnMessageChanged);

    public static readonly BindableProperty ToneProperty =
        BindableProperty.Create(nameof(Tone), typeof(string), typeof(OfflineStatusBannerView), "warning",
            propertyChanged: OnToneChanged);

    public OfflineStatusBannerView()
    {
        IsVisible = false;

        _label = new Label
        {
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center
        };

        _root = new Border
        {
            StrokeThickness = 0,
            Padding = new Thickness(14, 10),
            Content = _label
        };
        _root.BackgroundColor = Color.FromArgb("#F59E0B");

        Content = _root;
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>warning | offline | syncing | error | info</summary>
    public string Tone
    {
        get => (string)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public void Apply(string? message, string tone = "warning")
    {
        Message = message ?? string.Empty;
        Tone = tone;
    }

    private static void OnMessageChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (OfflineStatusBannerView)bindable;
        var text = newValue?.ToString();
        view._label.Text = text ?? string.Empty;
        view.IsVisible = !string.IsNullOrWhiteSpace(text);
    }

    private static void OnToneChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (OfflineStatusBannerView)bindable;
        var tone = (newValue?.ToString() ?? "warning").Trim().ToLowerInvariant();
        view._root.BackgroundColor = Color.FromArgb(tone switch
        {
            "offline" or "error" or "danger" => "#DC2626",
            "syncing" or "info" => "#3B82F6",
            "success" or "online" => "#10B981",
            _ => "#F59E0B"
        });
    }
}
