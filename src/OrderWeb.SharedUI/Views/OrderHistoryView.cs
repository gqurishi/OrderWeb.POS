using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Contracts.Orders;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Mother-style order history: date filter, smart search, type tabs, rows, voided section.
/// Visual source: Mother OrderHistoryPage.
/// </summary>
public sealed class OrderHistoryView : ContentView
{
    private readonly Label _dateLabel = new()
    {
        Text = "Select date",
        FontSize = 18,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#14532D")
    };
    private readonly HorizontalStackLayout _tabs = new()
    {
        Spacing = 12,
        HorizontalOptions = LayoutOptions.Center,
        Padding = new Thickness(16)
    };
    private readonly VerticalStackLayout _items = new() { Spacing = 10 };
    private readonly VerticalStackLayout _voidedItems = new() { Spacing = 10 };
    private readonly Label _voidedHeader = new()
    {
        Text = "Voided Orders",
        FontSize = 16,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#991B1B"),
        Margin = new Thickness(0, 16, 0, 0),
        IsVisible = false
    };
    private readonly Label _permissionLabel = new()
    {
        Text = "Order history is not available for this terminal.",
        FontSize = 15,
        TextColor = Color.FromArgb("#64748B"),
        HorizontalTextAlignment = TextAlignment.Center,
        Margin = new Thickness(0, 40, 0, 0),
        IsVisible = false
    };
    private readonly Label _emptyLabel = new()
    {
        Text = "No orders for this date.",
        FontSize = 15,
        TextColor = Color.FromArgb("#9CA3AF"),
        HorizontalOptions = LayoutOptions.Center,
        IsVisible = false
    };
    private readonly StaleDataBannerView _staleBanner = new();
    private readonly Grid _loadingOverlay;
    private readonly OrderSearchView _searchOverlay = new() { IsVisible = false };
    private OpenOrderChannelKind _channel = OpenOrderChannelKind.All;
    private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);

    public OrderHistoryView()
    {
        _loadingOverlay = CustomerOrderUiHelpers.CreateLoadingOverlay("Loading history…");
        _searchOverlay.SearchRequested += (_, query) => SearchRequested?.Invoke(this, query);
        _searchOverlay.CancelRequested += (_, _) => _searchOverlay.IsVisible = false;
        _searchOverlay.ResultSelected += (_, hit) =>
        {
            _searchOverlay.IsVisible = false;
            HistoryItemSelected?.Invoke(this, new OrderHistoryItemDto(
                hit.OrderId,
                hit.OrderNumber,
                hit.ChannelLabel,
                hit.CustomerDisplay,
                hit.TotalAmount,
                hit.CreatedAtUtc,
                hit.StatusDisplay,
                hit.PaymentDisplay,
                false,
                false));
        };

        var dateCard = BuildActionCard(
            "#EEF7F2", "#B7E4C7", "FILTER BY DATE", "#16A34A",
            _dateLabel, "Tap to choose a different date", "#15803D",
            () => DateFilterRequested?.Invoke(this, _selectedDate));

        var searchTitle = new Label
        {
            Text = "Find order by number or phone",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#4C1D95")
        };
        var searchCard = BuildActionCard(
            "#F4ECFF", "#D8B4FE", "SMART SEARCH", "#7C3AED",
            searchTitle, "Tap to search instantly", "#6D28D9",
            () =>
            {
                _searchOverlay.IsVisible = true;
                SearchOverlayOpened?.Invoke(this, EventArgs.Empty);
            });

        var headerActions = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 12
        };
        headerActions.Add(dateCard, 0);
        headerActions.Add(searchCard, 1);

        var tabBar = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = _tabs
        };

        var body = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    _staleBanner,
                    headerActions,
                    tabBar,
                    _permissionLabel,
                    _emptyLabel,
                    _items,
                    _voidedHeader,
                    _voidedItems
                }
            }
        };

        Content = new Grid
        {
            BackgroundColor = Color.FromArgb("#F8F9FA"),
            Children = { body, _loadingOverlay, _searchOverlay }
        };
    }

    public event EventHandler<DateOnly>? DateFilterRequested;
    public event EventHandler<OpenOrderChannelKind>? ChannelChanged;
    public event EventHandler<string>? SearchRequested;
    public event EventHandler? SearchOverlayOpened;
    public event EventHandler<OrderHistoryItemDto>? HistoryItemSelected;

    public void Apply(OrderHistoryPageDto state)
    {
        _selectedDate = state.SelectedDate;
        _channel = state.SelectedChannel;
        _dateLabel.Text = state.SelectedDate.ToString("MMMM d, yyyy");

        _staleBanner.Apply(state.SyncStatus, state.StatusBanner, state.StatusBannerTone);
        _loadingOverlay.IsVisible = state.IsLoading;
        CustomerOrderUiHelpers.SetLoadingMessage(_loadingOverlay, state.LoadingMessage);

        _permissionLabel.IsVisible = !state.CanAccessHistory;
        _tabs.IsVisible = state.CanAccessHistory;
        _items.IsVisible = state.CanAccessHistory;
        _voidedItems.IsVisible = state.CanAccessHistory;

        if (!state.CanAccessHistory)
        {
            _emptyLabel.IsVisible = false;
            _voidedHeader.IsVisible = false;
            _items.Children.Clear();
            _voidedItems.Children.Clear();
            return;
        }

        RenderTabs();
        RenderItems(state.Items, _items);
        RenderItems(state.VoidedItems, _voidedItems);
        _voidedHeader.IsVisible = state.VoidedItems.Count > 0;
        _emptyLabel.IsVisible = state.Items.Count == 0 && state.VoidedItems.Count == 0 && !state.IsLoading;
    }

    public void ApplySearch(OrderSearchResultDto state)
    {
        _searchOverlay.IsVisible = true;
        _searchOverlay.Apply(state);
    }

    private void RenderTabs()
    {
        _tabs.Children.Clear();
        var accent = Color.FromArgb(CustomerOrderUiHelpers.SuccessGreen);
        void Add(string text, OpenOrderChannelKind channel)
        {
            _tabs.Children.Add(CustomerOrderUiHelpers.CreateTab(
                text,
                _channel == channel,
                accent,
                (_, _) =>
                {
                    _channel = channel;
                    ChannelChanged?.Invoke(this, channel);
                }));
        }

        Add("All", OpenOrderChannelKind.All);
        Add("Collection", OpenOrderChannelKind.Collection);
        Add("Delivery", OpenOrderChannelKind.Delivery);
        Add("Table", OpenOrderChannelKind.Table);
    }

    private void RenderItems(IReadOnlyList<OrderHistoryItemDto> source, VerticalStackLayout host)
    {
        host.Children.Clear();
        foreach (var item in source)
        {
            var local = item;
            var row = new Border
            {
                BackgroundColor = Colors.White,
                Stroke = item.IsVoided ? Color.FromArgb("#FECACA") : Color.FromArgb("#E5E7EB"),
                StrokeThickness = 1,
                Padding = new Thickness(16, 12),
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Content = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto)
                    },
                    ColumnSpacing = 12
                }
            };
            var grid = (Grid)row.Content!;
            grid.Add(new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label
                    {
                        Text = item.OrderNumber ?? item.OrderId,
                        FontSize = 16,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#111827")
                    },
                    new Label
                    {
                        Text = string.Join(" · ", new[] { item.ChannelLabel, item.CustomerDisplay, item.StatusDisplay }
                            .Where(v => !string.IsNullOrWhiteSpace(v))),
                        FontSize = 13,
                        TextColor = Color.FromArgb("#64748B")
                    }
                }
            });
            grid.Add(new VerticalStackLayout
            {
                Spacing = 2,
                HorizontalOptions = LayoutOptions.End,
                Children =
                {
                    new Label
                    {
                        Text = $"£{item.TotalAmount:F2}",
                        FontSize = 16,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#0F766E"),
                        HorizontalTextAlignment = TextAlignment.End
                    },
                    new Label
                    {
                        Text = item.PaymentDisplay,
                        FontSize = 12,
                        TextColor = Color.FromArgb("#94A3B8"),
                        HorizontalTextAlignment = TextAlignment.End
                    }
                }
            }, 1);

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => HistoryItemSelected?.Invoke(this, local);
            row.GestureRecognizers.Add(tap);
            host.Children.Add(row);
        }
    }

    private static Border BuildActionCard(
        string background,
        string stroke,
        string eyebrow,
        string eyebrowColor,
        View title,
        string hint,
        string hintColor,
        Action onTap)
    {
        var border = new Border
        {
            BackgroundColor = Color.FromArgb(background),
            Stroke = Color.FromArgb(stroke),
            StrokeThickness = 1,
            Padding = new Thickness(16),
            MinimumHeightRequest = 92,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = eyebrow,
                        FontSize = 12,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb(eyebrowColor)
                    },
                    title,
                    new Label
                    {
                        Text = hint,
                        FontSize = 11,
                        TextColor = Color.FromArgb(hintColor)
                    }
                }
            }
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onTap();
        border.GestureRecognizers.Add(tap);
        return border;
    }
}
