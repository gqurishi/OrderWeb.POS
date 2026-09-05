using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Contracts.Customers;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>Shared customer search list. Only Mother-approved fields are shown.</summary>
public sealed class CustomerSearchView : ContentView
{
    private readonly Entry _nameEntry = new()
    {
        Placeholder = "e.g., John Smith",
        FontSize = 16,
        HeightRequest = 58,
        Margin = new Thickness(18, 0),
        BackgroundColor = Colors.Transparent
    };
    private readonly Entry _phoneEntry = new()
    {
        Placeholder = "e.g., 07123 456789",
        Keyboard = Keyboard.Telephone,
        FontSize = 16,
        HeightRequest = 58,
        Margin = new Thickness(18, 0),
        BackgroundColor = Colors.Transparent
    };
    private readonly VerticalStackLayout _results = new() { Spacing = 10 };
    private readonly StaleDataBannerView _staleBanner = new();
    private readonly Label _emptyLabel = new()
    {
        Text = "Search by name or phone.",
        FontSize = 14,
        TextColor = Color.FromArgb("#6B7280"),
        HorizontalTextAlignment = TextAlignment.Center
    };
    private readonly Grid _loadingOverlay;
    private CustomerOrderKind? _orderKind;

    public CustomerSearchView()
    {
        _loadingOverlay = CustomerOrderUiHelpers.CreateLoadingOverlay("Searching customers...");
        var search = new SharedButton
        {
            Text = "Search Existing Customer",
            Variant = ButtonVariant.Primary,
            HeightRequest = 56
        };
        search.Clicked += (_, _) => SearchRequested?.Invoke(
            this,
            new CustomerSearchRequestDto(_nameEntry.Text, _phoneEntry.Text, null, _orderKind));

        Content = new Grid
        {
            BackgroundColor = Color.FromArgb(CustomerOrderUiHelpers.PageBackground),
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 14,
                    Padding = new Thickness(8),
                    Children =
                    {
                        _staleBanner,
                        CustomerOrderUiHelpers.CreateField("Customer Name", _nameEntry),
                        CustomerOrderUiHelpers.CreateField("Phone Number", _phoneEntry),
                        search,
                        _emptyLabel,
                        _results
                    }
                },
                _loadingOverlay
            }
        };
    }

    public event EventHandler<CustomerSearchRequestDto>? SearchRequested;
    public event EventHandler<CustomerSummaryDto>? CustomerSelected;

    public void SetOrderKind(CustomerOrderKind? orderKind) => _orderKind = orderKind;

    public void Apply(CustomerSearchResultDto state)
    {
        _results.Children.Clear();
        _emptyLabel.IsVisible = state.Customers.Count == 0 && !state.IsLoading;
        foreach (var customer in state.Customers)
        {
            var local = customer;
            var use = new SharedButton { Text = "Use", Variant = ButtonVariant.Success, HeightRequest = 44, WidthRequest = 88 };
            use.Clicked += (_, _) => CustomerSelected?.Invoke(this, local);
            var row = new Border
            {
                BackgroundColor = Colors.White,
                Stroke = Color.FromArgb("#E2E8F0"),
                StrokeThickness = 1,
                Padding = new Thickness(14, 12),
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
                        Text = customer.Name ?? "Customer",
                        FontAttributes = FontAttributes.Bold,
                        FontSize = 16,
                        TextColor = Color.FromArgb("#111827")
                    },
                    new Label
                    {
                        Text = customer.DisplayLine ?? customer.Phone ?? customer.Id,
                        FontSize = 13,
                        TextColor = Color.FromArgb("#64748B")
                    }
                }
            });
            grid.Add(use, 1);
            _results.Children.Add(row);
        }

        _staleBanner.Apply(state.SyncStatus, state.StatusBanner, state.StatusBannerTone);
        _loadingOverlay.IsVisible = state.IsLoading;
        CustomerOrderUiHelpers.SetLoadingMessage(_loadingOverlay, state.LoadingMessage);
    }
}
