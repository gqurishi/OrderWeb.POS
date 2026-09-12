using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>Mother Rider card. Hosts map their own data into this; SharedUI only draws.</summary>
public sealed class RiderBoardCard
{
    public string Id { get; init; } = string.Empty;
    public string OrderNumber { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string TimeText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string StatusColor { get; init; } = "#D97706";
    public string CustomerName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string TotalText { get; init; } = string.Empty;
    public string PaymentText { get; init; } = string.Empty;
    public string CollectText { get; init; } = string.Empty;
    public string QuoteText { get; init; } = string.Empty;
    public bool HasRider { get; init; }
    public string RiderName { get; init; } = string.Empty;
    public string RiderPhone { get; init; } = string.Empty;
    public string ErrorText { get; init; } = string.Empty;
    public string ActionText { get; init; } = "Request Rider";
    public bool ActionEnabled { get; init; }
    public bool IsConfirm { get; init; }
    public bool IsProblem { get; init; }
}

public sealed class RiderBoardActionEventArgs : EventArgs
{
    public required RiderBoardCard Card { get; init; }
}

/// <summary>
/// Mother Rider look: operational day, filter chips, delivery cards.
/// Hosts own load, quote, and confirm.
/// </summary>
public sealed class RiderBoardView : ContentView
{
    private static readonly (string Key, string Title)[] Filters =
    [
        ("all", "All"),
        ("kitchen", "Kitchen"),
        ("ready", "Ready"),
        ("quote", "Quote"),
        ("active", "Active rider"),
        ("delivered", "Delivered"),
        ("problems", "Problems")
    ];

    private readonly Label _dayLabel = new()
    {
        FontSize = 13,
        TextColor = Color.FromArgb("#64748B"),
        Text = "Operational day"
    };
    private readonly HorizontalStackLayout _filters = new() { Spacing = 10 };
    private readonly CollectionView _orders = new() { SelectionMode = SelectionMode.None };
    private readonly ChefLoaderView _loader = new()
    {
        Mode = ChefLoaderMode.Overlay,
        Size = ChefLoaderSize.Md,
        Message = "Cooking up your data…",
        DelayMilliseconds = 0,
        IsLoading = false,
        HorizontalOptions = LayoutOptions.Fill,
        VerticalOptions = LayoutOptions.Fill
    };

    private IReadOnlyList<RiderBoardCard> _cards = Array.Empty<RiderBoardCard>();
    private string _filter = "all";

    public RiderBoardView()
    {
        BackgroundColor = Color.FromArgb("#F5F7FB");
        _orders.ItemsLayout = new GridItemsLayout(2, ItemsLayoutOrientation.Vertical)
        {
            HorizontalItemSpacing = 14,
            VerticalItemSpacing = 14
        };
        _orders.EmptyView = BuildEmpty();
        _orders.ItemTemplate = new DataTemplate(() => BuildCardShell());

        foreach (var filter in Filters)
        {
            var button = new Button
            {
                CommandParameter = filter.Key,
                CornerRadius = 10,
                Padding = new Thickness(18, 9),
                FontAttributes = FontAttributes.Bold,
                FontSize = 13
            };
            button.Clicked += (_, _) =>
            {
                _filter = filter.Key;
                ApplyFilter();
            };
            _filters.Add(button);
        }

        var header = new Grid
        {
            Padding = new Thickness(20, 16, 20, 8),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 16
        };
        header.Add(new VerticalStackLayout
        {
            Spacing = 3,
            Children =
            {
                new Label
                {
                    Text = "Delivery operations",
                    FontSize = 24,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#0F172A")
                },
                _dayLabel
            }
        });
        var refresh = new Button
        {
            Text = "Refresh",
            BackgroundColor = Color.FromArgb("#E2E8F0"),
            TextColor = Color.FromArgb("#0F172A"),
            CornerRadius = 10,
            Padding = new Thickness(22, 10),
            FontAttributes = FontAttributes.Bold
        };
        refresh.Clicked += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        header.Add(refresh, 1, 0);

        var filterScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Padding = new Thickness(20, 8, 20, 14),
            Content = _filters
        };

        var board = new Grid
        {
            Padding = new Thickness(20, 0, 20, 18),
            Children = { _orders, _loader }
        };

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            Children = { header, filterScroll, board }
        };
        Grid.SetRow(filterScroll, 1);
        Grid.SetRow(board, 2);
        ApplyFilter();
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler<RiderBoardActionEventArgs>? ActionRequested;

    public void SetDayText(string text) => _dayLabel.Text = text;

    public void SetLoading(bool loading) => _loader.IsLoading = loading;

    public void SetCards(IReadOnlyList<RiderBoardCard>? cards)
    {
        _cards = cards ?? Array.Empty<RiderBoardCard>();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var visible = _cards.Where(Matches).ToList();
        _orders.ItemsSource = visible;
        foreach (var button in _filters.Children.OfType<Button>())
        {
            var key = button.CommandParameter?.ToString() ?? "all";
            var title = Filters.First(f => f.Key == key).Title;
            var count = _cards.Count(card => key == "all" || Matches(card, key));
            button.Text = $"{title}  {count}";
            var active = key == _filter;
            button.BackgroundColor = Color.FromArgb(active ? "#0F766E" : "#E2E8F0");
            button.TextColor = Color.FromArgb(active ? "#FFFFFF" : "#334155");
        }
    }

    private bool Matches(RiderBoardCard card) => Matches(card, _filter);

    private static bool Matches(RiderBoardCard card, string filter) => filter switch
    {
        "kitchen" => card.StatusText == "Awaiting kitchen",
        "ready" => card.StatusText == "Ready for rider",
        "quote" => card.StatusText is "Quote available" or "Requesting quote",
        "active" => card.StatusText is "Finding rider" or "Rider assigned" or "Collected" or "Delivering",
        "delivered" => card.StatusText == "Delivered",
        "problems" => card.IsProblem,
        _ => true
    };

    private static View BuildEmpty() => new VerticalStackLayout
    {
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center,
        Spacing = 8,
        Padding = 30,
        Children =
        {
            new Label
            {
                Text = "No delivery operations",
                FontSize = 20,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#0F172A"),
                HorizontalTextAlignment = TextAlignment.Center
            },
            new Label
            {
                Text = "Committed web and local delivery orders will appear here automatically.",
                FontSize = 13,
                TextColor = Color.FromArgb("#64748B"),
                HorizontalTextAlignment = TextAlignment.Center
            }
        }
    };

    private View BuildCardShell()
    {
        var orderNumber = BoundLabel(nameof(RiderBoardCard.OrderNumber), 18, "#0F172A", bold: true);
        var source = BoundLabel(nameof(RiderBoardCard.Source), 11, "#1D4ED8", bold: true);
        var sourceChip = new Border
        {
            BackgroundColor = Color.FromArgb("#EFF6FF"),
            StrokeThickness = 0,
            Padding = new Thickness(8, 3),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = source,
            VerticalOptions = LayoutOptions.Center
        };
        var time = BoundLabel(nameof(RiderBoardCard.TimeText), 12, "#64748B");
        var status = BoundLabel(nameof(RiderBoardCard.StatusText), 11, "#FFFFFF", bold: true);
        var statusChip = new Border
        {
            StrokeThickness = 0,
            Padding = new Thickness(10, 6),
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = status,
            VerticalOptions = LayoutOptions.Start
        };
        statusChip.SetBinding(Border.BackgroundColorProperty, new Binding(nameof(RiderBoardCard.StatusColor), converter: ColorHexConverter.Instance));

        var name = BoundLabel(nameof(RiderBoardCard.CustomerName), 15, "#1E293B", bold: true);
        var phone = BoundLabel(nameof(RiderBoardCard.Phone), 13, "#475569");
        var address = BoundLabel(nameof(RiderBoardCard.Address), 13, "#475569");
        address.MaxLines = 2;
        address.LineBreakMode = LineBreakMode.TailTruncation;

        var totalBox = MoneyBox("ORDER TOTAL", nameof(RiderBoardCard.TotalText), nameof(RiderBoardCard.PaymentText), "#F8FAFC", "#64748B", "#0F172A", "#475569");
        var collectBox = MoneyBox("RIDER COLLECTION", nameof(RiderBoardCard.CollectText), nameof(RiderBoardCard.QuoteText), "#FFF7ED", "#9A3412", "#C2410C", "#9A3412", quotePrefix: true);

        var riderBlock = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                Caption("RIDER"),
                BoundLabel(nameof(RiderBoardCard.RiderName), 13, "#1E293B", bold: true),
                BoundLabel(nameof(RiderBoardCard.RiderPhone), 12, "#475569")
            }
        };
        riderBlock.SetBinding(IsVisibleProperty, nameof(RiderBoardCard.HasRider));

        var error = BoundLabel(nameof(RiderBoardCard.ErrorText), 11, "#DC2626");
        error.MaxLines = 2;
        var action = new Button
        {
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 10,
            Padding = new Thickness(18, 10)
        };
        action.SetBinding(Button.TextProperty, nameof(RiderBoardCard.ActionText));
        action.SetBinding(Button.IsEnabledProperty, nameof(RiderBoardCard.ActionEnabled));
        action.SetBinding(Button.BackgroundColorProperty, new Binding(nameof(RiderBoardCard.IsConfirm), converter: ActionColorConverter.Instance));
        action.Clicked += (_, _) =>
        {
            if (action.BindingContext is RiderBoardCard card && card.ActionEnabled)
            {
                ActionRequested?.Invoke(this, new RiderBoardActionEventArgs { Card = card });
            }
        };

        var cardGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            RowSpacing = 11,
            ColumnSpacing = 12
        };
        cardGrid.Add(new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new HorizontalStackLayout { Spacing = 8, Children = { orderNumber, sourceChip } },
                time
            }
        });
        cardGrid.Add(statusChip, 1, 0);
        var customer = new VerticalStackLayout { Spacing = 3, Children = { name, phone, address } };
        cardGrid.Add(customer, 0, 1);
        Grid.SetColumnSpan(customer, 2);
        var money = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10,
            Children = { totalBox, collectBox }
        };
        Grid.SetColumn(collectBox, 1);
        cardGrid.Add(money, 0, 2);
        Grid.SetColumnSpan(money, 2);
        cardGrid.Add(riderBlock, 0, 3);
        Grid.SetColumnSpan(riderBlock, 2);
        var actions = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 10,
            Children = { error, action }
        };
        Grid.SetColumn(action, 1);
        cardGrid.Add(actions, 0, 4);
        Grid.SetColumnSpan(actions, 2);

        return new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            Padding = 16,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = cardGrid
        };
    }

    private static Border MoneyBox(
        string caption,
        string valuePath,
        string detailPath,
        string background,
        string captionColor,
        string valueColor,
        string detailColor,
        bool quotePrefix = false)
    {
        var detail = BoundLabel(detailPath, 11, detailColor);
        if (quotePrefix)
        {
            detail.SetBinding(Label.TextProperty, new Binding(detailPath, stringFormat: "Quote: {0}"));
        }

        return new Border
        {
            BackgroundColor = Color.FromArgb(background),
            StrokeThickness = 0,
            Padding = new Thickness(10, 8),
            StrokeShape = new RoundRectangle { CornerRadius = 9 },
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label
                    {
                        Text = caption,
                        FontSize = 10,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb(captionColor)
                    },
                    BoundLabel(valuePath, 16, valueColor, bold: true),
                    detail
                }
            }
        };
    }

    private static Label Caption(string text) => new()
    {
        Text = text,
        FontSize = 10,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#64748B")
    };

    private static Label BoundLabel(string path, double size, string color, bool bold = false) => new Label()
    {
        FontSize = size,
        FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
        TextColor = Color.FromArgb(color)
    }.Also(label => label.SetBinding(Label.TextProperty, path));

    private sealed class ColorHexConverter : IValueConverter
    {
        public static readonly ColorHexConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            Color.FromArgb(value as string ?? "#D97706");
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class ActionColorConverter : IValueConverter
    {
        public static readonly ActionColorConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is true ? Color.FromArgb("#2563EB") : Color.FromArgb("#0F766E");
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

file static class RiderBoardViewExtensions
{
    public static T Also<T>(this T value, Action<T> apply)
    {
        apply(value);
        return value;
    }
}
