using OrderWeb.Contracts.Customers;
using OrderWeb.Contracts.Orders;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Assign-customer flow: search + detail + previous orders.
/// Hosts supply directory data; SharedUI only renders approved fields.
/// </summary>
public sealed class AssignCustomerView : ContentView
{
    private readonly CustomerSearchView _search = new();
    private readonly CustomerDetailView _detail = new();
    private readonly VerticalStackLayout _historyHost = new() { Spacing = 8 };
    private readonly Label _historyHeader = new()
    {
        Text = "Previous Orders",
        FontSize = 16,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#111827"),
        IsVisible = false
    };
    private readonly StaleDataBannerView _staleBanner = new();

    public AssignCustomerView()
    {
        _search.SearchRequested += (_, request) => SearchRequested?.Invoke(this, request);
        _search.CustomerSelected += (_, customer) => CustomerSelected?.Invoke(this, customer);
        _detail.AssignRequested += (_, customer) => AssignRequested?.Invoke(this, customer);

        Content = new Grid
        {
            BackgroundColor = Color.FromArgb(CustomerOrderUiHelpers.PageBackground),
            Padding = new Thickness(20),
            Children =
            {
                new ScrollView
                {
                    Content = new VerticalStackLayout
                    {
                        Spacing = 16,
                        Children =
                        {
                            new Label
                            {
                                Text = "Assign Customer",
                                FontSize = 28,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = Color.FromArgb("#111827")
                            },
                            _staleBanner,
                            _search,
                            _detail,
                            _historyHeader,
                            _historyHost
                        }
                    }
                }
            }
        };
    }

    public event EventHandler<CustomerSearchRequestDto>? SearchRequested;
    public event EventHandler<CustomerSummaryDto>? CustomerSelected;
    public event EventHandler<CustomerSummaryDto>? AssignRequested;
    public event EventHandler<CustomerPreviousOrderDto>? PreviousOrderSelected;

    public void ApplySearch(CustomerSearchResultDto state)
    {
        _search.Apply(state);
        _staleBanner.Apply(state.SyncStatus, state.StatusBanner, state.StatusBannerTone);
    }

    public void ApplyDetail(CustomerDetailDto state)
    {
        _detail.Apply(state);
        _staleBanner.Apply(state.SyncStatus, state.StatusBanner, state.StatusBannerTone);
    }

    public void ApplyPreviousOrders(CustomerPreviousOrdersDto state)
    {
        _historyHost.Children.Clear();
        _historyHeader.IsVisible = state.CanAccessHistory && state.Orders.Count > 0;
        if (!state.CanAccessHistory)
        {
            return;
        }

        foreach (var order in state.Orders)
        {
            var local = order;
            var row = new Border
            {
                BackgroundColor = Colors.White,
                Stroke = Color.FromArgb("#E2E8F0"),
                StrokeThickness = 1,
                Padding = new Thickness(12, 10),
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label
                        {
                            Text = $"{order.OrderReferenceDisplay} · {order.DateDisplay}",
                            FontAttributes = FontAttributes.Bold,
                            FontSize = 14,
                            TextColor = Color.FromArgb("#0F172A")
                        },
                        new Label
                        {
                            Text = $"{order.ChannelLabel} · {order.TotalDisplay} · {order.StatusDisplay}",
                            FontSize = 12,
                            TextColor = Color.FromArgb("#64748B")
                        }
                    }
                }
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => PreviousOrderSelected?.Invoke(this, local);
            row.GestureRecognizers.Add(tap);
            _historyHost.Children.Add(row);
        }
    }
}
