namespace OrderWeb.SharedUI.Controls;

public class StatusBadge : Border
{
    private readonly BoxView _dot;
    private readonly Label _label;
    public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(StatusBadge), string.Empty, propertyChanged: (b, _, v) => ((StatusBadge)b)._label.Text = v?.ToString());
    public static readonly BindableProperty KindProperty = BindableProperty.Create(nameof(Kind), typeof(StatusKind), typeof(StatusBadge), StatusKind.Neutral, propertyChanged: (b, _, _) => ((StatusBadge)b).ApplyKind());
    public static readonly BindableProperty ShowDotProperty = BindableProperty.Create(nameof(ShowDot), typeof(bool), typeof(StatusBadge), true, propertyChanged: (b, _, v) => ((StatusBadge)b)._dot.IsVisible = (bool)v);

    public StatusBadge()
    {
        Padding = new Thickness(12, 6);
        StrokeThickness = 1;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 };
        _dot = new BoxView { WidthRequest = 8, HeightRequest = 8, CornerRadius = 4, VerticalOptions = LayoutOptions.Center };
        _label = new Label { FontSize = 13, FontAttributes = FontAttributes.Bold, VerticalTextAlignment = TextAlignment.Center };
        Content = new HorizontalStackLayout { Spacing = 8, Children = { _dot, _label } };
        ApplyKind();
    }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public StatusKind Kind { get => (StatusKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool ShowDot { get => (bool)GetValue(ShowDotProperty); set => SetValue(ShowDotProperty, value); }
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
        this.Use(BackgroundColorProperty, bg); this.Use(StrokeProperty, stroke);
        _label.Use(Label.TextColorProperty, text); _dot.Use(BoxView.ColorProperty, stroke);
    }
}
