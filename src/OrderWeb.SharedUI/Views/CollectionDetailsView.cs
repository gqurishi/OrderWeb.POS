using OrderWeb.Contracts.Customers;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Mother-style collection customer capture + search.
/// Visual source: Mother CollectionCustomerModal (green accent).
/// </summary>
public sealed class CollectionDetailsView : ContentView
{
    private readonly Entry _nameEntry = CreateEntry("e.g., John Smith");
    private readonly Entry _phoneEntry = CreateEntry("e.g., 07123 456789", Keyboard.Telephone);
    private readonly Entry _pickupEntry = CreateEntry("Pickup time (optional)");
    private readonly Entry _notesEntry = CreateEntry("Notes (optional)");
    private readonly CustomerSearchView _search = new();
    private readonly StaleDataBannerView _staleBanner = new();
    private readonly Grid _loadingOverlay;
    private string? _customerId;

    public CollectionDetailsView()
    {
        _loadingOverlay = CustomerOrderUiHelpers.CreateLoadingOverlay("Loading…");
        _search.SetOrderKind(CustomerOrderKind.Collection);
        _search.SearchRequested += (_, request) => SearchRequested?.Invoke(this, request);
        _search.CustomerSelected += (_, customer) =>
        {
            ApplyCustomer(customer);
            CustomerSelected?.Invoke(this, customer);
        };

        var continueButton = new SharedButton
        {
            Text = "Continue",
            Variant = ButtonVariant.Success,
            HeightRequest = 56
        };
        continueButton.Clicked += (_, _) => ContinueRequested?.Invoke(this, CaptureDraft());

        var form = new VerticalStackLayout
        {
            Spacing = 26,
            WidthRequest = 560,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                _staleBanner,
                BuildHeader(),
                CustomerOrderUiHelpers.CreateField("Customer Name", _nameEntry),
                CustomerOrderUiHelpers.CreateField("Phone Number", _phoneEntry),
                CustomerOrderUiHelpers.CreateField("Pickup Time", _pickupEntry),
                CustomerOrderUiHelpers.CreateField("Notes", _notesEntry),
                _search,
                continueButton
            }
        };

        Content = new Grid
        {
            BackgroundColor = Color.FromArgb(CustomerOrderUiHelpers.PageBackground),
            Padding = new Thickness(44),
            Children =
            {
                new ScrollView { Content = form },
                _loadingOverlay
            }
        };
    }

    public event EventHandler<CustomerSearchRequestDto>? SearchRequested;
    public event EventHandler<CustomerSummaryDto>? CustomerSelected;
    public event EventHandler<CollectionDetailsDto>? ContinueRequested;

    public void Apply(CollectionDetailsDto state)
    {
        _customerId = state.CustomerId;
        if (!string.IsNullOrWhiteSpace(state.Name)) _nameEntry.Text = state.Name;
        if (!string.IsNullOrWhiteSpace(state.Phone)) _phoneEntry.Text = state.Phone;
        if (!string.IsNullOrWhiteSpace(state.PickupTime)) _pickupEntry.Text = state.PickupTime;
        if (!string.IsNullOrWhiteSpace(state.Notes)) _notesEntry.Text = state.Notes;
        if (state.SearchResults is not null) _search.Apply(state.SearchResults);

        _staleBanner.Apply(state.SyncStatus, state.StatusBanner, state.StatusBannerTone);
        _loadingOverlay.IsVisible = state.IsLoading;
        CustomerOrderUiHelpers.SetLoadingMessage(_loadingOverlay, state.LoadingMessage);
    }

    public CollectionDetailsDto CaptureDraft() =>
        new(
            CustomerId: _customerId,
            Name: _nameEntry.Text?.Trim(),
            Phone: _phoneEntry.Text?.Trim(),
            PickupTime: _pickupEntry.Text?.Trim(),
            Notes: _notesEntry.Text?.Trim(),
            SearchResults: null,
            SyncStatus: CustomerOrderUiHelpers.LiveSync());

    private void ApplyCustomer(CustomerSummaryDto customer)
    {
        _customerId = customer.Id;
        _nameEntry.Text = customer.Name;
        _phoneEntry.Text = customer.Phone;
    }

    private static Entry CreateEntry(string placeholder, Keyboard? keyboard = null) =>
        new()
        {
            Placeholder = placeholder,
            Keyboard = keyboard ?? Keyboard.Default,
            FontSize = 16,
            HeightRequest = 58,
            Margin = new Thickness(18, 0),
            BackgroundColor = Colors.Transparent
        };

    private static View BuildHeader() =>
        new VerticalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 24, 0, 8),
            Children =
            {
                new Label
                {
                    Text = "Collection Order",
                    FontFamily = "AlegreyaBold",
                    FontSize = 40,
                    HorizontalOptions = LayoutOptions.Center,
                    TextColor = Color.FromArgb("#111827")
                },
                new Label
                {
                    Text = "Customer Information",
                    FontSize = 17,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    TextColor = Color.FromArgb("#64748B")
                },
                new BoxView
                {
                    HeightRequest = 3,
                    WidthRequest = 66,
                    Color = Color.FromArgb(CustomerOrderUiHelpers.SuccessGreen),
                    HorizontalOptions = LayoutOptions.Center,
                    CornerRadius = 2
                }
            }
        };
}
