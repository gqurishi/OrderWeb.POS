using System.Windows.Input;

namespace OrderWeb.SharedUI.Controls;

public class QuantitySelector : ContentView
{
    private readonly Label _value;

    public static readonly BindableProperty QuantityProperty = BindableProperty.Create(
        nameof(Quantity), typeof(int), typeof(QuantitySelector), 1, BindingMode.TwoWay,
        propertyChanged: (b, _, v) => ((QuantitySelector)b)._value.Text = Math.Max(0, (int)v).ToString());

    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(nameof(Minimum), typeof(int), typeof(QuantitySelector), 0);
    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(nameof(Maximum), typeof(int), typeof(QuantitySelector), 99);
    public static readonly BindableProperty ChangedCommandProperty = BindableProperty.Create(nameof(ChangedCommand), typeof(ICommand), typeof(QuantitySelector));

    public QuantitySelector()
    {
        var minus = new SharedButton { Text = "−", WidthRequest = 44, HeightRequest = 44, Variant = ButtonVariant.Secondary };
        var plus = new SharedButton { Text = "+", WidthRequest = 44, HeightRequest = 44 };
        _value = new Label
        {
            Text = "1",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            WidthRequest = 40,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        _value.Use(Label.TextColorProperty, "OwTextPrimary");
        minus.Clicked += (_, _) =>
        {
            if (Quantity > Minimum)
            {
                Quantity--;
                RaiseChanged();
            }
        };
        plus.Clicked += (_, _) =>
        {
            if (Quantity < Maximum)
            {
                Quantity++;
                RaiseChanged();
            }
        };

        Content = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children = { minus, _value, plus }
        };
    }

    public int Quantity { get => (int)GetValue(QuantityProperty); set => SetValue(QuantityProperty, value); }
    public int Minimum { get => (int)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public ICommand? ChangedCommand { get => (ICommand?)GetValue(ChangedCommandProperty); set => SetValue(ChangedCommandProperty, value); }
    public event EventHandler<int>? QuantityChanged;

    private void RaiseChanged()
    {
        QuantityChanged?.Invoke(this, Quantity);
        if (ChangedCommand?.CanExecute(Quantity) == true)
        {
            ChangedCommand.Execute(Quantity);
        }
    }
}

public class BasketSummaryView : ContentView
{
    private readonly Label _subtotal;
    private readonly Label _discount;
    private readonly Label _tax;
    private readonly Label _total;
    private readonly Label _estimate;

    public static readonly BindableProperty SubtotalTextProperty = BindableProperty.Create(nameof(SubtotalText), typeof(string), typeof(BasketSummaryView), "£0.00", propertyChanged: (b, _, v) => ((BasketSummaryView)b)._subtotal.Text = v?.ToString());
    public static readonly BindableProperty DiscountTextProperty = BindableProperty.Create(nameof(DiscountText), typeof(string), typeof(BasketSummaryView), "£0.00", propertyChanged: (b, _, v) => ((BasketSummaryView)b)._discount.Text = v?.ToString());
    public static readonly BindableProperty TaxTextProperty = BindableProperty.Create(nameof(TaxText), typeof(string), typeof(BasketSummaryView), "£0.00", propertyChanged: (b, _, v) => ((BasketSummaryView)b)._tax.Text = v?.ToString());
    public static readonly BindableProperty TotalTextProperty = BindableProperty.Create(nameof(TotalText), typeof(string), typeof(BasketSummaryView), "£0.00", propertyChanged: (b, _, v) => ((BasketSummaryView)b)._total.Text = v?.ToString());
    public static readonly BindableProperty IsEstimateProperty = BindableProperty.Create(nameof(IsEstimate), typeof(bool), typeof(BasketSummaryView), false, propertyChanged: (b, _, v) => ((BasketSummaryView)b)._estimate.IsVisible = (bool)v);

    public BasketSummaryView()
    {
        _subtotal = RowValue();
        _discount = RowValue();
        _tax = RowValue();
        _total = RowValue(true);
        _estimate = new Label
        {
            Text = "Display total — waiting for Mother confirmation",
            FontSize = 12,
            IsVisible = false,
            Margin = new Thickness(0, 6, 0, 0)
        };
        _estimate.Use(Label.TextColorProperty, "OwWarningText");

        Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Row("Subtotal", _subtotal),
                Row("Discount", _discount),
                Row("Tax", _tax),
                Row("Total", _total),
                _estimate
            }
        };
    }

    public string SubtotalText { get => (string)GetValue(SubtotalTextProperty); set => SetValue(SubtotalTextProperty, value); }
    public string DiscountText { get => (string)GetValue(DiscountTextProperty); set => SetValue(DiscountTextProperty, value); }
    public string TaxText { get => (string)GetValue(TaxTextProperty); set => SetValue(TaxTextProperty, value); }
    public string TotalText { get => (string)GetValue(TotalTextProperty); set => SetValue(TotalTextProperty, value); }
    public bool IsEstimate { get => (bool)GetValue(IsEstimateProperty); set => SetValue(IsEstimateProperty, value); }

    private static Label RowValue(bool strong = false)
    {
        var label = new Label
        {
            Text = "£0.00",
            FontSize = strong ? 22 : 15,
            FontAttributes = strong ? FontAttributes.Bold : FontAttributes.None,
            HorizontalTextAlignment = TextAlignment.End
        };
        label.Use(Label.TextColorProperty, strong ? "OwTextPrimary" : "OwTextSecondary");
        return label;
    }

    private static View Row(string title, View value)
    {
        var label = new Label { Text = title, FontSize = 15, VerticalTextAlignment = TextAlignment.Center };
        label.Use(Label.TextColorProperty, "OwTextMuted");
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        grid.Add(label);
        grid.Add(value, 1);
        return grid;
    }
}
