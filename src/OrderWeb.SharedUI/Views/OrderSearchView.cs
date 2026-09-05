using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Contracts.Orders;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>Mother-style order search overlay (order number or phone).</summary>
public sealed class OrderSearchView : ContentView
{
    private readonly Entry _queryEntry = new()
    {
        Placeholder = "Order number or phone",
        FontSize = 16,
        HeightRequest = 52,
        Margin = new Thickness(16, 0),
        BackgroundColor = Colors.Transparent
    };
    private readonly VerticalStackLayout _results = new() { Spacing = 10 };
    private readonly StaleDataBannerView _staleBanner = new();
    private readonly Label _emptyLabel = new()
    {
        Text = "Enter an order number or phone to search.",
        FontSize = 14,
        TextColor = Color.FromArgb("#6B7280"),
        HorizontalTextAlignment = TextAlignment.Center
    };
    private readonly Grid _loadingOverlay;

    public OrderSearchView()
    {
        _loadingOverlay = CustomerOrderUiHelpers.CreateLoadingOverlay("Searching...");
        var searchButton = new SharedButton { Text = "Search", Variant = ButtonVariant.Success, HeightRequest = 52 };
        searchButton.Clicked += (_, _) => SearchRequested?.Invoke(this, _queryEntry.Text?.Trim() ?? string.Empty);
        var cancelButton = new SharedButton { Text = "Cancel", Variant = ButtonVariant.Secondary, HeightRequest = 52 };
        cancelButton.Clicked += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);

        var actions = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 12
        };
        actions.Add(searchButton, 0);
        actions.Add(cancelButton, 1);

        var card = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 1,
            Padding = new Thickness(24),
            WidthRequest = 500,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children =
                {
                    new Label
                    {
                        Text = "Search Orders",
                        FontSize = 22,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#1F2937")
                    },
                    new Label
                    {
                        Text = "Order Number or Phone",
                        FontSize = 14,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#4B5563")
                    },
                    new Border
                    {
                        BackgroundColor = Color.FromArgb("#F8FAFC"),
                        Stroke = Color.FromArgb("#E2E8F0"),
                        StrokeThickness = 1,
                        StrokeShape = new RoundRectangle { CornerRadius = 12 },
                        Content = _queryEntry
                    },
                    actions,
                    _staleBanner,
                    _emptyLabel,
                    new ScrollView { Content = _results, MaximumHeightRequest = 280 }
                }
            }
        };

        Content = new Grid
        {
            BackgroundColor = Color.FromArgb("#80000000"),
            Padding = new Thickness(24),
            Children = { card, _loadingOverlay }
        };
    }

    public event EventHandler<string>? SearchRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler<OrderSearchHitDto>? ResultSelected;

    public void Apply(OrderSearchResultDto state)
    {
        _queryEntry.Text = state.Query ?? _queryEntry.Text;
        _results.Children.Clear();
        _emptyLabel.IsVisible = state.Hits.Count == 0 && !state.IsLoading;
        _emptyLabel.Text = string.IsNullOrWhiteSpace(state.Query)
            ? "Enter an order number or phone to search."
            : "No matching orders found.";

        foreach (var hit in state.Hits)
        {
            var local = hit;
            var row = new Border
            {
                BackgroundColor = Color.FromArgb("#F8FAFC"),
                Stroke = Color.FromArgb("#E2E8F0"),
                StrokeThickness = 1,
                Padding = new Thickness(14, 12),
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label
                        {
                            Text = hit.OrderNumber ?? hit.OrderId,
                            FontAttributes = FontAttributes.Bold,
                            FontSize = 16,
                            TextColor = Color.FromArgb("#111827")
                        },
                        new Label
                        {
                            Text = string.Join(" · ", new[] { hit.ChannelLabel, hit.CustomerDisplay, hit.PhoneDisplay }
                                .Where(v => !string.IsNullOrWhiteSpace(v))),
                            FontSize = 13,
                            TextColor = Color.FromArgb("#64748B")
                        },
                        new Label
                        {
                            Text = $"£{hit.TotalAmount:F2} · {hit.StatusDisplay} · {hit.PaymentDisplay}",
                            FontSize = 13,
                            TextColor = Color.FromArgb("#0F766E")
                        }
                    }
                }
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => ResultSelected?.Invoke(this, local);
            row.GestureRecognizers.Add(tap);
            _results.Children.Add(row);
        }

        _staleBanner.Apply(state.SyncStatus, state.StatusBanner, state.StatusBannerTone);
        _loadingOverlay.IsVisible = state.IsLoading;
        CustomerOrderUiHelpers.SetLoadingMessage(_loadingOverlay, state.LoadingMessage);
    }
}
