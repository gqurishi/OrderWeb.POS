using System.Windows.Input;

namespace OrderWeb.SharedUI.Controls;

public class SharedButton : Button
{
    public static readonly BindableProperty VariantProperty = BindableProperty.Create(
        nameof(Variant), typeof(ButtonVariant), typeof(SharedButton), ButtonVariant.Primary,
        propertyChanged: (b, _, _) => ((SharedButton)b).ApplyVariant());

    public SharedButton()
    {
        HeightRequest = ControlResources.Value("PosButtonHeight", 48d);
        MinimumHeightRequest = ControlResources.Value("PosTouchTargetMinimum", 44d);
        CornerRadius = (int)ControlResources.Value("PosCornerRadius", 12d);
        Padding = ControlResources.Value("PosButtonPadding", new Thickness(20, 0));
        FontAttributes = FontAttributes.Bold;
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
            ButtonVariant.Secondary => ("PosSurfaceStrong", "PosTextStrong"),
            ButtonVariant.Success => ("PosSuccessStrong", "PosTextOnPrimary"),
            ButtonVariant.Danger => ("PosErrorStrong", "PosTextOnPrimary"),
            _ => ("PosPrimary", "PosTextOnPrimary")
        };
        this.Use(BackgroundColorProperty, background);
        this.Use(TextColorProperty, foreground);
    }
}
