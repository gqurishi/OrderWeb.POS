using OrderWeb.Client.Models;
using OrderWeb.Client.Services;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Pages.Orders;

public partial class DeliveryOrderPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private readonly MotherCustomerClient _customerClient = new();
    private readonly MotherOrderClient _orderClient = new();
    private readonly ClientOfflinePolicy _offlinePolicy = new();
    private string? _pendingDeliveryOrderId;
    private bool _isContinuing;
    private bool _isClosing;
    private bool _enterAnimationStarted;

    public DeliveryOrderPage()
    {
        InitializeComponent();
        ClientPageChrome.HideSystemBackChrome(this);
        // Start off-screen so Delivery slides in from the right like Mother.
        Opacity = 0;
        TranslationX = 420;

        Entry.AddressSearchRequested += OnAddressSearchRequested;
        Entry.CustomerSearchRequested += OnCustomerSearchRequested;
        Entry.ContinueRequested += OnContinueRequested;
        Entry.CancelRequested += OnCancelRequested;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ClientPageChrome.HideSystemBackChrome(this);
        if (_enterAnimationStarted)
        {
            return;
        }

        _enterAnimationStarted = true;
        var width = Width > 1 ? Width : (DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density);
        TranslationX = Math.Max(width, 420);
        Opacity = 1;
        await this.TranslateToAsync(0, 0, 280, Easing.CubicOut);
    }

    protected override bool OnBackButtonPressed() => true;

    private async void OnAddressSearchRequested(object? sender, (string? Term, string? Name, string? Phone) e)
    {
        var term = e.Term?.Trim();
        var name = e.Name?.Trim();
        var phone = e.Phone?.Trim();

        if (string.IsNullOrWhiteSpace(term) &&
            string.IsNullOrWhiteSpace(name) &&
            string.IsNullOrWhiteSpace(phone))
        {
            Entry.ShowStatus("Enter a postcode, address, name, or phone to search.", "#DC2626");
            return;
        }

        Entry.SetAddressSearchBusy(true);
        try
        {
            var customerResults = await SearchCustomersInternalAsync(name, phone, term);
            if (customerResults.Count > 0)
            {
                Entry.SetCustomerResults(customerResults.Select(ToSearchItem));
                Entry.ShowStatus($"Found {customerResults.Count} customer(s). Select one to fill the form.", "#10B981");
                return;
            }

            if (string.IsNullOrWhiteSpace(term))
            {
                Entry.SetCustomerResults(Array.Empty<CustomerEntrySearchItem>());
                Entry.ShowStatus("No customer match. Enter a postcode to look up addresses.", "#718096");
                return;
            }

            var addresses = await _customerClient.LookupAddressesAsync(term);
            if (addresses.Count > 0)
            {
                Entry.SetAddressSuggestions(addresses.Select(ToSuggestionItem));
                Entry.ShowStatus($"Found {addresses.Count} address(es). Select one to fill the form.", "#3B82F6");
            }
            else
            {
                Entry.SetCustomerResults(Array.Empty<CustomerEntrySearchItem>());
                Entry.ShowStatus($"No addresses found for {term}. You can enter the address manually.", "#718096");
            }
        }
        catch (Exception ex)
        {
            Entry.ShowStatus($"Search failed: {ex.Message}. You can still enter the address manually.", "#DC2626");
        }
        finally
        {
            Entry.SetAddressSearchBusy(false);
        }
    }

    private async void OnCustomerSearchRequested(object? sender, (string? Name, string? Phone, string? AddressOrPostcode) e)
    {
        Entry.SetCustomerSearchBusy(true);
        try
        {
            var results = await SearchCustomersInternalAsync(e.Name?.Trim(), e.Phone?.Trim(), e.AddressOrPostcode?.Trim());
            Entry.SetCustomerResults(results.Select(ToSearchItem));
            Entry.ShowStatus(
                results.Count == 0 ? "No customers found matching your search." : $"Found {results.Count} customer(s).",
                results.Count == 0 ? "#718096" : "#10B981");
        }
        finally
        {
            Entry.SetCustomerSearchBusy(false);
        }
    }

    private async Task<IReadOnlyList<CachedCustomer>> SearchCustomersInternalAsync(string? name, string? phone, string? addressOrPostcode)
    {
        if (string.IsNullOrWhiteSpace(name) &&
            string.IsNullOrWhiteSpace(phone) &&
            string.IsNullOrWhiteSpace(addressOrPostcode))
        {
            Entry.ShowStatus("Enter customer name, phone, address, or postcode to search.", "#DC2626");
            return Array.Empty<CachedCustomer>();
        }

        Entry.ShowStatus("Searching customers...", "#718096");
        var request = new CustomerSearchRequest("Delivery", name, phone, addressOrPostcode);
        var mother = await _customerClient.SearchCustomersAsync(request);
        return mother
            .GroupBy(customer => string.IsNullOrWhiteSpace(customer.MotherId) ? customer.Id.ToString() : customer.MotherId)
            .Select(group => group.First())
            .Take(10)
            .ToList();
    }

    private async void OnContinueRequested(object? sender, DeliveryCustomerEntryResult e)
    {
        if (_isContinuing)
        {
            return;
        }

        var customer = new CachedCustomer(0, string.Empty, e.Name, e.Phone, null, e.FormattedAddress, e.Postcode, 0);

        _isContinuing = true;
        Entry.SetContinueBusy(true, "Opening order...");

        try
        {
            var decision = _offlinePolicy.Evaluate(ClientOperation.SaveCollectionOrder, await _offlinePolicy.IsMotherOnlineAsync());
            if (!decision.Allowed)
            {
                Entry.ShowStatus(decision.Message, "#DC2626");
                return;
            }

            var deliveryQuote = await _customerClient.QuoteDeliveryZoneAsync(e.Postcode);

            var draft = new CustomerOrderDraft(
                "Delivery",
                customer,
                null,
                "ASAP",
                string.Empty,
                e.FormattedAddress,
                e.Postcode,
                deliveryQuote.ZoneName,
                deliveryQuote.DeliveryFee);

            Entry.ShowStatus(
                deliveryQuote.IsKnownZone
                    ? $"{deliveryQuote.ZoneName}: £{deliveryQuote.DeliveryFee:F2} delivery fee. Saving customer with Mother POS..."
                    : $"No delivery zone found for {deliveryQuote.Postcode}. Saving customer with Mother POS...",
                deliveryQuote.IsKnownZone ? "#10B981" : "#B45309");

            var savedCustomer = await _customerClient.SaveCustomerAsync(draft);
            await _cache.CacheCustomerForActiveOrderAsync(savedCustomer, isDelivery: true);

            Entry.ShowStatus("Opening delivery order...", "#718096");
            var session = await _cache.GetCurrentLoginSessionAsync();
            _pendingDeliveryOrderId ??= Guid.NewGuid().ToString("N");
            var orderResult = await _orderClient.CreateCustomerOrderAsync(
                draft with { Customer = savedCustomer },
                session,
                _pendingDeliveryOrderId);
            await _cache.SaveOrderStateAsync(orderResult.State);
            _pendingDeliveryOrderId = null;
            if (orderResult.ConflictDetected)
            {
                Entry.ShowStatus(orderResult.Message, "#D97706");
            }

            await Navigation.PushAsync(
                new OrderPage(orderResult.State, savedCustomer.Name, savedCustomer.Phone),
                false);
        }
        catch (Exception ex)
        {
            Entry.ShowStatus($"Failed to continue: {ex.Message}", "#DC2626");
        }
        finally
        {
            _isContinuing = false;
            Entry.SetContinueBusy(false);
        }
    }

    private async void OnCancelRequested(object? sender, EventArgs e) => await CloseAsync();

    private async Task CloseAsync()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        try
        {
            var width = Width > 1 ? Width : 420;
            await this.TranslateToAsync(width, 0, 220, Easing.CubicIn);
            await ClientSideNavigation.PopFromSideAsync(Navigation);
        }
        catch
        {
            await ClientSideNavigation.PopFromSideAsync(Navigation);
        }
        finally
        {
            _isClosing = false;
        }
    }

    private static CustomerEntrySearchItem ToSearchItem(CachedCustomer customer) =>
        new(customer.Name, customer.Phone, customer.MotherId, new DeliveryCustomerAddressInfo(customer.Address, customer.Postcode));

    private static DeliveryAddressSuggestionItem ToSuggestionItem(AddressSuggestion address)
    {
        var (premise, road) = SplitLookupAddress(address);
        return new DeliveryAddressSuggestionItem(
            address.DisplayText,
            new DeliveryAddressFields(premise, road, address.City, address.Postcode));
    }

    private static (string Premise, string Road) SplitLookupAddress(AddressSuggestion address)
    {
        var line1 = address.AddressLine1?.Trim() ?? string.Empty;
        var remainingLines = new[] { address.AddressLine2, address.AddressLine3 }
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line!.Trim())
            .ToList();

        if (remainingLines.Count > 0 && IsPremiseOnly(line1))
        {
            return (line1, string.Join(", ", remainingLines));
        }

        var (premise, road) = SplitPremiseAndRoad(line1);
        if (remainingLines.Count > 0)
        {
            road = string.Join(", ", new[] { road }.Concat(remainingLines)
                .Where(line => !string.IsNullOrWhiteSpace(line)));
        }

        return (premise, road);
    }

    private static (string Premise, string Road) SplitPremiseAndRoad(string? streetAddress)
    {
        var value = streetAddress?.Trim().Trim(',') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return (string.Empty, string.Empty);
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            value,
            @"^(?<premise>\d+[A-Za-z]?|[A-Za-z]+\s+\d+[A-Za-z]?|Flat\s+\w+|Apartment\s+\w+|Apt\s+\w+|Unit\s+\w+)\s+(?<road>.+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return (match.Groups["premise"].Value.Trim(), match.Groups["road"].Value.Trim());
        }

        var parts = value.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && IsPremiseOnly(parts[0]))
        {
            return (parts[0], parts[1]);
        }

        return (string.Empty, value);
    }

    private static bool IsPremiseOnly(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            value?.Trim() ?? string.Empty,
            @"^(\d+[A-Za-z]?|Flat\s+\w+|Apartment\s+\w+|Apt\s+\w+|Unit\s+\w+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
