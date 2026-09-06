using OrderWeb.Contracts.Dtos;

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

public class GuestCountControl : ContentView
{
    private readonly Label _value;

    public static readonly BindableProperty CountProperty = BindableProperty.Create(
        nameof(Count), typeof(int), typeof(GuestCountControl), 2, BindingMode.TwoWay,
        propertyChanged: (b, _, v) => ((GuestCountControl)b)._value.Text = v?.ToString() ?? "0");

    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(nameof(Minimum), typeof(int), typeof(GuestCountControl), 1);
    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(nameof(Maximum), typeof(int), typeof(GuestCountControl), 99);

    public GuestCountControl()
    {
        var minus = new SharedButton { Text = "−", WidthRequest = 56, HeightRequest = 48, Variant = ButtonVariant.Secondary };
        var plus = new SharedButton { Text = "+", WidthRequest = 56, HeightRequest = 48 };
        _value = new Label
        {
            Text = "2",
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            WidthRequest = 64
        };
        _value.Use(Label.TextColorProperty, "OwTextPrimary");
        minus.Clicked += (_, _) => { if (Count > Minimum) Count--; };
        plus.Clicked += (_, _) => { if (Count < Maximum) Count++; };

        var title = new Label { Text = "How many guests?", FontSize = 16, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
        title.Use(Label.TextColorProperty, "OwTextStrong");

        Content = new VerticalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                title,
                new HorizontalStackLayout
                {
                    Spacing = 12,
                    HorizontalOptions = LayoutOptions.Center,
                    Children = { minus, _value, plus }
                }
            }
        };
    }

    public int Count { get => (int)GetValue(CountProperty); set => SetValue(CountProperty, value); }
    public int Minimum { get => (int)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
}
