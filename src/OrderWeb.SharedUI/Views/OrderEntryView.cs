using System.Globalization;
using OrderWeb.Contracts.Orders;
using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.ViewModels;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Shared order-entry surface (Phase 12): categories, product grid, modifiers,
/// basket/lines, quantity, notes, discounts, totals, send, void, manager approval.
/// </summary>
public sealed class OrderEntryView : ContentView
{
    private OrderEntryViewModel? _viewModel;
    private readonly HorizontalStackLayout _categories = new() { Spacing = 10 };
    private readonly FlexLayout _products = new()
    {
        Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
        Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
        JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start
    };
    private readonly VerticalStackLayout _modifierGroups = new() { Spacing = 8 };
    private readonly VerticalStackLayout _lines = new() { Spacing = 6 };
    private readonly BasketSummaryView _basket = new();
    private readonly Label _status = new() { FontSize = 13 };
    private readonly SharedTextInput _notes = new() { Placeholder = "Item notes" };
    private readonly SharedTextInput _discount = new() { Placeholder = "Discount amount" };
    private readonly Label _title = new() { FontSize = 22, FontAttributes = FontAttributes.Bold };
    private readonly HorizontalStackLayout _quickNotes = new() { Spacing = 8 };
    private readonly LoadingOverlayView _loading = new() { IsVisible = false, Message = "Waiting for Mother…" };
    private readonly OfflineStatusBannerView _conflictBanner = new()
    {
        IsVisible = false,
        Message = "Order conflict — refreshed from Mother."
    };

    public OrderEntryView()
    {
        _status.Use(Label.TextColorProperty, "PosTextMuted");
        _title.Use(Label.TextColorProperty, "PosTextPrimary");

        var send = new SharedButton { Text = "Send Order" };
        send.Clicked += async (_, _) =>
        {
            if (_viewModel is not null)
                await _viewModel.SendOrderAsync();
        };

        var discountBtn = new SharedButton { Text = "Apply Discount", Variant = ButtonVariant.Secondary };
        discountBtn.Clicked += async (_, _) =>
        {
            if (_viewModel is null)
                return;
            if (decimal.TryParse(_discount.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                _viewModel.DiscountDraftAmount = amount;
                await _viewModel.ApplyDiscountAsync(amount, false, "Manual discount");
            }
        };

        BuildQuickNotes();
        var more = new SharedButton { Text = "More options", Variant = ButtonVariant.Secondary };
        more.Clicked += async (_, _) => await ShowMoreOptionsAsync();
        Content = BuildLayout(send, discountBtn, more);
    }

    private void BuildQuickNotes()
    {
        _quickNotes.Children.Clear();
        foreach (var note in new[] { "No ice", "Extra spicy", "Allergy", "No onion", "Well done" })
        {
            var chip = new SharedButton { Text = note, Variant = ButtonVariant.Secondary, HeightRequest = 36 };
            var value = note;
            chip.Clicked += (_, _) =>
            {
                if (_viewModel is null)
                    return;
                _viewModel.PendingNotes = string.IsNullOrWhiteSpace(_viewModel.PendingNotes)
                    ? value
                    : $"{_viewModel.PendingNotes}; {value}";
                _notes.Text = _viewModel.PendingNotes ?? string.Empty;
            };
            _quickNotes.Children.Add(chip);
        }
    }

    private async Task ShowMoreOptionsAsync()
    {
        if (_viewModel is null)
            return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page
                   ?? Application.Current?.MainPage;
        if (page is null)
            return;

        var choice = await page.DisplayActionSheet(
            "Order options",
            "Cancel",
            null,
            "Refresh from Mother",
            "Clear item notes",
            "Apply 10% discount");

        if (choice == "Refresh from Mother" && !string.IsNullOrWhiteSpace(_viewModel.OrderId))
            await _viewModel.RefreshOrderAsync(_viewModel.OrderId!);
        else if (choice == "Clear item notes")
        {
            _viewModel.PendingNotes = null;
            _notes.Text = string.Empty;
        }
        else if (choice == "Apply 10% discount")
            await _viewModel.ApplyDiscountAsync(10m, isPercent: true, reason: "Quick 10% discount");

    }

    public void Bind(OrderEntryViewModel viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnVmPropertyChanged;
            _viewModel.OrderUpdated -= OnOrderUpdated;
        }

        _viewModel = viewModel;
        BindingContext = viewModel;
        viewModel.PropertyChanged += OnVmPropertyChanged;
        viewModel.OrderUpdated += OnOrderUpdated;
        RefreshAll();
    }

    private View BuildLayout(SharedButton send, SharedButton discountBtn, SharedButton more)
    {
        var menu = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 12
        };
        menu.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _categories
        }, 0, 0);
        menu.Add(_modifierGroups, 0, 1);
        menu.Add(new ScrollView { Content = _products }, 0, 2);
        menu.Add(new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                _notes,
                new ScrollView
                {
                    Orientation = ScrollOrientation.Horizontal,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                    Content = _quickNotes
                }
            }
        }, 0, 3);

        var basketPanel = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                _title,
                new ScrollView { Content = _lines, HeightRequest = 320 },
                _basket,
                _discount,
                discountBtn,
                send,
                more,
                _status
            }
        };

        var root = new Grid
        {
            Padding = 16,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star))
            },
            ColumnSpacing = 18
        };
        root.Add(menu, 0);
        root.Add(basketPanel, 1);

        var shell = new Grid();
        shell.Add(new VerticalStackLayout
        {
            Spacing = 0,
            Children = { _conflictBanner, root }
        });
        shell.Add(_loading);
        return shell;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
            return;

        if (e.PropertyName is nameof(OrderEntryViewModel.Categories) or null)
            RebuildCategories();
        if (e.PropertyName is nameof(OrderEntryViewModel.Products) or null)
            RebuildProducts();
        if (e.PropertyName is nameof(OrderEntryViewModel.Lines) or null)
            RebuildLines();
        if (e.PropertyName is nameof(OrderEntryViewModel.ModifierGroups) or null)
            RebuildModifiers();
        if (e.PropertyName is nameof(OrderEntryViewModel.DisplaySubtotalText)
            or nameof(OrderEntryViewModel.DisplayTaxText)
            or nameof(OrderEntryViewModel.DisplayDiscountText)
            or nameof(OrderEntryViewModel.DisplayTotalText)
            or nameof(OrderEntryViewModel.IsDisplayEstimate)
            or null)
            RefreshBasket();
        if (e.PropertyName is nameof(OrderEntryViewModel.StatusMessage) or nameof(OrderEntryViewModel.TableName) or nameof(OrderEntryViewModel.OrderStatus) or null)
            RefreshHeader();
        if (e.PropertyName is nameof(OrderEntryViewModel.PendingNotes))
            _notes.Text = _viewModel.PendingNotes ?? string.Empty;
        if (e.PropertyName is nameof(OrderEntryViewModel.IsBusy)
            or nameof(OrderEntryViewModel.IsDisplayEstimate)
            or nameof(OrderEntryViewModel.StatusMessage)
            or nameof(OrderEntryViewModel.DisplayServiceChargeText)
            or null)
        {
            RefreshStateChrome();
            if (e.PropertyName is nameof(OrderEntryViewModel.DisplayServiceChargeText) or null)
                RefreshBasket();
        }
    }

    private void RefreshStateChrome()
    {
        if (_viewModel is null)
            return;

        _loading.IsVisible = _viewModel.IsBusy;
        _loading.Message = string.IsNullOrWhiteSpace(_viewModel.StatusMessage)
            ? "Waiting for Mother…"
            : _viewModel.StatusMessage!;

        var status = _viewModel.StatusMessage ?? string.Empty;
        var conflict = status.Contains("another terminal", StringComparison.OrdinalIgnoreCase)
                       || status.Contains("conflict", StringComparison.OrdinalIgnoreCase);
        _conflictBanner.IsVisible = conflict;
        if (conflict)
            _conflictBanner.Message = status;
    }

    private void OnOrderUpdated(object? sender, OrderDto e) => RefreshAll();

    private void RefreshAll()
    {
        RebuildCategories();
        RebuildProducts();
        RebuildModifiers();
        RebuildLines();
        RefreshBasket();
        RefreshHeader();
        if (_viewModel is not null)
            _notes.Text = _viewModel.PendingNotes ?? string.Empty;
    }

    private void RefreshHeader()
    {
        if (_viewModel is null)
            return;
        var table = string.IsNullOrWhiteSpace(_viewModel.TableName) ? "Order" : _viewModel.TableName;
        _title.Text = $"{table} · {_viewModel.OrderStatus}";
        _status.Text = _viewModel.StatusMessage ?? string.Empty;
    }

    private void RefreshBasket()
    {
        if (_viewModel is null)
            return;
        _basket.SubtotalText = _viewModel.DisplaySubtotalText;
        _basket.TaxText = _viewModel.DisplayTaxText;
        _basket.ServiceChargeText = _viewModel.DisplayServiceChargeText;
        _basket.DiscountText = _viewModel.DisplayDiscountText;
        _basket.TotalText = _viewModel.DisplayTotalText;
        _basket.IsEstimate = _viewModel.IsDisplayEstimate;
    }

    private void RebuildCategories()
    {
        _categories.Children.Clear();
        if (_viewModel is null)
            return;

        foreach (var category in _viewModel.Categories)
        {
            var button = new CategoryButton
            {
                Text = category.Name,
                IsSelected = category.Id == _viewModel.SelectedCategoryId
            };
            var id = category.Id;
            button.Clicked += async (_, _) =>
            {
                _viewModel.PendingNotes = _notes.Text;
                await _viewModel.SelectCategoryAsync(id);
                RebuildCategories();
                RebuildProducts();
            };
            _categories.Children.Add(button);
        }
    }

    private void RebuildProducts()
    {
        _products.Children.Clear();
        if (_viewModel is null)
            return;

        foreach (var product in _viewModel.Products)
        {
            var button = new ProductButton
            {
                Text = $"{product.Name}\n{product.Price.ToString("C", CultureInfo.GetCultureInfo("en-GB"))}",
                WidthRequest = 150,
                HeightRequest = 96,
                Margin = new Thickness(0, 0, 10, 10)
            };
            if (!string.IsNullOrWhiteSpace(product.Colour)
                && Color.TryParse(product.Colour, out var tint))
            {
                button.BackgroundColor = tint;
            }
            var id = product.Id;
            button.Clicked += async (_, _) =>
            {
                _viewModel.PendingNotes = _notes.Text;
                await _viewModel.LoadModifiersAsync(id);
                RebuildModifiers();
                if (_viewModel.ModifierGroups.Count == 0)
                {
                    await _viewModel.AddProductAsync(id);
                    RebuildLines();
                    RefreshBasket();
                    RefreshHeader();
                }
            };
            _products.Children.Add(button);
        }
    }

    private void RebuildModifiers()
    {
        _modifierGroups.Children.Clear();
        if (_viewModel is null || _viewModel.ModifierGroups.Count == 0)
            return;

        foreach (var group in _viewModel.ModifierGroups)
        {
            var row = new HorizontalStackLayout { Spacing = 8 };
            var label = new Label { Text = group.Name, VerticalTextAlignment = TextAlignment.Center, FontAttributes = FontAttributes.Bold };
            label.Use(Label.TextColorProperty, "PosTextPrimary");
            row.Children.Add(label);
            foreach (var option in group.Options)
            {
                var chip = new SharedButton
                {
                    Text = option.Name,
                    Variant = _viewModel.SelectedModifierIds.Contains(option.Id) ? ButtonVariant.Primary : ButtonVariant.Secondary,
                    HeightRequest = 40
                };
                var optionId = option.Id;
                chip.Clicked += (_, _) =>
                {
                    _viewModel.ToggleModifier(optionId);
                    RebuildModifiers();
                };
                row.Children.Add(chip);
            }

            var add = new SharedButton { Text = "Add with options", HeightRequest = 40 };
            var productId = _viewModel.Products.FirstOrDefault(p => p.ModifierGroups?.Any(g => g.Id == group.Id) == true)?.Id
                            ?? _viewModel.Products.FirstOrDefault()?.Id;
            add.Clicked += async (_, _) =>
            {
                if (productId is null || _viewModel is null)
                    return;
                _viewModel.PendingNotes = _notes.Text;
                await _viewModel.AddProductAsync(productId);
                RebuildModifiers();
                RebuildLines();
                RefreshBasket();
                RefreshHeader();
            };
            row.Children.Add(add);
            _modifierGroups.Children.Add(row);
        }
    }

    private void RebuildLines()
    {
        _lines.Children.Clear();
        if (_viewModel is null)
            return;

        foreach (var line in _viewModel.Lines)
        {
            var lineId = line.Id;
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 8
            };

            var lineView = new OrderLineView
            {
                Quantity = line.Quantity,
                ProductName = line.ProductName,
                Notes = line.Notes ?? string.Empty,
                Total = line.LineTotal.ToString("C", CultureInfo.GetCultureInfo("en-GB"))
            };
            row.Add(lineView, 0);

            var qty = new QuantitySelector { Quantity = line.Quantity, Minimum = 1 };
            qty.QuantityChanged += async (_, value) =>
            {
                if (_viewModel is null) return;
                await _viewModel.ChangeQuantityAsync(lineId, value);
                RebuildLines();
                RefreshBasket();
                RefreshHeader();
            };
            row.Add(qty, 1);

            var voidBtn = new SharedButton { Text = "Void", Variant = ButtonVariant.Danger, WidthRequest = 72, HeightRequest = 44 };
            voidBtn.Clicked += async (_, _) =>
            {
                if (_viewModel is null) return;
                await _viewModel.VoidLineAsync(lineId, "Voided from order entry");
                RebuildLines();
                RefreshBasket();
                RefreshHeader();
            };
            row.Add(voidBtn, 2);
            _lines.Children.Add(row);
        }
    }
}
