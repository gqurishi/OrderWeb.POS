using OrderWeb.Contracts.Dtos;
using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Controls;

public class FloorSelector : ContentView
{
    private readonly HorizontalStackLayout _row = new() { Spacing = 10 };

    public static readonly BindableProperty FloorsProperty = BindableProperty.Create(
        nameof(Floors), typeof(IReadOnlyList<FloorDto>), typeof(FloorSelector),
        propertyChanged: (b, _, _) => ((FloorSelector)b).Rebuild());

    public static readonly BindableProperty SelectedFloorIdProperty = BindableProperty.Create(
        nameof(SelectedFloorId), typeof(string), typeof(FloorSelector), string.Empty, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((FloorSelector)b).Rebuild());

    public FloorSelector()
    {
        Content = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _row
        };
    }

    public event EventHandler<string>? FloorSelected;
    public IReadOnlyList<FloorDto>? Floors { get => (IReadOnlyList<FloorDto>?)GetValue(FloorsProperty); set => SetValue(FloorsProperty, value); }
    public string SelectedFloorId { get => (string)GetValue(SelectedFloorIdProperty); set => SetValue(SelectedFloorIdProperty, value); }

    private void Rebuild()
    {
        _row.Children.Clear();
        if (Floors is null) return;
        foreach (var floor in Floors.OrderBy(f => f.SortOrder).ThenBy(f => f.Name))
        {
            var selected = string.Equals(floor.Id, SelectedFloorId, StringComparison.OrdinalIgnoreCase);
            var button = new SharedButton
            {
                Text = floor.Name,
                Variant = selected ? ButtonVariant.Primary : ButtonVariant.Secondary,
                HeightRequest = 44
            };
            var id = floor.Id;
            button.Clicked += (_, _) =>
            {
                SelectedFloorId = id;
                FloorSelected?.Invoke(this, id);
            };
            _row.Children.Add(button);
        }
    }
}

/// <summary>
/// Mother-style "How many guests?" picker: 1–12 grid, Other number + Go, red X close.
/// Tapping a cover count confirms immediately (same as Mother VisualTablePage).
/// </summary>
public class GuestCountControl : ContentView
{
    private readonly Label _titleLabel;
    private readonly Entry _otherEntry;
    private int _pendingCustom;

    public static readonly BindableProperty TableTitleProperty = BindableProperty.Create(
        nameof(TableTitle), typeof(string), typeof(GuestCountControl), "Table",
        propertyChanged: (b, _, v) => ((GuestCountControl)b)._titleLabel.Text = v?.ToString() ?? "Table");

    public GuestCountControl()
    {
        _titleLabel = new Label
        {
            Text = "Table",
            FontSize = 20,
            FontFamily = "OpenSansSemibold",
            TextColor = Color.FromArgb("#1F2937")
        };

        var close = new Border
        {
            BackgroundColor = Color.FromArgb("#FEE2E2"),
            StrokeThickness = 0,
            WidthRequest = 36,
            HeightRequest = 36,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Content = new Label
            {
                Text = "X",
                FontSize = 16,
                FontFamily = "OpenSansSemibold",
                TextColor = Color.FromArgb("#DC2626"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        var closeTap = new TapGestureRecognizer();
        closeTap.Tapped += (_, _) => Cancelled?.Invoke(this, EventArgs.Empty);
        close.GestureRecognizers.Add(closeTap);

        var header = new Grid
        {
            Padding = new Thickness(24, 20),
            BackgroundColor = Color.FromArgb("#F9FAFB"),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        _titleLabel,
                        new Label
                        {
                            Text = "How many guests?",
                            FontSize = 14,
                            FontFamily = "OpenSansRegular",
                            TextColor = Color.FromArgb("#6B7280")
                        }
                    }
                },
                close
            }
        };
        Grid.SetColumn(close, 1);

        var numbers = new Grid
        {
            Padding = 20,
            ColumnSpacing = 10,
            RowSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };

        for (var i = 1; i <= 12; i++)
        {
            var covers = i;
            var button = new Button
            {
                Text = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 18,
                FontFamily = "OpenSansSemibold",
                BackgroundColor = Color.FromArgb("#F3F4F6"),
                TextColor = Color.FromArgb("#1F2937"),
                CornerRadius = 10,
                HeightRequest = 56,
                Padding = 0
            };
            button.Clicked += (_, _) => CoverConfirmed?.Invoke(this, covers);
            numbers.Add(button, (i - 1) % 4, (i - 1) / 4);
        }

        _otherEntry = new Entry
        {
            AutomationId = "GuestQuantity",
            Placeholder = "Other number...",
            FontSize = 15,
            FontFamily = "OpenSansRegular",
            BackgroundColor = Colors.White,
            Keyboard = Keyboard.Numeric,
            HeightRequest = 48,
            TextColor = Color.FromArgb("#1F2937"),
            PlaceholderColor = Color.FromArgb("#9CA3AF")
        };

        var go = new Button
        {
            Text = "Go",
            FontSize = 14,
            FontFamily = "OpenSansSemibold",
            BackgroundColor = Color.FromArgb("#3B82F6"),
            TextColor = Colors.White,
            CornerRadius = 8,
            WidthRequest = 72,
            HeightRequest = 48,
            Padding = new Thickness(20, 12)
        };
        go.Clicked += (_, _) =>
        {
            var text = _otherEntry.Text?.Trim();
            if (int.TryParse(text, out var covers) && covers > 0)
            {
                CoverConfirmed?.Invoke(this, covers);
                return;
            }

            if (_pendingCustom > 0)
            {
                CoverConfirmed?.Invoke(this, _pendingCustom);
            }
        };

        var otherHost = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#D1D5DB"),
            StrokeThickness = 1,
            Padding = new Thickness(12, 0),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = _otherEntry
        };

        var footer = new Border
        {
            BackgroundColor = Color.FromArgb("#F9FAFB"),
            StrokeThickness = 0,
            Padding = new Thickness(20, 16),
            Content = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                ColumnSpacing = 12,
                Children = { otherHost, go }
            }
        };
        Grid.SetColumn(go, 1);

        Content = new Border
        {
            WidthRequest = 380,
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 8),
                Radius = 24,
                Opacity = 0.25f
            },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 0,
                Children = { header, numbers, footer }
            }
        };
    }

    public event EventHandler<int>? CoverConfirmed;
    public event EventHandler? Cancelled;

    public string TableTitle
    {
        get => (string)GetValue(TableTitleProperty);
        set => SetValue(TableTitleProperty, value);
    }

    public void ResetCustomEntry()
    {
        _pendingCustom = 0;
        _otherEntry.Text = string.Empty;
        _otherEntry.Placeholder = "Other number...";
    }
}
