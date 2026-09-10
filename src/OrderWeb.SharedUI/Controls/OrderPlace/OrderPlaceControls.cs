using System.Globalization;
using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Controls.OrderPlace;

// Order Place Lego bricks — host-neutral.
// Style via Op*/Ow* tokens only (see Themes). Hosts supply data/commands
// through page code or IOrderPlaceHost (Hosting/OrderPlaceHost.cs).
// Docs: docs/ORDER_PLACE_CLIENT_READINESS.md

/// <summary>Shared sizing helpers for Order Place adaptive layout.</summary>
public static class OrderPlaceLayout
{
    public static double Token(string key, double fallback) =>
        ControlResources.Value(key, fallback);

    public static int ProductColumnCount(double availableWidth)
    {
        var minWidth = Token("OpProductCardMinWidth", 100);
        var gap = Token("OpProductCardGap", 10);
        if (availableWidth <= 0)
        {
            return 4;
        }

        var columns = (int)Math.Floor((availableWidth + gap) / (minWidth + gap));
        return Math.Clamp(columns, 2, 6);
    }

    public static async Task FlashPressAsync(VisualElement target, int milliseconds = 70, double pressedOpacity = 0.7)
    {
        var previous = target.Opacity;
        target.Opacity = pressedOpacity;
        try
        {
            await Task.Delay(milliseconds);
        }
        finally
        {
            target.Opacity = previous;
        }
    }
}

/// <summary>Strong main-category chip (selected = filled primary).</summary>
public sealed class OrderPlaceCategoryButton : ContentView
{
    private readonly Border _border;
    private readonly Label _label;

    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(OrderPlaceCategoryButton), string.Empty,
            propertyChanged: (b, _, v) => ((OrderPlaceCategoryButton)b)._label.Text = v?.ToString() ?? string.Empty);

    public static readonly BindableProperty IsSelectedProperty =
        BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(OrderPlaceCategoryButton), false,
            propertyChanged: (b, _, _) => ((OrderPlaceCategoryButton)b).ApplyState());

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(OrderPlaceCategoryButton));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(OrderPlaceCategoryButton));

    public OrderPlaceCategoryButton()
    {
        _label = new Label
        {
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        _label.Use(Label.FontSizeProperty, "OpFontCategory");

        _border = new Border
        {
            StrokeThickness = 1,
            Padding = new Thickness(8, 0),
            Content = _label,
            WidthRequest = OrderPlaceLayout.Token("OpCategoryWidth", 80),
            MinimumWidthRequest = OrderPlaceLayout.Token("OpCategoryWidth", 80)
        };
        _border.Use(Border.HeightRequestProperty, "OpCategoryHeight");
        _border.StrokeShape = new RoundRectangle
        {
            CornerRadius = ControlResources.Value("OpChipCornerRadius", 10)
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            _ = OrderPlaceLayout.FlashPressAsync(this);
            Execute();
        };
        _border.GestureRecognizers.Add(tap);
        Content = _border;
        ApplyState();
    }

    public event EventHandler? Tapped;
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    private void Execute()
    {
        Tapped?.Invoke(this, EventArgs.Empty);
        if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }
    }

    private void ApplyState()
    {
        if (IsSelected)
        {
            _border.Use(Border.BackgroundColorProperty, "OwPrimary");
            _border.Use(Border.StrokeProperty, "OwPrimary");
            _label.Use(Label.TextColorProperty, "OwTextOnPrimary");
        }
        else
        {
            _border.Use(Border.BackgroundColorProperty, "OwSurface");
            _border.Use(Border.StrokeProperty, "OwBorderStrong");
            _label.Use(Label.TextColorProperty, "OwTextStrong");
        }
    }
}

/// <summary>Lighter subcategory pill.</summary>
public sealed class OrderPlaceSubcategoryButton : ContentView
{
    private readonly Border _border;
    private readonly Label _label;

    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(OrderPlaceSubcategoryButton), string.Empty,
            propertyChanged: (b, _, v) => ((OrderPlaceSubcategoryButton)b)._label.Text = v?.ToString() ?? string.Empty);

    public static readonly BindableProperty IsSelectedProperty =
        BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(OrderPlaceSubcategoryButton), false,
            propertyChanged: (b, _, _) => ((OrderPlaceSubcategoryButton)b).ApplyState());

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(OrderPlaceSubcategoryButton));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(OrderPlaceSubcategoryButton));

    public OrderPlaceSubcategoryButton()
    {
        _label = new Label
        {
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        _label.Use(Label.FontSizeProperty, "OpFontSubcategory");

        _border = new Border
        {
            StrokeThickness = 1,
            Padding = new Thickness(8, 0),
            Content = _label,
            WidthRequest = OrderPlaceLayout.Token("OpSubcategoryWidth", 80),
            MinimumWidthRequest = OrderPlaceLayout.Token("OpSubcategoryWidth", 80)
        };
        _border.Use(Border.HeightRequestProperty, "OpSubcategoryHeight");
        _border.StrokeShape = new RoundRectangle
        {
            CornerRadius = ControlResources.Value("OpChipCornerRadius", 10)
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            _ = OrderPlaceLayout.FlashPressAsync(this);
            Execute();
        };
        _border.GestureRecognizers.Add(tap);
        Content = _border;
        ApplyState();
    }

    public event EventHandler? Tapped;
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    private void Execute()
    {
        Tapped?.Invoke(this, EventArgs.Empty);
        if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }
    }

    private void ApplyState()
    {
        // Quieter than main categories: soft selected, not solid fill.
        if (IsSelected)
        {
            _border.Use(Border.BackgroundColorProperty, "OwPrimarySoft");
            _border.Use(Border.StrokeProperty, "OwPrimary");
            _label.Use(Label.TextColorProperty, "OwPrimary");
            _label.FontAttributes = FontAttributes.Bold;
        }
        else
        {
            _border.BackgroundColor = Colors.Transparent;
            _border.Use(Border.StrokeProperty, "OwBorder");
            _label.Use(Label.TextColorProperty, "OwTextMuted");
            _label.FontAttributes = FontAttributes.None;
        }
    }
}

/// <summary>Compact product tile — entire card tappable.</summary>
public sealed class OrderPlaceProductCard : ContentView
{
    private readonly Border _border;
    private readonly Label _name;
    private readonly Label _price;
    private readonly Label _badge;

    public static readonly BindableProperty NameProperty =
        BindableProperty.Create(nameof(Name), typeof(string), typeof(OrderPlaceProductCard), string.Empty,
            propertyChanged: (b, _, v) => ((OrderPlaceProductCard)b)._name.Text = v?.ToString() ?? string.Empty);

    public static readonly BindableProperty PriceProperty =
        BindableProperty.Create(nameof(Price), typeof(decimal), typeof(OrderPlaceProductCard), 0m,
            propertyChanged: (b, _, v) => ((OrderPlaceProductCard)b)._price.Text = FormatMoney((decimal)v));

    public static readonly BindableProperty PriceTextProperty =
        BindableProperty.Create(nameof(PriceText), typeof(string), typeof(OrderPlaceProductCard), string.Empty,
            propertyChanged: (b, _, v) =>
            {
                var text = v?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    ((OrderPlaceProductCard)b)._price.Text = text;
                }
            });

    public static readonly BindableProperty BadgeProperty =
        BindableProperty.Create(nameof(Badge), typeof(string), typeof(OrderPlaceProductCard), string.Empty,
            propertyChanged: (b, _, v) =>
            {
                var c = (OrderPlaceProductCard)b;
                c._badge.Text = v?.ToString() ?? string.Empty;
                c._badge.IsVisible = !string.IsNullOrWhiteSpace(c._badge.Text);
            });

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(OrderPlaceProductCard));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(OrderPlaceProductCard));

    public OrderPlaceProductCard()
    {
        _name = new Label
        {
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap,
            MaxLines = 2
        };
        _name.Use(Label.FontSizeProperty, "OpFontProductName");
        _name.Use(Label.TextColorProperty, "OwTextStrong");

        _price = new Label { FontAttributes = FontAttributes.Bold };
        _price.Use(Label.FontSizeProperty, "OpFontProductPrice");
        _price.Use(Label.TextColorProperty, "OwSuccessStrong");

        _badge = new Label
        {
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            IsVisible = false
        };
        _badge.Use(Label.TextColorProperty, "OwPrimary");

        var stack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { _name, _price, _badge }
        };

        _border = new Border
        {
            StrokeThickness = 1,
            Padding = new Thickness(8, 6),
            Content = stack
        };
        _border.Use(Border.BackgroundColorProperty, "OwSurface");
        _border.Use(Border.StrokeProperty, "OwBorder");
        _border.Use(Border.HeightRequestProperty, "OpProductCardMinHeight");
        _border.Use(Border.MinimumHeightRequestProperty, "OpProductCardMinHeight");
        _border.Use(Border.MinimumWidthRequestProperty, "OpProductCardMinWidth");
        _border.StrokeShape = new RoundRectangle
        {
            CornerRadius = ControlResources.Value("OpProductCardCornerRadius", 10)
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            _ = OrderPlaceLayout.FlashPressAsync(this);
            Tapped?.Invoke(this, EventArgs.Empty);
            if (Command?.CanExecute(CommandParameter) == true)
            {
                Command.Execute(CommandParameter);
            }
        };
        _border.GestureRecognizers.Add(tap);
        Content = _border;
        _price.Text = FormatMoney(0);
    }

    public event EventHandler? Tapped;
    public string Name { get => (string)GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public decimal Price { get => (decimal)GetValue(PriceProperty); set => SetValue(PriceProperty, value); }
    public string PriceText { get => (string)GetValue(PriceTextProperty); set => SetValue(PriceTextProperty, value); }
    public string Badge { get => (string)GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    private static string FormatMoney(decimal value) =>
        value.ToString("C2", CultureInfo.GetCultureInfo("en-GB"));
}

/// <summary>− / count / + control for order lines.</summary>
public sealed class OrderPlaceQuantityControl : ContentView
{
    private readonly Label _count;
    private readonly Border _minus;
    private readonly Border _plus;

    public static readonly BindableProperty QuantityProperty =
        BindableProperty.Create(nameof(Quantity), typeof(int), typeof(OrderPlaceQuantityControl), 1,
            propertyChanged: (b, _, v) => ((OrderPlaceQuantityControl)b)._count.Text = Math.Max(0, (int)v).ToString(CultureInfo.InvariantCulture));

    public static readonly BindableProperty MinimumProperty =
        BindableProperty.Create(nameof(Minimum), typeof(int), typeof(OrderPlaceQuantityControl), 0);

    public OrderPlaceQuantityControl()
    {
        var size = OrderPlaceLayout.Token("OpQuantityButtonSize", 28);
        _minus = QtyButton("−", size);
        _plus = QtyButton("+", size);
        _count = new Label
        {
            Text = "1",
            FontAttributes = FontAttributes.Bold,
            FontSize = 13,
            WidthRequest = 22,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        _count.Use(Label.TextColorProperty, "OwTextStrong");

        ((TapGestureRecognizer)_minus.GestureRecognizers[0]).Tapped += (_, _) => Change(-1);
        ((TapGestureRecognizer)_plus.GestureRecognizers[0]).Tapped += (_, _) => Change(1);

        Content = new HorizontalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { _minus, _count, _plus }
        };
    }

    public event EventHandler<int>? QuantityChanged;
    public int Quantity { get => (int)GetValue(QuantityProperty); set => SetValue(QuantityProperty, value); }
    public int Minimum { get => (int)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    private void Change(int delta)
    {
        var next = Math.Max(Minimum, Quantity + delta);
        if (next == Quantity)
        {
            return;
        }

        Quantity = next;
        QuantityChanged?.Invoke(this, Quantity);
    }

    private static Border QtyButton(string text, double size)
    {
        var label = new Label
        {
            Text = text,
            FontAttributes = FontAttributes.Bold,
            FontSize = 14,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        label.Use(Label.TextColorProperty, "OwTextStrong");
        var border = new Border
        {
            WidthRequest = size,
            HeightRequest = size,
            StrokeThickness = 1,
            Content = label,
            StrokeShape = new RoundRectangle { CornerRadius = 6 }
        };
        border.Use(Border.BackgroundColorProperty, "OwSurfaceMuted");
        border.Use(Border.StrokeProperty, "OwBorder");
        border.GestureRecognizers.Add(new TapGestureRecognizer());
        return border;
    }
}

/// <summary>Order basket line with qty controls.</summary>
public sealed class OrderPlaceLineRow : ContentView
{
    private readonly Border _border;
    private readonly Label _name;
    private readonly Label _price;
    private readonly Label _details;
    private readonly Label _statusCue;
    private readonly Label _noteLabel;
    private readonly Label _trailingLabel;
    private readonly OrderPlaceQuantityControl _qty;
    private readonly Border _noteAction;
    private readonly Border _trailingAction;
    private readonly HorizontalStackLayout _actionsRow;

    public static readonly BindableProperty ItemNameProperty =
        BindableProperty.Create(nameof(ItemName), typeof(string), typeof(OrderPlaceLineRow), string.Empty,
            propertyChanged: (b, _, v) => ((OrderPlaceLineRow)b)._name.Text = v?.ToString() ?? string.Empty);

    public static readonly BindableProperty LineTotalProperty =
        BindableProperty.Create(nameof(LineTotal), typeof(decimal), typeof(OrderPlaceLineRow), 0m,
            propertyChanged: (b, _, v) => ((OrderPlaceLineRow)b)._price.Text =
                ((decimal)v).ToString("C2", CultureInfo.GetCultureInfo("en-GB")));

    public static readonly BindableProperty DetailsProperty =
        BindableProperty.Create(nameof(Details), typeof(string), typeof(OrderPlaceLineRow), string.Empty,
            propertyChanged: (b, _, v) =>
            {
                var c = (OrderPlaceLineRow)b;
                var raw = v?.ToString() ?? string.Empty;
                var text = string.Join(
                    " · ",
                    raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                c._details.Text = text;
                c._details.IsVisible = !string.IsNullOrWhiteSpace(text);
            });

    public static readonly BindableProperty QuantityProperty =
        BindableProperty.Create(nameof(Quantity), typeof(int), typeof(OrderPlaceLineRow), 1,
            propertyChanged: (b, o, n) =>
            {
                var c = (OrderPlaceLineRow)b;
                if (c._qty.Quantity != (int)n)
                {
                    c._qty.Quantity = (int)n;
                }
            });

    public static readonly BindableProperty IsSentProperty =
        BindableProperty.Create(nameof(IsSent), typeof(bool), typeof(OrderPlaceLineRow), false,
            propertyChanged: (b, _, _) => ((OrderPlaceLineRow)b).ApplySentCue());

    public static readonly BindableProperty ShowNoteActionProperty =
        BindableProperty.Create(nameof(ShowNoteAction), typeof(bool), typeof(OrderPlaceLineRow), false,
            propertyChanged: (b, _, _) => ((OrderPlaceLineRow)b).SyncActions());

    public static readonly BindableProperty NoteActionTextProperty =
        BindableProperty.Create(nameof(NoteActionText), typeof(string), typeof(OrderPlaceLineRow), "+ Note",
            propertyChanged: (b, _, v) => ((OrderPlaceLineRow)b)._noteLabel.Text = v?.ToString() ?? "+ Note");

    public static readonly BindableProperty TrailingActionTextProperty =
        BindableProperty.Create(nameof(TrailingActionText), typeof(string), typeof(OrderPlaceLineRow), string.Empty,
            propertyChanged: (b, _, v) =>
            {
                var c = (OrderPlaceLineRow)b;
                c._trailingLabel.Text = v?.ToString() ?? string.Empty;
                c.SyncActions();
            });

    public OrderPlaceLineRow()
    {
        _name = new Label
        {
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap,
            MaxLines = 2,
            VerticalTextAlignment = TextAlignment.Center
        };
        _name.Use(Label.FontSizeProperty, "OpFontLineName");
        _name.Use(Label.TextColorProperty, "OwTextStrong");

        _statusCue = new Label
        {
            Text = "SENT",
            FontSize = 9,
            FontAttributes = FontAttributes.Bold,
            IsVisible = false,
            VerticalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        _statusCue.Use(Label.TextColorProperty, "OwSuccessText");

        _price = new Label
        {
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap
        };
        _price.Use(Label.FontSizeProperty, "OpFontLineName");
        _price.Use(Label.TextColorProperty, "OwSuccessStrong");

        _details = new Label
        {
            IsVisible = false,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            VerticalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        _details.Use(Label.FontSizeProperty, "OpFontLineMeta");
        _details.Use(Label.TextColorProperty, "OwTextMuted");

        _qty = new OrderPlaceQuantityControl { Minimum = 0, VerticalOptions = LayoutOptions.Center };
        _qty.QuantityChanged += (_, value) =>
        {
            // Ignore echoes from Quantity bindable sync (RefreshOrderItems).
            if (Quantity == value)
            {
                return;
            }

            Quantity = value;
            QuantityChanged?.Invoke(this, value);
        };

        _noteLabel = new Label
        {
            Text = "+ Note",
            FontAttributes = FontAttributes.Bold,
            FontSize = 10,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        _noteAction = CreateLineChip(_noteLabel, secondary: true);
        _noteAction.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => NoteTapped?.Invoke(this, EventArgs.Empty))
        });

        _trailingLabel = new Label
        {
            Text = string.Empty,
            FontAttributes = FontAttributes.Bold,
            FontSize = 10,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        _trailingAction = CreateLineChip(_trailingLabel, secondary: false);
        _trailingAction.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => TrailingActionTapped?.Invoke(this, EventArgs.Empty))
        });

        _actionsRow = new HorizontalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
            Children = { _noteAction, _trailingAction }
        };

        var title = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 4,
            VerticalOptions = LayoutOptions.Center
        };
        _name.HorizontalOptions = LayoutOptions.Fill;
        _details.MaximumWidthRequest = 110;
        title.Add(_name);
        title.Add(_statusCue, 1);
        title.Add(_details, 2);

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 6,
            VerticalOptions = LayoutOptions.Center
        };
        row.Add(title);
        row.Add(_qty, 1);
        row.Add(_actionsRow, 2);
        row.Add(_price, 3);

        _border = new Border
        {
            StrokeThickness = 1,
            Padding = new Thickness(8, 4),
            Content = row
        };
        _border.Use(Border.MinimumHeightRequestProperty, "OpOrderLineMinHeight");
        _border.Use(Border.HeightRequestProperty, "OpOrderLineMinHeight");
        _border.StrokeShape = new RoundRectangle { CornerRadius = 8 };
        Content = _border;
        _price.Text = FormatMoney(0);
        ApplySentCue();
        SyncActions();
    }

    public event EventHandler<int>? QuantityChanged;
    public event EventHandler? NoteTapped;
    public event EventHandler? TrailingActionTapped;

    public string ItemName { get => (string)GetValue(ItemNameProperty); set => SetValue(ItemNameProperty, value); }
    public decimal LineTotal { get => (decimal)GetValue(LineTotalProperty); set => SetValue(LineTotalProperty, value); }
    public string Details { get => (string)GetValue(DetailsProperty); set => SetValue(DetailsProperty, value); }
    public int Quantity { get => (int)GetValue(QuantityProperty); set => SetValue(QuantityProperty, value); }
    public bool IsSent { get => (bool)GetValue(IsSentProperty); set => SetValue(IsSentProperty, value); }
    public bool ShowNoteAction { get => (bool)GetValue(ShowNoteActionProperty); set => SetValue(ShowNoteActionProperty, value); }
    public string NoteActionText { get => (string)GetValue(NoteActionTextProperty); set => SetValue(NoteActionTextProperty, value); }
    public string TrailingActionText { get => (string)GetValue(TrailingActionTextProperty); set => SetValue(TrailingActionTextProperty, value); }

    private void SyncActions()
    {
        _noteAction.IsVisible = ShowNoteAction;
        _trailingAction.IsVisible = !string.IsNullOrWhiteSpace(TrailingActionText);
        _actionsRow.IsVisible = _noteAction.IsVisible || _trailingAction.IsVisible;
    }

    private void ApplySentCue()
    {
        _statusCue.IsVisible = IsSent;
        if (IsSent)
        {
            _border.Use(Border.BackgroundColorProperty, "OwSuccessSoft");
            _border.Use(Border.StrokeProperty, "OwSuccessSoftBorder");
        }
        else
        {
            _border.Use(Border.BackgroundColorProperty, "OwSurface");
            _border.Use(Border.StrokeProperty, "OwBorder");
        }
    }

    private static Border CreateLineChip(Label label, bool secondary)
    {
        label.Use(Label.TextColorProperty, secondary ? "OwPrimaryPressed" : "OwTextSecondary");
        var border = new Border
        {
            StrokeThickness = 1,
            Padding = new Thickness(6, 0),
            HeightRequest = 26,
            Content = label,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            IsVisible = false,
            VerticalOptions = LayoutOptions.Center
        };
        if (secondary)
        {
            border.Use(Border.BackgroundColorProperty, "OwPrimarySoft");
            border.Use(Border.StrokeProperty, "OwPrimarySoftBorder");
        }
        else
        {
            border.Use(Border.BackgroundColorProperty, "OwWarningSoft");
            border.Use(Border.StrokeProperty, "OwWarningBorder");
            label.Use(Label.TextColorProperty, "OwWarningText");
        }

        return border;
    }

    private static string FormatMoney(decimal value) =>
        value.ToString("C2", CultureInfo.GetCultureInfo("en-GB"));
}

/// <summary>Primary / secondary / destructive / utility action button for Order Place.</summary>
public sealed class PosActionButton : ContentView
{
    private readonly Border _border;
    private readonly Label _label;

    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(PosActionButton), string.Empty,
            propertyChanged: (b, _, v) => ((PosActionButton)b)._label.Text = v?.ToString() ?? string.Empty);

    public static readonly BindableProperty RoleProperty =
        BindableProperty.Create(nameof(Role), typeof(PosActionRole), typeof(PosActionButton), PosActionRole.Primary,
            propertyChanged: (b, _, _) => ((PosActionButton)b).ApplyRole());

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(PosActionButton));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(PosActionButton));

    public static readonly BindableProperty IsActionEnabledProperty =
        BindableProperty.Create(nameof(IsActionEnabled), typeof(bool), typeof(PosActionButton), true,
            propertyChanged: (b, _, _) => ((PosActionButton)b).ApplyEnabled());

    public PosActionButton()
    {
        _label = new Label
        {
            FontAttributes = FontAttributes.Bold,
            FontSize = 15,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        _border = new Border
        {
            StrokeThickness = 0,
            Padding = new Thickness(12, 0),
            Content = _label,
            StrokeShape = new RoundRectangle { CornerRadius = 12 }
        };
        _border.Use(Border.MinimumHeightRequestProperty, "OpTouchComfort");

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (!IsActionEnabled)
            {
                return;
            }

            _ = OrderPlaceLayout.FlashPressAsync(this, milliseconds: 90, pressedOpacity: 0.75);
            Tapped?.Invoke(this, EventArgs.Empty);
            if (Command?.CanExecute(CommandParameter) == true)
            {
                Command.Execute(CommandParameter);
            }
        };
        _border.GestureRecognizers.Add(tap);
        Content = _border;
        ApplyRole();
        ApplyEnabled();
    }

    public event EventHandler? Tapped;
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public PosActionRole Role { get => (PosActionRole)GetValue(RoleProperty); set => SetValue(RoleProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    public bool IsActionEnabled { get => (bool)GetValue(IsActionEnabledProperty); set => SetValue(IsActionEnabledProperty, value); }

    private void ApplyRole()
    {
        var heightKey = Role is PosActionRole.Primary or PosActionRole.Payment
            ? "OpPrimaryActionHeight"
            : "OpSecondaryActionHeight";
        _border.Use(Border.HeightRequestProperty, heightKey);
        _border.StrokeThickness = 0;

        switch (Role)
        {
            case PosActionRole.Payment:
                _border.Use(Border.BackgroundColorProperty, "OwWarningStrong");
                _label.Use(Label.TextColorProperty, "OwTextOnPrimary");
                _label.FontSize = 16;
                break;
            case PosActionRole.Primary:
                _border.Use(Border.BackgroundColorProperty, "OwSuccessStrong");
                _label.Use(Label.TextColorProperty, "OwTextOnPrimary");
                _label.FontSize = 15;
                break;
            case PosActionRole.Secondary:
                _border.Use(Border.BackgroundColorProperty, "OwPrimarySoft");
                _border.StrokeThickness = 1;
                _border.Use(Border.StrokeProperty, "OwPrimarySoftBorder");
                _label.Use(Label.TextColorProperty, "OwPrimaryPressed");
                _label.FontSize = 14;
                break;
            case PosActionRole.Destructive:
                _border.Use(Border.BackgroundColorProperty, "OwError");
                _label.Use(Label.TextColorProperty, "OwTextOnPrimary");
                _label.FontSize = 14;
                break;
            default:
                _border.Use(Border.BackgroundColorProperty, "OwSurfaceStrong");
                _label.Use(Label.TextColorProperty, "OwTextSecondary");
                _label.FontSize = 14;
                break;
        }
    }

    private void ApplyEnabled()
    {
        Opacity = IsActionEnabled ? 1 : ControlResources.Value("PosDisabledOpacity", 0.45);
        InputTransparent = !IsActionEnabled;
    }
}

public enum PosActionRole
{
    Primary,
    Payment,
    Secondary,
    Destructive,
    Utility
}

/// <summary>Single-row horizontal chip scroller — never wraps to multiple rows.</summary>
public sealed class HorizontalChipScroller : ContentView
{
    private readonly HorizontalStackLayout _row;

    public HorizontalChipScroller()
    {
        _row = new HorizontalStackLayout
        {
            Spacing = OrderPlaceLayout.Token("OpActionGap", 10),
            Padding = new Thickness(0, 2)
        };

        Content = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _row
        };
    }

    public void SetChips(IEnumerable<View> chips)
    {
        _row.Children.Clear();
        foreach (var chip in chips)
        {
            _row.Children.Add(chip);
        }
    }

    public void Clear() => _row.Children.Clear();

    public IList<IView> Chips => _row.Children;
}

/// <summary>Subtotal / optional SC / optional delivery fee / TOTAL.</summary>
public sealed class OrderTotalsBlock : ContentView
{
    private readonly Label _subtotal;
    private readonly Label _service;
    private readonly Label _fee;
    private readonly Label _total;
    private readonly Grid _serviceRow;
    private readonly Grid _feeRow;

    public static readonly BindableProperty SubtotalProperty =
        BindableProperty.Create(nameof(Subtotal), typeof(decimal), typeof(OrderTotalsBlock), 0m,
            propertyChanged: (b, _, v) => ((OrderTotalsBlock)b)._subtotal.Text = Money((decimal)v));

    public static readonly BindableProperty ServiceChargeProperty =
        BindableProperty.Create(nameof(ServiceCharge), typeof(decimal), typeof(OrderTotalsBlock), 0m,
            propertyChanged: (b, _, v) => ((OrderTotalsBlock)b)._service.Text = Money((decimal)v));

    public static readonly BindableProperty DeliveryFeeProperty =
        BindableProperty.Create(nameof(DeliveryFee), typeof(decimal), typeof(OrderTotalsBlock), 0m,
            propertyChanged: (b, _, v) => ((OrderTotalsBlock)b)._fee.Text = Money((decimal)v));

    public static readonly BindableProperty TotalProperty =
        BindableProperty.Create(nameof(Total), typeof(decimal), typeof(OrderTotalsBlock), 0m,
            propertyChanged: (b, _, v) => ((OrderTotalsBlock)b)._total.Text = Money((decimal)v));

    public static readonly BindableProperty ShowServiceChargeProperty =
        BindableProperty.Create(nameof(ShowServiceCharge), typeof(bool), typeof(OrderTotalsBlock), true,
            propertyChanged: (b, _, v) => ((OrderTotalsBlock)b)._serviceRow.IsVisible = (bool)v);

    public static readonly BindableProperty ShowDeliveryFeeProperty =
        BindableProperty.Create(nameof(ShowDeliveryFee), typeof(bool), typeof(OrderTotalsBlock), false,
            propertyChanged: (b, _, v) => ((OrderTotalsBlock)b)._feeRow.IsVisible = (bool)v);

    public OrderTotalsBlock()
    {
        _subtotal = MoneyLabel(false);
        _service = MoneyLabel(false);
        _fee = MoneyLabel(false);
        _total = MoneyLabel(true);
        _total.Use(Label.FontSizeProperty, "OpFontTotal");
        _total.Use(Label.TextColorProperty, "OwSuccessStrong");

        _serviceRow = MoneyRow("Service charge", _service);
        _feeRow = MoneyRow("Delivery fee", _fee);
        _feeRow.IsVisible = false;

        var divider = new BoxView { HeightRequest = 1 };
        divider.Use(BoxView.ColorProperty, "OwBorder");

        Content = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                MoneyRow("Subtotal", _subtotal),
                _serviceRow,
                _feeRow,
                divider,
                MoneyRow("TOTAL", _total)
            }
        };
    }

    public decimal Subtotal { get => (decimal)GetValue(SubtotalProperty); set => SetValue(SubtotalProperty, value); }
    public decimal ServiceCharge { get => (decimal)GetValue(ServiceChargeProperty); set => SetValue(ServiceChargeProperty, value); }
    public decimal DeliveryFee { get => (decimal)GetValue(DeliveryFeeProperty); set => SetValue(DeliveryFeeProperty, value); }
    public decimal Total { get => (decimal)GetValue(TotalProperty); set => SetValue(TotalProperty, value); }
    public bool ShowServiceCharge { get => (bool)GetValue(ShowServiceChargeProperty); set => SetValue(ShowServiceChargeProperty, value); }
    public bool ShowDeliveryFee { get => (bool)GetValue(ShowDeliveryFeeProperty); set => SetValue(ShowDeliveryFeeProperty, value); }

    private static Grid MoneyRow(string label, Label value)
    {
        var name = new Label { Text = label, FontAttributes = FontAttributes.Bold, FontSize = 12 };
        name.Use(Label.TextColorProperty, "OwTextMuted");
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        grid.Add(name);
        grid.Add(value, 1);
        return grid;
    }

    private static Label MoneyLabel(bool emphasize)
    {
        var label = new Label
        {
            FontAttributes = FontAttributes.Bold,
            FontSize = emphasize ? 16 : 12,
            HorizontalTextAlignment = TextAlignment.End,
            Text = Money(0)
        };
        label.Use(Label.TextColorProperty, emphasize ? "OwSuccessStrong" : "OwTextPrimary");
        return label;
    }

    private static string Money(decimal value) =>
        value.ToString("C2", CultureInfo.GetCultureInfo("en-GB"));
}

/// <summary>Adaptive product grid — columns from available width / min card width.</summary>
public sealed class OrderPlaceProductGrid : ContentView
{
    private readonly Grid _grid;
    private readonly List<View> _items = [];
    private int _columns = 4;
    private int _laidOutColumnCount = -1;
    private int _laidOutItemCount = -1;

    public OrderPlaceProductGrid()
    {
        _grid = new Grid
        {
            ColumnSpacing = OrderPlaceLayout.Token("OpProductCardGap", 10),
            RowSpacing = OrderPlaceLayout.Token("OpProductCardGap", 10)
        };
        Content = _grid;
        SizeChanged += (_, _) => Relayout(force: false);
    }

    public int ColumnCount => _columns;

    public void SetProducts(IEnumerable<OrderPlaceProductCard> cards) =>
        SetItems(cards);

    public void SetItems(IEnumerable<View> items)
    {
        var next = items as IList<View> ?? items.ToList();
        var sameViews = _items.Count == next.Count;
        if (sameViews)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (!ReferenceEquals(_items[i], next[i]))
                {
                    sameViews = false;
                    break;
                }
            }
        }

        if (!sameViews)
        {
            _items.Clear();
            _items.AddRange(next);
        }

        Relayout(force: !sameViews);
    }

    public void Clear() => SetItems([]);

    private void Relayout(bool force)
    {
        var width = Width > 0 ? Width : 800;
        var columns = OrderPlaceLayout.ProductColumnCount(width);
        if (!force
            && columns == _laidOutColumnCount
            && _items.Count == _laidOutItemCount
            && _grid.Children.Count == _items.Count)
        {
            _columns = columns;
            return;
        }

        _columns = columns;
        _laidOutColumnCount = columns;
        _laidOutItemCount = _items.Count;
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _grid.RowDefinitions.Clear();
        for (var c = 0; c < _columns; c++)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        for (var i = 0; i < _items.Count; i++)
        {
            if (i % _columns == 0)
            {
                _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            var item = _items[i];
            item.HorizontalOptions = LayoutOptions.Fill;
            item.VerticalOptions = LayoutOptions.Fill;
            _grid.Add(item, i % _columns, i / _columns);
        }
    }
}
