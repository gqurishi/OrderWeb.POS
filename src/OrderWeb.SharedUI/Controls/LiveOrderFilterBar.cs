using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.SharedUI.Controls;

/// <summary>Mother-style All / Collection / Delivery / Table filter strip (no Refresh).</summary>
public sealed class LiveOrderFilterBar : ContentView
{
    private static readonly Color ActiveBackground = Color.FromArgb("#10B981");
    private static readonly Color InactiveBackground = Color.FromArgb("#F5F5F5");
    private static readonly Color InactiveText = Color.FromArgb("#6B7280");

    private readonly Dictionary<LiveOrderFilter, Border> _tabBorders = new();
    private readonly Dictionary<LiveOrderFilter, Label> _tabLabels = new();
    private bool _suppressFilterChanged;

    public static readonly BindableProperty SelectedFilterProperty = BindableProperty.Create(
        nameof(SelectedFilter),
        typeof(LiveOrderFilter),
        typeof(LiveOrderFilterBar),
        LiveOrderFilter.All,
        propertyChanged: (b, _, n) => ((LiveOrderFilterBar)b).ApplySelected((LiveOrderFilter)n));

    public LiveOrderFilter SelectedFilter
    {
        get => (LiveOrderFilter)GetValue(SelectedFilterProperty);
        set => SetValue(SelectedFilterProperty, value);
    }

    public event EventHandler<LiveOrderFilterChangedEventArgs>? FilterChanged;

    public LiveOrderFilterBar()
    {
        var row = new HorizontalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.Center,
            Padding = new Thickness(12, 10)
        };

        row.Children.Add(CreateTab(LiveOrderFilter.All, "All"));
        row.Children.Add(CreateTab(LiveOrderFilter.Collection, "Collection"));
        row.Children.Add(CreateTab(LiveOrderFilter.Delivery, "Delivery"));
        row.Children.Add(CreateTab(LiveOrderFilter.Table, "Table"));

        Content = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 1,
            Padding = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 0 },
            Content = row
        };

        ApplySelected(SelectedFilter);
    }

    private Border CreateTab(LiveOrderFilter filter, string text)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };

        var border = new Border
        {
            BackgroundColor = InactiveBackground,
            StrokeThickness = 0,
            Padding = new Thickness(32, 14),
            HeightRequest = 50,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = label
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (SelectedFilter == filter)
            {
                return;
            }

            SelectedFilter = filter;
            FilterChanged?.Invoke(this, new LiveOrderFilterChangedEventArgs(filter));
        };
        border.GestureRecognizers.Add(tap);

        _tabBorders[filter] = border;
        _tabLabels[filter] = label;
        return border;
    }

    private void ApplySelected(LiveOrderFilter selected)
    {
        foreach (var (filter, border) in _tabBorders)
        {
            var isActive = filter == selected;
            border.BackgroundColor = isActive ? ActiveBackground : InactiveBackground;
            _tabLabels[filter].TextColor = isActive ? Colors.White : InactiveText;
        }

        if (_suppressFilterChanged)
        {
            return;
        }
    }

    /// <summary>Set filter without raising <see cref="FilterChanged"/> (host sync).</summary>
    public void SetSelectedFilterQuiet(LiveOrderFilter filter)
    {
        if (SelectedFilter == filter)
        {
            ApplySelected(filter);
            return;
        }

        _suppressFilterChanged = true;
        try
        {
            SelectedFilter = filter;
        }
        finally
        {
            _suppressFilterChanged = false;
        }
    }
}
