using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.Controls.OrderPlace;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Story-style host for Order Place SharedUI bricks (Phase 1).
/// No order logic — sample data only for layout/density review.
/// </summary>
public sealed class OrderPlaceSandboxView : ContentView
{
    private readonly Label _densityLabel;
    private readonly HorizontalChipScroller _categories;
    private readonly HorizontalChipScroller _subcategories;
    private readonly OrderPlaceProductGrid _products;
    private readonly VerticalStackLayout _lines;
    private readonly OrderTotalsBlock _totals;
    private string _selectedCategory = "MAIN";
    private string _selectedSub = "All";

    public OrderPlaceSandboxView()
    {
        _densityLabel = new Label { FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
        _densityLabel.Use(Label.TextColorProperty, "OwTextMuted");

        _categories = new HorizontalChipScroller();
        _subcategories = new HorizontalChipScroller();
        _products = new OrderPlaceProductGrid();
        _lines = new VerticalStackLayout { Spacing = 8 };
        _totals = new OrderTotalsBlock
        {
            Subtotal = 32.00m,
            ServiceCharge = 3.20m,
            Total = 35.20m,
            ShowServiceCharge = true,
            ShowDeliveryFee = false
        };

        var searchPlaceholder = new Label
        {
            Text = "Search menu items...",
            VerticalTextAlignment = TextAlignment.Center,
            FontSize = 15
        };
        searchPlaceholder.Use(Label.TextColorProperty, "OwTextPlaceholder");

        var search = new Border
        {
            StrokeThickness = 1,
            HeightRequest = 48,
            Padding = new Thickness(14, 0),
            Content = searchPlaceholder,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 }
        };
        search.Use(Border.BackgroundColorProperty, "OwSurface");
        search.Use(Border.StrokeProperty, "OwBorder");

        var left = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            RowSpacing = OrderPlaceLayout.Token("OpSectionGap", 14),
            Padding = new Thickness(16, 12, 10, 12)
        };
        left.Add(_densityLabel);
        left.Add(search, 0, 1);
        left.Add(_categories, 0, 2);
        left.Add(_subcategories, 0, 3);
        left.Add(new ScrollView { Content = _products }, 0, 4);
        left.Use(Grid.BackgroundColorProperty, "OwBackground");

        var header = new Label
        {
            Text = "TABLE 10 • 5 GUESTS",
            FontAttributes = FontAttributes.Bold,
            FontSize = 16
        };
        header.Use(Label.TextColorProperty, "OwTextStrong");

        var notes = new PosActionButton { Text = "NOTES", Role = PosActionRole.Secondary };
        var voidBtn = new PosActionButton { Text = "VOID", Role = PosActionRole.Destructive };
        var more = new PosActionButton { Text = "MORE ▼", Role = PosActionRole.Utility };
        var send = new PosActionButton { Text = "SEND TO KITCHEN", Role = PosActionRole.Primary };
        var print = new PosActionButton { Text = "PRINT", Role = PosActionRole.Secondary };
        var pay = new PosActionButton { Text = "PAYMENT £35.20", Role = PosActionRole.Payment };

        var utilityRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8
        };
        utilityRow.Add(notes);
        utilityRow.Add(voidBtn, 1);
        utilityRow.Add(more, 2);

        var kitchenRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1.4, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8
        };
        kitchenRow.Add(send);
        kitchenRow.Add(print, 1);

        var actions = new VerticalStackLayout
        {
            Spacing = 8,
            Children = { _totals, utilityRow, kitchenRow, pay }
        };

        var right = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 10,
            Padding = new Thickness(10, 12, 16, 12)
        };
        right.Use(Grid.BackgroundColorProperty, "OwSurface");
        right.Add(header);
        right.Add(new ScrollView { Content = _lines }, 0, 1);
        right.Add(actions, 0, 2);

        var root = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(7, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(3, GridUnitType.Star))
            }
        };
        root.Use(Grid.BackgroundColorProperty, "OwBackground");
        root.Add(left);
        root.Add(right, 1);
        root.SizeChanged += (_, _) => UpdateDensityLabel(root.Width);

        Content = root;
        RebuildCategories();
        RebuildSubcategories();
        RebuildProducts();
        RebuildLines();
        UpdateDensityLabel(1200);
    }

    private void RebuildCategories()
    {
        var names = new[] { "MAIN", "STARTERS", "DRINKS", "DESSERT", "SIDES", "MEAL DEALS", "TASTING" };
        _categories.SetChips(names.Select(name =>
        {
            var chip = new OrderPlaceCategoryButton
            {
                Text = name,
                IsSelected = name == _selectedCategory,
                Command = new Command(() =>
                {
                    _selectedCategory = name;
                    RebuildCategories();
                })
            };
            return (View)chip;
        }));
    }

    private void RebuildSubcategories()
    {
        var names = new[] { "All", "Veg", "Meat", "Chicken", "Fish", "Rice", "Curry", "Grill" };
        _subcategories.SetChips(names.Select(name =>
        {
            var chip = new OrderPlaceSubcategoryButton
            {
                Text = name,
                IsSelected = name == _selectedSub,
                Command = new Command(() =>
                {
                    _selectedSub = name;
                    RebuildSubcategories();
                })
            };
            return (View)chip;
        }));
    }

    private void RebuildProducts()
    {
        var sample = new (string Name, decimal Price)[]
        {
            ("Chicken Biryani", 12.50m),
            ("Lamb Curry", 11.50m),
            ("Chicken Tikka", 10.50m),
            ("Burger", 8.50m),
            ("Veg Biryani", 9.50m),
            ("Lamb Chops", 14.00m),
            ("Salmon", 13.00m),
            ("Pasta", 9.00m),
            ("Rice", 4.00m),
            ("Wings", 7.50m),
            ("Pizza", 11.00m),
            ("Fish & Chips", 11.50m),
            ("Wrap", 8.00m),
            ("Steak", 16.00m),
            ("Garlic Bread", 4.50m),
            ("Coke", 3.00m)
        };

        _products.SetProducts(sample.Select(item => new OrderPlaceProductCard
        {
            Name = item.Name,
            Price = item.Price,
            Command = new Command(() => { })
        }));
    }

    private void RebuildLines()
    {
        _lines.Children.Clear();
        _lines.Children.Add(new OrderPlaceLineRow
        {
            ItemName = "Chicken Biryani",
            Details = "No onion",
            Quantity = 2,
            LineTotal = 25.00m
        });
        _lines.Children.Add(new OrderPlaceLineRow
        {
            ItemName = "Coke",
            Quantity = 1,
            LineTotal = 3.00m
        });
        _lines.Children.Add(new OrderPlaceLineRow
        {
            ItemName = "Garlic Bread",
            Quantity = 1,
            LineTotal = 4.50m
        });
    }

    private void UpdateDensityLabel(double pageWidth)
    {
        var menuWidth = pageWidth * 0.70;
        var columns = OrderPlaceLayout.ProductColumnCount(Math.Max(0, menuWidth - 40));
        var band = pageWidth >= 1400 ? "≈15–15.5″ wide" : pageWidth >= 1100 ? "≈14–15″" : "narrow / scaled";
        _densityLabel.Text =
            $"Order Place sandbox • {band} • menu≈{menuWidth:0}px → {columns} product columns (min card width token)";
    }
}
