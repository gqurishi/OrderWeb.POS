using System.Windows.Input;

namespace OrderWeb.SharedUI.Controls;

public class SharedButton : Button
{
    public static readonly BindableProperty VariantProperty = BindableProperty.Create(
        nameof(Variant), typeof(ButtonVariant), typeof(SharedButton), ButtonVariant.Primary,
        propertyChanged: (b, _, _) => ((SharedButton)b).ApplyVariant());

    public SharedButton()
    {
        MinimumHeightRequest = 44;
        Padding = new Thickness(20, 0);
        FontAttributes = FontAttributes.Bold;
        this.Use(HeightRequestProperty, "PosButtonHeight");
        this.Use(CornerRadiusProperty, "PosButtonCornerRadius");
        ApplyVariant();
    }

    public ButtonVariant Variant
    {
        get => (ButtonVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    private void ApplyVariant()
    {
        var (background, foreground) = Variant switch
        {
            ButtonVariant.Secondary => ("OwSurfaceStrong", "OwTextStrong"),
            ButtonVariant.Success => ("OwSuccessStrong", "OwTextOnPrimary"),
            ButtonVariant.Danger => ("OwErrorStrong", "OwTextOnPrimary"),
            ButtonVariant.Utility => ("OwSurfaceMuted", "OwTextSecondary"),
            _ => ("OwPrimary", "OwTextOnPrimary")
        };
        this.Use(BackgroundColorProperty, background);
        this.Use(TextColorProperty, foreground);
    }
}

/// <summary>The canonical Mother-derived button used by both POS hosts.</summary>
public class PosButton : SharedButton
{
    public PosButton()
    {
        this.Use(StyleProperty, "PosButtonStyle");
    }
}
