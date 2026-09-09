using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.Controls.OrderPlace;
using OrderWeb.SharedUI.Hosting;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Host-neutral Order Place chrome (70/30, dual scroll, bottom Payment).
/// Mother/Client supply <see cref="IOrderPlaceHost"/> — no HTTP/DB here.
/// </summary>
public sealed class OrderPlaceShellView : ContentView
{
    private readonly HorizontalChipScroller _categories;
    private readonly HorizontalChipScroller _subcategories;
    private readonly Border _subcategoryBand;
    private readonly OrderPlaceProductGrid _products;
    private readonly VerticalStackLayout _lines;
    private readonly Label _identity;
    private readonly Label _detail;
    private readonly Label _status;
    private readonly OrderTotalsBlock _totals;
    private readonly Label _discountLabel;
    private readonly Grid _discountRow;
    private readonly PosActionButton _notes;
    private readonly PosActionButton _void;
    private readonly PosActionButton _more;
    private readonly PosActionButton _send;
    private readonly PosActionButton _print;
    private readonly PosActionButton _pay;
    private readonly Grid _root;
    private IOrderPlaceHost? _host;
    private string? _categoriesFingerprint;
    private string? _subcategoriesFingerprint;
    private string? _productsFingerprint;
    private string? _linesFingerprint;

    public OrderPlaceShellView()
    {
        _categories = new HorizontalChipScroller { HeightRequest = 56 };
        _subcategories = new HorizontalChipScroller { HeightRequest = 56 };
        _products = new OrderPlaceProductGrid();

        var subBandPadding = OrderPlaceLayout.Token("OpSubcategoryBandPadding", 10);
        _subcategoryBand = new Border
        {
            StrokeThickness = 1,
            Padding = new Thickness(subBandPadding, 8),
            Margin = new Thickness(0, 6, 0, 2),
            IsVisible = false,
            Content = _subcategories,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = ControlResources.Value("OpSubcategoryBandCornerRadius", 8)
            }
        };
        _subcategoryBand.Use(Border.BackgroundColorProperty, "OwSurfaceMuted");
        _subcategoryBand.Use(Border.StrokeProperty, "OwBorder");

        var left = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            RowSpacing = OrderPlaceLayout.Token("OpSectionGap", 14),
            Padding = new Thickness(12)
        };
        left.Add(_categories);
        left.Add(_subcategoryBand, 0, 1);
        left.Add(new ScrollView { Content = _products }, 0, 2);
        left.Use(Grid.BackgroundColorProperty, "OwSurface");

        var leftBorder = WrapPanel(left);

        _identity = new Label { FontAttributes = FontAttributes.Bold, FontSize = 17, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        _identity.Use(Label.TextColorProperty, "OwTextStrong");
        _detail = new Label { FontAttributes = FontAttributes.Bold, FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        _detail.Use(Label.TextColorProperty, "OwTextMuted");
        _status = new Label { FontSize = 12, IsVisible = false, LineBreakMode = LineBreakMode.WordWrap };
        _status.Use(Label.TextColorProperty, "OwWarningText");

        _notes = new PosActionButton { Text = "NOTES", Role = PosActionRole.Secondary };
        _void = new PosActionButton { Text = "VOID", Role = PosActionRole.Destructive };
        _more = new PosActionButton { Text = "MORE ▼", Role = PosActionRole.Utility };
        _send = new PosActionButton { Text = "SEND TO KITCHEN", Role = PosActionRole.Primary };
        _print = new PosActionButton { Text = "PRINT BILL", Role = PosActionRole.Secondary };
        _pay = new PosActionButton
        {
            Text = "PAYMENT",
            Role = PosActionRole.Payment,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
            MinimumWidthRequest = 130
        };

        _notes.Tapped += async (_, _) => { if (_host != null) await _host.OrderNotesAsync(); };
        _void.Tapped += async (_, _) => { if (_host != null) await _host.VoidAsync(); };
        _more.Tapped += async (_, _) => { if (_host != null) await _host.MoreAsync(); };
        _send.Tapped += async (_, _) => { if (_host != null) await _host.SendAsync(); };
        _print.Tapped += async (_, _) => { if (_host != null) await _host.PrintAsync(); };
        _pay.Tapped += async (_, _) => { if (_host != null) await _host.PayAsync(); };

        var headerText = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center, Children = { _identity, _detail, _status } };
        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        headerGrid.Add(headerText);
        headerGrid.Add(_pay, 1);

        var header = new Border
        {
            StrokeThickness = 1,
            Padding = new Thickness(12, 10),
            Content = headerGrid,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }
        };
        header.Use(Border.BackgroundColorProperty, "OwSurfaceMuted");
        header.Use(Border.StrokeProperty, "OwBorder");

        _lines = new VerticalStackLayout { Spacing = 4 };
        _totals = new OrderTotalsBlock();

        _discountLabel = new Label { FontAttributes = FontAttributes.Bold, FontSize = 13, HorizontalTextAlignment = TextAlignment.End };
        _discountLabel.Use(Label.TextColorProperty, "OwErrorStrong");
        var discountName = new Label { Text = "Discount", FontAttributes = FontAttributes.Bold, FontSize = 13 };
        discountName.Use(Label.TextColorProperty, "OwTextMuted");
        _discountRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            IsVisible = false,
            Children = { discountName }
        };
        _discountRow.Add(_discountLabel, 1);

        var quick = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8
        };
        quick.Add(_notes);
        quick.Add(_void, 1);
        quick.Add(_more, 2);

        var main = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1.4, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8
        };
        main.Add(_send);
        main.Add(_print, 1);

        var bottom = new VerticalStackLayout
        {
            Spacing = 8,
            Children = { _discountRow, _totals, quick, main }
        };

        var right = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 8,
            Padding = new Thickness(12)
        };
        right.Add(header);
        right.Add(new ScrollView { Content = _lines }, 0, 1);
        right.Add(bottom, 0, 2);
        right.Use(Grid.BackgroundColorProperty, "OwSurface");

        var rightBorder = WrapPanel(right);

        _root = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(7, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(3, GridUnitType.Star))
            },
            ColumnSpacing = 12,
            Padding = new Thickness(12)
        };
        _root.Add(leftBorder);
        _root.Add(rightBorder, 1);
        Content = _root;
        SizeChanged += (_, _) => ApplyResponsiveLayout();
    }

    public void BindHost(IOrderPlaceHost host)
    {
        if (_host != null)
        {
            _host.StateChanged -= OnHostStateChanged;
        }

        _host = host;
        _host.StateChanged += OnHostStateChanged;
        Render();
    }

    private void OnHostStateChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(Render);

    private void Render()
    {
        if (_host is null)
        {
            return;
        }

        var session = _host.Session;

        _identity.Text = session.HeaderTitle;
        _detail.Text = session.HeaderDetail;
        _detail.IsVisible = !string.IsNullOrWhiteSpace(session.HeaderDetail);
        _status.Text = session.StatusMessage ?? string.Empty;
        _status.IsVisible = !string.IsNullOrWhiteSpace(session.StatusMessage);

        _totals.Subtotal = session.Subtotal;
        _totals.ServiceCharge = session.ServiceCharge;
        _totals.DeliveryFee = session.DeliveryFee;
        _totals.Total = session.Total;
        _totals.ShowServiceCharge = session.ShowServiceCharge;
        _totals.ShowDeliveryFee = session.ShowDeliveryFee;

        _discountRow.IsVisible = session.Discount > 0;
        _discountLabel.Text = $"-£{session.Discount:F2}";

        _print.Text = session.PrintActionLabel;
        _pay.Text = session.PaymentActionLabel;
        _send.Text = string.IsNullOrWhiteSpace(session.SendActionLabel)
            ? "SEND TO KITCHEN"
            : session.SendActionLabel;

        var actionsEnabled = !session.ActionsBusy && !session.IsBusy;
        _pay.IsActionEnabled = actionsEnabled;
        _send.IsActionEnabled = actionsEnabled;
        _print.IsActionEnabled = actionsEnabled;
        _void.IsActionEnabled = actionsEnabled;

        var categoriesKey = BuildCategoriesFingerprint(session);
        if (!string.Equals(categoriesKey, _categoriesFingerprint, StringComparison.Ordinal))
        {
            _categoriesFingerprint = categoriesKey;
            RenderCategories(session);
        }

        var subKey = BuildSubcategoriesFingerprint(session);
        if (!string.Equals(subKey, _subcategoriesFingerprint, StringComparison.Ordinal))
        {
            _subcategoriesFingerprint = subKey;
            RenderSubcategories(session);
        }

        var productsKey = BuildProductsFingerprint(session);
        if (!string.Equals(productsKey, _productsFingerprint, StringComparison.Ordinal))
        {
            _productsFingerprint = productsKey;
            RenderProducts(session);
        }

        var linesKey = BuildLinesFingerprint(session);
        if (!string.Equals(linesKey, _linesFingerprint, StringComparison.Ordinal))
        {
            _linesFingerprint = linesKey;
            RenderLines(session);
        }
    }

    private static string BuildCategoriesFingerprint(OrderPlaceSessionState session) =>
        string.Join('|', session.Categories.Select(c => $"{c.Id}:{c.Name}")) +
        $"#{session.SelectedCategoryId}";

    private static string BuildSubcategoriesFingerprint(OrderPlaceSessionState session) =>
        string.Join('|', session.Subcategories.Select(c => $"{c.Id}:{c.Name}")) +
        $"#{session.SelectedSubcategoryId}";

    private static string BuildProductsFingerprint(OrderPlaceSessionState session) =>
        string.Join('|', session.Products.Select(p => $"{p.Id}:{p.Name}:{p.Price:0.00}"));

    private static string BuildLinesFingerprint(OrderPlaceSessionState session) =>
        string.Join('|', session.Lines.Select(l =>
            $"{l.Id}:{l.Quantity}:{l.LineTotal:0.00}:{l.Details}:{l.IsSent}"));

    private void RenderCategories(OrderPlaceSessionState session)
    {
        var chips = new List<View>();
        foreach (var category in session.Categories)
        {
            var chip = new OrderPlaceCategoryButton
            {
                Text = category.Name,
                IsSelected = string.Equals(category.Id, session.SelectedCategoryId, StringComparison.Ordinal),
                CommandParameter = category.Id
            };
            var id = category.Id;
            chip.Tapped += async (_, _) =>
            {
                if (_host != null)
                {
                    await _host.SelectCategoryAsync(id);
                }
            };
            chips.Add(chip);
        }

        _categories.SetChips(chips);
    }

    private void RenderSubcategories(OrderPlaceSessionState session)
    {
        if (session.Subcategories.Count == 0)
        {
            _subcategoryBand.IsVisible = false;
            _subcategories.Clear();
            return;
        }

        _subcategoryBand.IsVisible = true;
        var chips = new List<View>();
        foreach (var category in session.Subcategories)
        {
            var chip = new OrderPlaceSubcategoryButton
            {
                Text = category.Name,
                IsSelected = string.Equals(category.Id, session.SelectedSubcategoryId, StringComparison.Ordinal),
                CommandParameter = category.Id
            };
            var id = category.Id;
            chip.Tapped += async (_, _) =>
            {
                if (_host != null)
                {
                    await _host.SelectSubcategoryAsync(id);
                }
            };
            chips.Add(chip);
        }

        _subcategories.SetChips(chips);
    }

    private void RenderProducts(OrderPlaceSessionState session)
    {
        var cards = new List<View>();
        foreach (var product in session.Products)
        {
            var card = new OrderPlaceProductCard
            {
                Name = product.Name,
                Price = product.Price,
                Badge = product.Badge ?? string.Empty,
                PriceText = product.PriceText ?? string.Empty,
                CommandParameter = product.Id
            };
            var id = product.Id;
            card.Tapped += async (_, _) =>
            {
                if (_host != null)
                {
                    await _host.AddProductAsync(id);
                }
            };
            cards.Add(card);
        }

        _products.SetItems(cards);
    }

    private void RenderLines(OrderPlaceSessionState session)
    {
        _lines.Children.Clear();
        foreach (var line in session.Lines)
        {
            var row = new OrderPlaceLineRow
            {
                ItemName = line.Name,
                LineTotal = line.LineTotal,
                Quantity = line.Quantity,
                Details = line.Details ?? string.Empty,
                IsSent = line.IsSent,
                ShowNoteAction = line.ShowNoteAction,
                NoteActionText = string.IsNullOrWhiteSpace(line.Details) ? "+ Note" : "Note Added",
                TrailingActionText = line.TrailingActionText ?? string.Empty
            };
            var id = line.Id;
            row.QuantityChanged += async (_, quantity) =>
            {
                if (_host != null)
                {
                    await _host.SetLineQuantityAsync(id, quantity);
                }
            };
            row.NoteTapped += async (_, _) =>
            {
                if (_host != null)
                {
                    await _host.EditLineNoteAsync(id);
                }
            };
            if (!string.IsNullOrWhiteSpace(line.TrailingActionText))
            {
                row.TrailingActionTapped += async (_, _) =>
                {
                    if (_host != null)
                    {
                        await _host.TrailingLineActionAsync(id);
                    }
                };
            }

            _lines.Children.Add(row);
        }
    }

    private void ApplyResponsiveLayout()
    {
        // Mother + Client Order Place: always ~70/30 side-by-side (no vertical stack on narrow).
        _root.ColumnDefinitions.Clear();
        _root.RowDefinitions.Clear();
        _root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        _root.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(7, GridUnitType.Star)));
        _root.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star)));
        _root.ColumnSpacing = 12;
        _root.RowSpacing = 0;
        if (_root.Children.Count >= 2)
        {
            Grid.SetRow(_root.Children[0] as View, 0);
            Grid.SetColumn(_root.Children[0] as View, 0);
            Grid.SetRow(_root.Children[1] as View, 0);
            Grid.SetColumn(_root.Children[1] as View, 1);
        }
    }

    private static Border WrapPanel(View content)
    {
        var border = new Border
        {
            StrokeThickness = 1,
            Content = content,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 }
        };
        border.Use(Border.BackgroundColorProperty, "OwSurface");
        border.Use(Border.StrokeProperty, "OwBorder");
        return border;
    }
}
