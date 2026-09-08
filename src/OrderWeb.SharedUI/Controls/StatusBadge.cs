namespace OrderWeb.SharedUI.Controls;

public class StatusBadge : Border
{
    private readonly BoxView _dot;
    private readonly Label _label;

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(StatusBadge), string.Empty,
        propertyChanged: (b, _, v) =>
        {
            var badge = (StatusBadge)b;
            badge._label.Text = v?.ToString() ?? string.Empty;
            badge.ApplyCompactChrome();
        });

    public static readonly BindableProperty KindProperty = BindableProperty.Create(
        nameof(Kind), typeof(StatusKind), typeof(StatusBadge), StatusKind.Neutral,
        propertyChanged: (b, _, _) => ((StatusBadge)b).ApplyKind());

    public static readonly BindableProperty ShowDotProperty = BindableProperty.Create(
        nameof(ShowDot), typeof(bool), typeof(StatusBadge), true,
        propertyChanged: (b, _, v) => ((StatusBadge)b)._dot.IsVisible = (bool)v);

    public static readonly BindableProperty CompactProperty = BindableProperty.Create(
        nameof(Compact), typeof(bool), typeof(StatusBadge), false,
        propertyChanged: (b, _, _) => ((StatusBadge)b).ApplyCompactChrome());

    public StatusBadge()
    {
        _dot = new BoxView
        {
            WidthRequest = 8,
            HeightRequest = 8,
            CornerRadius = 4,
            VerticalOptions = LayoutOptions.Center
        };
        _label = new Label
        {
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center
        };
        Content = new HorizontalStackLayout { Spacing = 8, Children = { _dot, _label } };
        ApplyKind();
        ApplyCompactChrome();
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public StatusKind Kind { get => (StatusKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool ShowDot { get => (bool)GetValue(ShowDotProperty); set => SetValue(ShowDotProperty, value); }
    public bool Compact { get => (bool)GetValue(CompactProperty); set => SetValue(CompactProperty, value); }

    private void ApplyCompactChrome()
    {
        if (Compact)
        {
            Padding = 0;
            StrokeThickness = 0;
            BackgroundColor = Colors.Transparent;
            Stroke = Colors.Transparent;
            WidthRequest = 14;
            HeightRequest = 14;
            MinimumWidthRequest = 14;
            MinimumHeightRequest = 14;
            VerticalOptions = LayoutOptions.Center;
            _label.IsVisible = false;
            _dot.WidthRequest = 12;
            _dot.HeightRequest = 12;
            _dot.CornerRadius = 6;
            _dot.IsVisible = true;
            SemanticProperties.SetDescription(this, string.IsNullOrWhiteSpace(Text) ? "Connection status" : Text);
            ToolTipProperties.SetText(this, string.IsNullOrWhiteSpace(Text) ? "Connection status" : Text);
            return;
        }

        Padding = new Thickness(12, 6);
        StrokeThickness = 1;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 };
        ClearValue(WidthRequestProperty);
        ClearValue(HeightRequestProperty);
        ClearValue(MinimumWidthRequestProperty);
        ClearValue(MinimumHeightRequestProperty);
        _label.IsVisible = true;
        _dot.WidthRequest = 8;
        _dot.HeightRequest = 8;
        _dot.CornerRadius = 4;
        _dot.IsVisible = ShowDot;
        ApplyKind();
    }

    private void ApplyKind()
    {
        var (bg, stroke, text) = Kind switch
        {
            StatusKind.Success => ("OwSuccessSoft", "OwSuccess", "OwSuccessText"),
            StatusKind.Warning => ("OwWarningSoft", "OwWarningBorder", "OwWarningText"),
            StatusKind.Error => ("OwErrorSoft", "OwErrorBorder", "OwErrorText"),
            StatusKind.Info => ("OwInfoSoft", "OwPrimarySoftBorder", "OwInfoText"),
            _ => ("OwSurfaceMuted", "OwBorder", "OwTextSecondary")
        };

        if (!Compact)
        {
            this.Use(BackgroundColorProperty, bg);
            this.Use(StrokeProperty, stroke);
        }

        _label.Use(Label.TextColorProperty, text);
        _dot.Use(BoxView.ColorProperty, stroke);
    }
}
