using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Controls;

/// <summary>
/// Shared PIN dot display used by Mother and Client login screens.
/// </summary>
public sealed class PinDotsView : ContentView
{
    private readonly HorizontalStackLayout _dots = new()
    {
        Spacing = 22,
        HorizontalOptions = LayoutOptions.Center
    };

    public static readonly BindableProperty LengthProperty = BindableProperty.Create(
        nameof(Length), typeof(int), typeof(PinDotsView), 4,
        propertyChanged: (b, _, _) => ((PinDotsView)b).Rebuild());

    public static readonly BindableProperty FilledCountProperty = BindableProperty.Create(
        nameof(FilledCount), typeof(int), typeof(PinDotsView), 0,
        propertyChanged: (b, _, _) => ((PinDotsView)b).ApplyFill());

    public PinDotsView()
    {
        Content = _dots;
        Rebuild();
    }

    public int Length
    {
        get => (int)GetValue(LengthProperty);
        set => SetValue(LengthProperty, value);
    }

    public int FilledCount
    {
        get => (int)GetValue(FilledCountProperty);
        set => SetValue(FilledCountProperty, value);
    }

    private void Rebuild()
    {
        _dots.Children.Clear();
        var count = Math.Max(1, Length);
        for (var i = 0; i < count; i++)
        {
            var dot = new Border
            {
                WidthRequest = 18,
                HeightRequest = 18,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 9 }
            };
            _dots.Children.Add(dot);
        }

        ApplyFill();
    }

    private void ApplyFill()
    {
        var filled = Math.Clamp(FilledCount, 0, _dots.Children.Count);
        for (var i = 0; i < _dots.Children.Count; i++)
        {
            if (_dots.Children[i] is not Border dot)
            {
                continue;
            }

            if (i < filled)
            {
                dot.Use(Border.BackgroundColorProperty, "PosPrimary");
            }
            else
            {
                dot.BackgroundColor = Color.FromArgb("#E5E7EB");
            }
        }
    }
}
