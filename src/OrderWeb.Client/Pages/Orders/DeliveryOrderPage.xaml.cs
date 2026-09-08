using System.Text.RegularExpressions;
using OrderWeb.Client.Models;
using OrderWeb.Client.Services;
using OrderWeb.Client.Views.Dialogs;

namespace OrderWeb.Client.Pages.Orders;

public partial class DeliveryOrderPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private readonly MotherCustomerClient _customerClient = new();
    private readonly MotherOrderClient _orderClient = new();
    private readonly ClientOfflinePolicy _offlinePolicy = new();
    private DeliveryZoneQuote? _deliveryQuote;
    private CachedCustomer? _selectedCustomer;
    private string? _pendingDeliveryOrderId;
    private bool _isOpeningKeyboard;
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

    private async void OnCustomerNameFieldTapped(object sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(CustomerNameEntry);

    private async void OnPhoneNumberFieldTapped(object sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(PhoneNumberEntry);

    private async void OnPostcodeFieldTapped(object sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(PostcodeEntry);

    private async void OnHouseNumberFieldTapped(object sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(HouseNumberEntry);

    private async void OnRoadNameFieldTapped(object sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(RoadNameEntry);

    private async void OnCityFieldTapped(object sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(CityEntry);

    private async void OnPostcodeResultFieldTapped(object sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(PostcodeResultEntry);

    private async Task OpenKeyboardForEntryAsync(Entry entry)
    {
        if (_isOpeningKeyboard)
        {
            return;
        }

        _isOpeningKeyboard = true;
        try
        {
            entry.Unfocus();
            var keyboard = new VirtualKeyboardDialog();
            keyboard.SetPrompt(GetKeyboardTitle(entry), "DONE");
            keyboard.SetInitialText(entry.Text ?? string.Empty);
            var result = await keyboard.ShowAsync(this);
            if (result != null)
            {
                entry.Text = result.Trim();
            }
        }
        finally
        {
            _isOpeningKeyboard = false;
        }
    }

    private string GetKeyboardTitle(Entry entry)
    {
        if (entry == CustomerNameEntry) return "Customer name";
        if (entry == PhoneNumberEntry) return "Phone number";
        if (entry == PostcodeEntry) return "Address search";
        if (entry == HouseNumberEntry) return "Flat or house number";
        if (entry == RoadNameEntry) return "Road name";
        if (entry == CityEntry) return "City";
        if (entry == PostcodeResultEntry) return "Postcode";
        return "Keyboard";
    }

    private async void OnSearchPostcodeClicked(object sender, EventArgs e)
    {
        var term = PostcodeEntry.Text?.Trim();
        var name = CustomerNameEntry.Text?.Trim();
        var phone = PhoneNumberEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(term) &&
            string.IsNullOrWhiteSpace(name) &&
            string.IsNullOrWhiteSpace(phone))
        {
            ShowStatus("Enter a postcode, address, name, or phone to search.", "#DC2626");
            return;
        }

        SearchPostcodeButton.Text = "Searching...";
        SearchPostcodeButton.IsEnabled = false;
        AddressResultsBorder.IsVisible = false;
        SearchResultsBorder.IsVisible = false;
        NoResultsLabel.IsVisible = false;

        try
        {
            var customerResults = await SearchCustomersInternalAsync(name, phone, term);
            if (customerResults.Count > 0)
            {
                SearchResultsCollection.ItemsSource = customerResults;
                SearchResultsBorder.IsVisible = true;
                ShowStatus($"Found {customerResults.Count} customer(s). Select one to fill the form.", "#10B981");
                return;
            }

            if (string.IsNullOrWhiteSpace(term))
            {
                NoResultsLabel.IsVisible = true;
                ShowStatus("No customer match. Enter a postcode to look up addresses.", "#718096");
                return;
            }

            var addresses = await _customerClient.LookupAddressesAsync(term);
            if (addresses.Count > 0)
            {
                AddressResultsCollection.ItemsSource = addresses;
                AddressResultsBorder.IsVisible = true;
                ShowStatus($"Found {addresses.Count} address(es). Select one to fill the form.", "#3B82F6");
            }
            else
            {
                NoResultsLabel.IsVisible = true;
                ShowStatus($"No addresses found for {term}. You can enter the address manually.", "#718096");
            }

            await QuoteDeliveryZoneAsync(false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Search failed: {ex.Message}. You can still enter the address manually.", "#DC2626");
        }
        finally
        {
            SearchPostcodeButton.Text = "Search";
            SearchPostcodeButton.IsEnabled = true;
        }
    }

    private async void OnSearchCustomerClicked(object sender, EventArgs e)
    {
        var original = SearchCustomerButton.Text;
        SearchCustomerButton.Text = "Searching...";
        SearchCustomerButton.IsEnabled = false;
        try
        {
            var results = await SearchCustomersInternalAsync(
                CustomerNameEntry.Text?.Trim(),
                PhoneNumberEntry.Text?.Trim(),
                PostcodeEntry.Text?.Trim());

            AddressResultsBorder.IsVisible = false;
            SearchResultsCollection.ItemsSource = results;
            SearchResultsBorder.IsVisible = results.Count > 0;
            NoResultsLabel.IsVisible = results.Count == 0;
            ShowStatus(
                results.Count == 0 ? "No customers found matching your search." : $"Found {results.Count} customer(s).",
                results.Count == 0 ? "#718096" : "#10B981");
        }
        finally
        {
            SearchCustomerButton.Text = original;
            SearchCustomerButton.IsEnabled = true;
        }
    }

    private async Task<IReadOnlyList<CachedCustomer>> SearchCustomersInternalAsync(string? name, string? phone, string? addressOrPostcode)
    {
        if (string.IsNullOrWhiteSpace(name) &&
            string.IsNullOrWhiteSpace(phone) &&
            string.IsNullOrWhiteSpace(addressOrPostcode))
        {
            ShowStatus("Enter customer name, phone, address, or postcode to search.", "#DC2626");
            return Array.Empty<CachedCustomer>();
        }

        ShowStatus("Searching customers...", "#718096");
        var request = new CustomerSearchRequest("Delivery", name, phone, addressOrPostcode);
        var mother = await _customerClient.SearchCustomersAsync(request);
        return mother
            .GroupBy(customer => string.IsNullOrWhiteSpace(customer.MotherId) ? customer.Id.ToString() : customer.MotherId)
            .Select(group => group.First())
            .Take(10)
            .ToList();
    }

    private void OnAddressSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not AddressSuggestion address)
        {
            return;
        }

        var (premise, road) = SplitLookupAddress(address);
        HouseNumberEntry.Text = premise;
        RoadNameEntry.Text = road;
        CityEntry.Text = address.City;
        PostcodeResultEntry.Text = address.Postcode;
        PostcodeEntry.Text = address.Postcode;
        AddressResultsBorder.IsVisible = false;
        ((CollectionView)sender).SelectedItem = null;
        _ = QuoteDeliveryZoneAsync(true);
    }

    private void OnCustomerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not CachedCustomer customer)
        {
            return;
        }

        ApplyCustomerToForm(customer);
        SearchResultsBorder.IsVisible = false;
        NoResultsLabel.IsVisible = false;
        ((CollectionView)sender).SelectedItem = null;
    }

    private void OnCustomerTapped(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement element && element.BindingContext is CachedCustomer customer)
        {
            ApplyCustomerToForm(customer);
            SearchResultsBorder.IsVisible = false;
            NoResultsLabel.IsVisible = false;
        }
    }

    private void ApplyCustomerToForm(CachedCustomer customer)
    {
        _selectedCustomer = customer;
        CustomerNameEntry.Text = customer.Name;
        PhoneNumberEntry.Text = customer.Phone;
        PostcodeEntry.Text = customer.Postcode ?? customer.Address;

        HouseNumberEntry.Text = string.Empty;
        RoadNameEntry.Text = string.Empty;
        CityEntry.Text = string.Empty;
        PostcodeResultEntry.Text = string.Empty;

        var addressLines = (customer.Address ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (addressLines.Length == 0)
        {
            PostcodeResultEntry.Text = customer.Postcode ?? string.Empty;
            PostcodeEntry.Text = customer.Postcode ?? string.Empty;
        }
        else
        {
            var (premise, road) = SplitPremiseAndRoad(addressLines[0]);
            HouseNumberEntry.Text = premise;
            RoadNameEntry.Text = road;
            if (addressLines.Length >= 3)
            {
                CityEntry.Text = addressLines[1];
            }

            var postcode = addressLines.Length > 1 ? addressLines[^1] : customer.Postcode;
            PostcodeResultEntry.Text = postcode ?? string.Empty;
            PostcodeEntry.Text = postcode ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(customer.Postcode))
        {
            _ = QuoteDeliveryZoneAsync(true);
        }
    }

    private async Task QuoteDeliveryZoneAsync(bool showStatus)
    {
        var postcode = PostcodeResultEntry.Text;
        if (string.IsNullOrWhiteSpace(postcode))
        {
            postcode = PostcodeEntry.Text;
        }

        if (string.IsNullOrWhiteSpace(postcode))
        {
            return;
        }

        _deliveryQuote = await _customerClient.QuoteDeliveryZoneAsync(postcode);
        if (!string.IsNullOrWhiteSpace(_deliveryQuote.Postcode))
        {
            PostcodeResultEntry.Text = _deliveryQuote.Postcode;
        }

        if (showStatus)
        {
            ShowStatus(
                _deliveryQuote.IsKnownZone
                    ? $"{_deliveryQuote.ZoneName}: £{_deliveryQuote.DeliveryFee:F2} delivery fee"
                    : $"No delivery zone found for {_deliveryQuote.Postcode}.",
                _deliveryQuote.IsKnownZone ? "#10B981" : "#B45309");
        }
    }

    private async void OnContinueClicked(object sender, EventArgs e)
    {
        if (_isContinuing)
        {
            return;
        }

        var name = CustomerNameEntry.Text?.Trim();
        var phone = PhoneNumberEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(HouseNumberEntry.Text))
        {
            ShowStatus("Flat or house number is required to continue.", "#DC2626");
            return;
        }

        if (string.IsNullOrWhiteSpace(RoadNameEntry.Text))
        {
            ShowStatus("Road name is required to continue.", "#DC2626");
            return;
        }

        if (string.IsNullOrWhiteSpace(CityEntry.Text))
        {
            ShowStatus("City is required to continue.", "#DC2626");
            return;
        }

        var normalizedPostcode = MotherCustomerClient.NormalizePostcode(PostcodeResultEntry.Text);
        if (string.IsNullOrWhiteSpace(normalizedPostcode))
        {
            ShowStatus("Full postcode is required for delivery zone pricing.", "#DC2626");
            return;
        }

        name = string.IsNullOrWhiteSpace(name) ? "Delivery Customer" : name;
        phone = string.IsNullOrWhiteSpace(phone) ? "N/A" : phone;

        if (_deliveryQuote is null)
        {
            await QuoteDeliveryZoneAsync(false);
        }

        var address = string.Join("\n", new[]
        {
            $"{HouseNumberEntry.Text.Trim()} {RoadNameEntry.Text.Trim()}".Trim(),
            CityEntry.Text.Trim(),
            normalizedPostcode
        });

        var customer = _selectedCustomer ?? new CachedCustomer(0, string.Empty, name, phone, null, address, normalizedPostcode, 0);
        customer = customer with
        {
            Name = name,
            Phone = phone,
            Address = address,
            Postcode = normalizedPostcode
        };

        var draft = new CustomerOrderDraft(
            "Delivery",
            customer,
            null,
            "ASAP",
            string.Empty,
            address,
            normalizedPostcode,
            _deliveryQuote?.ZoneName,
            _deliveryQuote?.DeliveryFee ?? 0m);

        var button = sender as Button;
        var originalText = button?.Text;
        _isContinuing = true;
        if (button is not null)
        {
            button.IsEnabled = false;
            button.Text = "Opening order...";
        }

        try
        {
            var decision = _offlinePolicy.Evaluate(ClientOperation.SaveCollectionOrder, await _offlinePolicy.IsMotherOnlineAsync());
            if (!decision.Allowed)
            {
                ShowStatus(decision.Message, "#DC2626");
                return;
            }

            ShowStatus("Saving customer with Mother POS...", "#718096");
            var savedCustomer = await _customerClient.SaveCustomerAsync(draft);
            await _cache.CacheCustomerForActiveOrderAsync(savedCustomer, isDelivery: true);

            ShowStatus("Opening delivery order...", "#718096");
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
                ShowStatus(orderResult.Message, "#D97706");
            }

            await Navigation.PushAsync(
                new OrderPage(orderResult.State, savedCustomer.Name, savedCustomer.Phone),
                false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to continue: {ex.Message}", "#DC2626");
        }
        finally
        {
            _isContinuing = false;
            if (button is not null)
            {
                button.Text = originalText ?? "Continue to Order";
                button.IsEnabled = true;
            }
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e) => await CloseAsync();

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

    private void ShowStatus(string message, string color)
    {
        StatusLabel.Text = message;
        StatusLabel.TextColor = Color.FromArgb(color);
        StatusLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
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

        var match = Regex.Match(value, @"^(?<premise>\d+[A-Za-z]?|[A-Za-z]+\s+\d+[A-Za-z]?|Flat\s+\w+|Apartment\s+\w+|Apt\s+\w+|Unit\s+\w+)\s+(?<road>.+)$", RegexOptions.IgnoreCase);
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
        Regex.IsMatch(value?.Trim() ?? string.Empty,
            @"^(\d+[A-Za-z]?|Flat\s+\w+|Apartment\s+\w+|Apt\s+\w+|Unit\s+\w+)$",
            RegexOptions.IgnoreCase);
}
