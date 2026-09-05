using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Contracts.Customers;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Mother-style delivery customer capture + address lookup.
/// Visual source: Mother DeliveryCustomerModal (blue accent).
/// </summary>
public sealed class DeliveryDetailsView : ContentView
{
    private readonly Entry _nameEntry = CreateEntry("e.g., John Smith");
    private readonly Entry _phoneEntry = CreateEntry("e.g., 07123 456789", Keyboard.Telephone);
    private readonly Entry _addressEntry = CreateEntry("Flat / house and road");
    private readonly Entry _postcodeEntry = CreateEntry("Enter address or postcode...");
    private readonly Entry _cityEntry = CreateEntry("City");
    private readonly Label _zoneLabel = new()
    {
        FontSize = 14,
        TextColor = Color.FromArgb("#0369A1"),
        IsVisible = false
    };
    private readonly VerticalStackLayout _suggestions = new() { Spacing = 8 };
    private readonly CustomerSearchView _search = new();
    private readonly StaleDataBannerView _staleBanner = new();
    private readonly Grid _loadingOverlay;
    private string? _customerId;
    private decimal? _deliveryFee;
    private string? _zoneName;

    public DeliveryDetailsView()
    {
        _loadingOverlay = CustomerOrderUiHelpers.CreateLoadingOverlay("Loading…");
        _search.SetOrderKind(CustomerOrderKind.Delivery);
        _search.SearchRequested += (_, request) => CustomerSearchRequested?.Invoke(this, request);
        _search.CustomerSelected += (_, customer) =>
        {
            ApplyCustomer(customer);
            CustomerSelected?.Invoke(this, customer);
        };

        var lookup = new SharedButton
        {
            Text = "Lookup",
            Variant = ButtonVariant.Primary,
            HeightRequest = 56,
            WidthRequest = 120
        };
        lookup.Clicked += (_, _) =>
            AddressLookupRequested?.Invoke(this, _postcodeEntry.Text?.Trim() ?? string.Empty);

        var postcodeRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        postcodeRow.Add(new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = _postcodeEntry
        }, 0);
        postcodeRow.Add(lookup, 1);

        var continueButton = new SharedButton
        {
            Text = "Continue",
            Variant = ButtonVariant.Primary,
            HeightRequest = 56
        };
        continueButton.Clicked += (_, _) => ContinueRequested?.Invoke(this, CaptureDraft());

        var form = new VerticalStackLayout
        {
            Spacing = 20,
            MaximumWidthRequest = 550,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                _staleBanner,
                BuildHeader(),
                CustomerOrderUiHelpers.CreateField("Customer Name", _nameEntry),
                CustomerOrderUiHelpers.CreateField("Phone Number", _phoneEntry),
                new VerticalStackLayout
                {
                    Spacing = 12,
                    Children =
                    {
                        new Label
                        {
                            Text = "Delivery Address",
                            FontSize = 15,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#4A5568")
                        },
                        postcodeRow,
                        _suggestions,
                        CustomerOrderUiHelpers.CreateField("Address", _addressEntry),
                        CustomerOrderUiHelpers.CreateField("City", _cityEntry),
                        _zoneLabel
                    }
                },
                _search,
                continueButton
            }
        };

        Content = new Grid
        {
            BackgroundColor = Color.FromArgb("#F8F9FA"),
            Padding = new Thickness(24),
            Children =
            {
                new ScrollView { Content = form },
                _loadingOverlay
            }
        };
    }

    public event EventHandler<CustomerSearchRequestDto>? CustomerSearchRequested;
    public event EventHandler<string>? AddressLookupRequested;
    public event EventHandler<CustomerSummaryDto>? CustomerSelected;
    public event EventHandler<AddressSuggestionDto>? AddressSelected;
    public event EventHandler<DeliveryDetailsDto>? ContinueRequested;

    public void Apply(DeliveryDetailsDto state)
    {
        _customerId = state.CustomerId;
        _deliveryFee = state.DeliveryFee;
        _zoneName = state.ZoneName;

        if (!string.IsNullOrWhiteSpace(state.Name)) _nameEntry.Text = state.Name;
        if (!string.IsNullOrWhiteSpace(state.Phone)) _phoneEntry.Text = state.Phone;
        if (!string.IsNullOrWhiteSpace(state.AddressLine)) _addressEntry.Text = state.AddressLine;
        if (!string.IsNullOrWhiteSpace(state.City)) _cityEntry.Text = state.City;
        if (!string.IsNullOrWhiteSpace(state.Postcode)) _postcodeEntry.Text = state.Postcode;

        RenderSuggestions(state.AddressSuggestions);
        if (state.SearchResults is not null) _search.Apply(state.SearchResults);

        UpdateZoneLabel();
        _staleBanner.Apply(state.SyncStatus, state.StatusBanner, state.StatusBannerTone);
        _loadingOverlay.IsVisible = state.IsLoading;
        CustomerOrderUiHelpers.SetLoadingMessage(_loadingOverlay, state.LoadingMessage);
    }

    public DeliveryDetailsDto CaptureDraft() =>
        new(
            CustomerId: _customerId,
            Name: _nameEntry.Text?.Trim(),
            Phone: _phoneEntry.Text?.Trim(),
            AddressLine: _addressEntry.Text?.Trim(),
            FlatOrHouse: null,
            Road: null,
            City: _cityEntry.Text?.Trim(),
            Postcode: _postcodeEntry.Text?.Trim(),
            DeliveryFee: _deliveryFee,
            ZoneName: _zoneName,
            AddressSuggestions: Array.Empty<AddressSuggestionDto>(),
            SearchResults: null,
            SyncStatus: CustomerOrderUiHelpers.LiveSync());

    private void ApplyCustomer(CustomerSummaryDto customer)
    {
        _customerId = customer.Id;
        _nameEntry.Text = customer.Name;
        _phoneEntry.Text = customer.Phone;
        if (!string.IsNullOrWhiteSpace(customer.Address)) _addressEntry.Text = customer.Address;
        if (!string.IsNullOrWhiteSpace(customer.City)) _cityEntry.Text = customer.City;
        if (!string.IsNullOrWhiteSpace(customer.Postcode)) _postcodeEntry.Text = customer.Postcode;
    }

    private void RenderSuggestions(IReadOnlyList<AddressSuggestionDto> suggestions)
    {
        _suggestions.Children.Clear();
        foreach (var suggestion in suggestions)
        {
            var local = suggestion;
            var row = new Border
            {
                BackgroundColor = Color.FromArgb("#EFF6FF"),
                Stroke = Color.FromArgb("#BFDBFE"),
                StrokeThickness = 1,
                Padding = new Thickness(12, 10),
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Content = new Label
                {
                    Text = suggestion.DisplayText,
                    FontSize = 14,
                    TextColor = Color.FromArgb("#1E3A8A")
                }
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                _addressEntry.Text = string.Join(", ", new[] { local.AddressLine1, local.AddressLine2, local.AddressLine3 }
                    .Where(v => !string.IsNullOrWhiteSpace(v)));
                _cityEntry.Text = local.City;
                _postcodeEntry.Text = local.Postcode;
                AddressSelected?.Invoke(this, local);
            };
            row.GestureRecognizers.Add(tap);
            _suggestions.Children.Add(row);
        }
    }

    private void UpdateZoneLabel()
    {
        if (string.IsNullOrWhiteSpace(_zoneName) && _deliveryFee is null)
        {
            _zoneLabel.IsVisible = false;
            return;
        }

        _zoneLabel.Text = _deliveryFee is decimal fee
            ? $"Zone: {_zoneName ?? "Delivery"} · £{fee:F2}"
            : $"Zone: {_zoneName}";
        _zoneLabel.IsVisible = true;
    }

    private static Entry CreateEntry(string placeholder, Keyboard? keyboard = null) =>
        new()
        {
            Placeholder = placeholder,
            Keyboard = keyboard ?? Keyboard.Default,
            FontSize = 16,
            HeightRequest = 56,
            Margin = new Thickness(16, 0),
            BackgroundColor = Colors.Transparent,
            TextColor = Color.FromArgb("#1A202C"),
            PlaceholderColor = Color.FromArgb("#A0AEC0")
        };

    private static View BuildHeader() =>
        new VerticalStackLayout
        {
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 12),
            Children =
            {
                new Label
                {
                    Text = "Delivery Order",
                    FontSize = 36,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    TextColor = Color.FromArgb("#1A202C")
                },
                new Label
                {
                    Text = "Customer Information",
                    FontSize = 16,
                    HorizontalOptions = LayoutOptions.Center,
                    TextColor = Color.FromArgb("#718096")
                },
                new BoxView
                {
                    HeightRequest = 3,
                    WidthRequest = 60,
                    Color = Color.FromArgb(CustomerOrderUiHelpers.DeliveryBlue),
                    HorizontalOptions = LayoutOptions.Center,
                    CornerRadius = 2,
                    Margin = new Thickness(0, 10, 0, 0)
                }
            }
        };
}
