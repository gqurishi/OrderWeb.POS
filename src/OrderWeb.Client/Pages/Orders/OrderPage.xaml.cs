using System.Globalization;
using OrderWeb.Client.Dialogs;
using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Payments;
using OrderWeb.Client.Services;
using OrderWeb.Client.Views.Orders;

namespace OrderWeb.Client.Pages.Orders;

public partial class OrderPage : ContentPage
    {
        private readonly ClientCacheService _cache = new();
        private readonly MotherOrderClient _orderClient = new();
        private readonly ClientOfflinePolicy _offlinePolicy = new();
        private readonly MotherPrintClient _printClient = new();
        private readonly List<CachedMenuCategory> _categories = new();
        private readonly List<CachedProduct> _products = new();
        private readonly List<OrderSummaryLine> _basket = new();
        private readonly IDispatcherTimer _clockTimer;
        private readonly CachedTable? _table;
        private readonly int _covers;
        private CachedMenuCategory? _selectedCategory;
        private MotherOrderState? _currentOrder;
        private bool _loaded;
        private bool _orderContextLoaded;

        public OrderPage()
        {
            InitializeComponent();

        _clockTimer = Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        MenuGrid.ItemTapped += async (_, item) => await AddItemAsync(item);
        Summary.SendClicked += async (_, _) => await SendToKitchenAsync();
        Summary.PaymentClicked += async (_, _) => await Navigation.PushAsync(new PaymentPage(CurrentTotal(), _currentOrder?.OrderId, _currentOrder?.Version), false);
        Summary.ServiceClicked += async (_, _) => await QueueOrderActionAsync("service_charge", "Service charge request queued for Mother POS.");
        Summary.NotesClicked += async (_, _) => await AddOrderNoteAsync();
        Summary.VoidClicked += async (_, _) => await VoidOrderAsync();
        Summary.MoreClicked += async (_, _) => await Navigation.PushModalAsync(new MoreOptionsDialog(), false);
        Summary.PrintClicked += async (_, _) => await PrintOrderAsync();

        SizeChanged += (_, _) => ApplyResponsiveLayout();
        UpdateClock();
            ApplyResponsiveLayout();
            Refresh();
        }

        public OrderPage(CachedTable table, int covers)
            : this()
        {
            _table = table;
            _covers = Math.Max(covers, table.Covers > 0 ? table.Covers : 1);
            Refresh();
        }

        public OrderPage(MotherOrderState order)
            : this()
        {
            _currentOrder = order;
            Refresh();
        }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _clockTimer.Start();
            UpdateClock();
            if (!_loaded)
            {
                _loaded = true;
                await EnsureOrderContextAsync();
                await LoadMenuAsync();
            }
        }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _clockTimer.Stop();
    }

    private async Task LoadMenuAsync()
    {
        _categories.Clear();
        _categories.AddRange(await _cache.GetMenuCategoriesAsync());

        _selectedCategory = _categories.FirstOrDefault();
        await LoadProductsForSelectedCategoryAsync();
        Refresh();
    }

    private async Task LoadProductsForSelectedCategoryAsync()
    {
        _products.Clear();
        if (_selectedCategory == null)
        {
            return;
        }

        _products.AddRange(await _cache.GetProductsByCategoryAsync(_selectedCategory.Id));
    }

    private void ApplyResponsiveLayout()
    {
        const double summaryColumnWidth = 560;
        var compact = Width > 0 && Width < 1250;
        OrderLayout.RowDefinitions.Clear();
        OrderLayout.ColumnDefinitions.Clear();

        if (compact)
        {
            OrderLayout.Padding = 16;
            OrderLayout.ColumnSpacing = 0;
            OrderLayout.RowSpacing = 18;
            OrderLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            OrderLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            OrderLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Summary.WidthRequest = -1;
            Grid.SetRow(Summary, 1);
            Grid.SetColumn(Summary, 0);
            return;
        }

        OrderLayout.Padding = 24;
        OrderLayout.ColumnSpacing = 24;
        OrderLayout.RowSpacing = 0;
        OrderLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        OrderLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        OrderLayout.ColumnDefinitions.Add(new ColumnDefinition(summaryColumnWidth));
        Summary.WidthRequest = summaryColumnWidth;
        Grid.SetRow(Summary, 0);
        Grid.SetColumn(Summary, 1);
    }

        private void Refresh()
        {
            BuildCategoryTabs();
            MenuGrid.SetItems(FilteredProducts());
            Summary.SetOrder(OrderTitle(), OrderDetail(), SummaryLines());
        }

        private async Task EnsureOrderContextAsync()
        {
            if (_orderContextLoaded || _currentOrder != null || _table == null)
            {
                _orderContextLoaded = true;
                return;
            }

            _orderContextLoaded = true;

            try
            {
                if (!string.IsNullOrWhiteSpace(_table.CurrentOrderId))
                {
                    var cached = await _cache.GetOrderStateAsync(_table.CurrentOrderId);
                    if (cached != null)
                    {
                        _currentOrder = cached;
                        Refresh();
                        return;
                    }
                }

                var session = await _cache.GetCurrentLoginSessionAsync();
                var result = await _orderClient.OpenOrCreateTableOrderAsync(_table, _covers, session);
                _currentOrder = result.State;
                await _cache.SaveOrderStateAsync(_currentOrder);
                Refresh();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Table Order", $"Could not open this table order from the local client cache. Run bootstrap or Update All from Mother POS, then try again.\n\n{ex.Message}", "OK");
            }
        }

        private string OrderTitle()
        {
            if (!string.IsNullOrWhiteSpace(_currentOrder?.TableNumber))
            {
                return $"Order # Table {_currentOrder.TableNumber}";
            }

            if (!string.IsNullOrWhiteSpace(_table?.TableNumber))
            {
                return $"Order # Table {_table.TableNumber}";
            }

            return "Order # New";
        }

        private string OrderDetail()
        {
            if (_currentOrder?.OrderType.Equals("Table", StringComparison.OrdinalIgnoreCase) == true || _table != null)
            {
                return $"Guests = {Math.Max(_currentOrder?.Guests ?? _covers, 1)}";
            }

            return "Table order";
        }

        private IReadOnlyList<OrderSummaryLine> SummaryLines()
        {
            if (_basket.Count > 0)
            {
                return _basket;
            }

            return _currentOrder?.Lines
                .Select(line => new OrderSummaryLine(
                    line.Name,
                    line.Quantity,
                    line.UnitPrice,
                    line.Modifiers.Count > 0 ? string.Join(", ", line.Modifiers) : null,
                    line.Notes))
                .ToList()
                ?? _basket;
        }

    private IEnumerable<CachedProduct> FilteredProducts()
    {
        var query = SearchEntry.Text?.Trim();
        return _products.Where(product =>
            string.IsNullOrWhiteSpace(query)
            || product.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void BuildCategoryTabs()
    {
        CategoryTabs.Children.Clear();
        foreach (var category in _categories)
        {
            var selected = category == _selectedCategory;
            var button = new Button
            {
                Text = category.Name.ToUpperInvariant(),
                HeightRequest = 48,
                WidthRequest = 158,
                FontFamily = "OpenSansSemibold",
                FontSize = 16,
                CornerRadius = 8,
                Padding = 0,
                BackgroundColor = Color.FromArgb(selected ? "#EF4444" : "#3B82F6"),
                TextColor = Colors.White
            };
            button.Clicked += async (_, _) =>
            {
                _selectedCategory = category;
                await LoadProductsForSelectedCategoryAsync();
                Refresh();
            };
            CategoryTabs.Children.Add(button);
        }

        if (_categories.Count == 0)
        {
            CategoryTabs.Children.Add(new Label
            {
                Text = "No menu categories in local SQLite cache.",
                FontFamily = "OpenSansRegular",
                FontSize = 15,
                TextColor = Color.FromArgb("#64748B"),
                VerticalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(12, 0)
            });
        }
    }

    private async Task AddItemAsync(CachedProduct item)
    {
        var selectedModifiers = item.ModifierGroups
            .SelectMany(group => group.Modifiers.Take(Math.Min(group.MaxSelect, 1)))
            .Select(modifier => modifier.Name)
            .ToArray();
        var modifiers = selectedModifiers.Length > 0
            ? string.Join(", ", selectedModifiers)
            : null;

        if (item.ModifierGroups.Count > 0)
        {
            await Navigation.PushModalAsync(new ModifierDialog(), false);
        }

        var existing = _basket.FirstOrDefault(line => line.Name == item.Name && line.Modifiers == modifiers);
            if (existing != null)
            {
                var index = _basket.IndexOf(existing);
                _basket[index] = existing with { Quantity = existing.Quantity + 1 };
        }
        else
        {
                _basket.Add(new OrderSummaryLine(item.Name, 1, item.Price, modifiers));
            }

            if (_currentOrder != null)
            {
                var result = await _orderClient.AddItemAsync(_currentOrder, item, selectedModifiers);
                _currentOrder = result.State;
                await _cache.SaveOrderStateAsync(_currentOrder);
                _basket.Clear();
            }

            Refresh();
        }

    private async Task SendToKitchenAsync()
    {
            if (SummaryLines().Count == 0)
            {
                await DisplayAlert("Send to Kitchen", "Add items before sending to kitchen.", "OK");
                return;
            }

            var decision = _offlinePolicy.Evaluate(ClientOperation.SubmitFinalOrder, await _offlinePolicy.IsMotherOnlineAsync());
            if (!decision.Allowed)
            {
                await DisplayAlert("Send to Kitchen blocked", decision.Message, "OK");
                return;
            }

            if (_currentOrder != null)
            {
                // A Client-side state change is only a pending request. Do not
                // mark an order sent or print a kitchen ticket until Mother
                // accepts the authoritative mutation.
                await _cache.SaveOrderStateAsync(_currentOrder);
            }

            await QueueOrderActionAsync("send_to_kitchen", "Pending Mother confirmation — the order has not been sent to the kitchen.");
        }

    private async Task PrintOrderAsync()
    {
            if (SummaryLines().Count == 0)
            {
                await DisplayAlert("Print", "Add items before printing.", "OK");
                return;
            }

        await RequestPrintAsync("bill");
    }

        private async Task AddOrderNoteAsync()
        {
            if (SummaryLines().Count == 0)
            {
                await DisplayAlert("Notes", "Add an item before adding notes.", "OK");
                return;
            }

        var note = await DisplayPromptAsync("Notes", "Add note to the first item", "Save", "Cancel", "Kitchen note");
        if (string.IsNullOrWhiteSpace(note))
        {
            return;
        }

            if (_basket.Count > 0)
            {
                _basket[0] = _basket[0] with { Note = note.Trim() };
            }
            else if (_currentOrder?.Lines.Count > 0)
            {
                var firstLine = _currentOrder.Lines[0];
                var result = await _orderClient.AddNoteAsync(_currentOrder, firstLine, note.Trim());
                _currentOrder = result.State;
                await _cache.SaveOrderStateAsync(_currentOrder);
            }

            await QueueOrderActionAsync("order_note", "Note request queued for Mother POS.");
            Refresh();
        }

        private async Task VoidOrderAsync()
        {
            if (SummaryLines().Count == 0)
            {
                await DisplayAlert("Void", "There are no items to void.", "OK");
                return;
            }

        var confirm = await DisplayAlert("Void Order", "Void all items from this order?", "Void", "Cancel");
        if (!confirm)
        {
            return;
        }

            _basket.Clear();
            if (_currentOrder != null)
            {
                _currentOrder = _currentOrder with
                {
                    Lines = Array.Empty<MotherOrderLine>(),
                    Subtotal = 0m,
                    Tax = 0m,
                    Total = 0m,
                    Version = _currentOrder.Version + 1,
                    UpdatedUtc = DateTimeOffset.UtcNow.ToString("O")
                };
                await _cache.SaveOrderStateAsync(_currentOrder);
            }

            await QueueOrderActionAsync("void_order", "Void request queued for Mother POS.");
            Refresh();
        }

    private async Task QueueOrderActionAsync(string actionType, string message)
    {
        await _cache.QueuePendingActionAsync(actionType, new
        {
            orderType = "Table",
            total = CurrentTotal(),
            orderId = _currentOrder?.OrderId,
            tableId = _currentOrder?.TableId ?? _table?.Id,
            tableNumber = _currentOrder?.TableNumber ?? _table?.TableNumber,
            lines = SummaryLines().Select(line => new
            {
                line.Name,
                line.Quantity,
                line.UnitPrice,
                line.Modifiers,
                line.Note
            }).ToList(),
            queuedAtUtc = DateTimeOffset.UtcNow
        });
        await DisplayAlert("Mother POS", message, "OK");
    }

    private async Task RequestPrintAsync(string printType)
    {
        var session = await _cache.GetCurrentLoginSessionAsync();
        var request = await _printClient.RequestPrintAsync(printType, _currentOrder?.OrderId, session);
        await _cache.SavePrintRequestAsync(request);

        await DisplayAlert("Print", request.Message, "OK");
    }

        private decimal CurrentTotal()
        {
            if (_currentOrder != null && _basket.Count == 0)
            {
                return _currentOrder.Total;
            }

            var subtotal = _basket.Sum(line => line.Quantity * line.UnitPrice);
            return subtotal + Math.Round(subtotal * 0.2m, 2);
        }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        MenuGrid.SetItems(FilteredProducts());
    }

    private async void OnMenuClicked(object sender, EventArgs e)
    {
        await DisplayAlert("Restaurant POS", "Open the dashboard menu from the POS shell.", "OK");
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        await Navigation.PopToRootAsync(false);
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        DateLabel.Text = now.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
        TimeLabel.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
