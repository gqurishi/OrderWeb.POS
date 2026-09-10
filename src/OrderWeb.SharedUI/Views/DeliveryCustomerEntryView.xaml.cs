using System.Text.RegularExpressions;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Host-neutral Delivery customer entry screen (Mother DeliveryCustomerModal / Client DeliveryOrderPage
/// parity). Does not call HTTP/DB — the host wires <see cref="AddressSearchRequested"/> /
/// <see cref="CustomerSearchRequested"/> / <see cref="ContinueRequested"/> to its own services and feeds
/// results back via <see cref="SetAddressSuggestions"/> / <see cref="SetCustomerResults"/>.
/// </summary>
public partial class DeliveryCustomerEntryView : ContentView
{
    private CustomerEntrySearchItem? _selectedCustomer;
    private bool _isOpeningKeyboard;

    public DeliveryCustomerEntryView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the host should look up addresses (and/or customers) for the given search term.</summary>
    public event EventHandler<(string? Term, string? Name, string? Phone)>? AddressSearchRequested;

    /// <summary>Raised when the host should search for an existing customer.</summary>
    public event EventHandler<(string? Name, string? Phone, string? AddressOrPostcode)>? CustomerSearchRequested;

    /// <summary>Raised when the form is valid and the host should save the customer and open the order.</summary>
    public event EventHandler<DeliveryCustomerEntryResult>? ContinueRequested;

    /// <summary>Raised when the user cancels out of this screen.</summary>
    public event EventHandler? CancelRequested;

    public string GetName() => CustomerNameEntry.Text?.Trim() ?? string.Empty;

    public string GetPhone() => PhoneNumberEntry.Text?.Trim() ?? string.Empty;

    public string GetHouseNumber() => HouseNumberEntry.Text?.Trim() ?? string.Empty;

    public string GetRoadName() => RoadNameEntry.Text?.Trim() ?? string.Empty;

    public string GetCity() => CityEntry.Text?.Trim() ?? string.Empty;

    public string GetPostcode() => PostcodeResultEntry.Text?.Trim() ?? string.Empty;

    public string GetAddressSearchTerm() => PostcodeEntry.Text?.Trim() ?? string.Empty;

    public void SetAddressSearchTerm(string? text) => PostcodeEntry.Text = text ?? string.Empty;

    public void SetPostcode(string? text)
    {
        PostcodeResultEntry.Text = text ?? string.Empty;
        PostcodeEntry.Text = text ?? string.Empty;
    }

    public void SetAddressSuggestions(IEnumerable<DeliveryAddressSuggestionItem> items)
    {
        var list = items?.ToList() ?? new List<DeliveryAddressSuggestionItem>();
        AddressResultsCollection.ItemsSource = list;
        AddressResultsBorder.IsVisible = list.Count > 0;
    }

    public void SetCustomerResults(IEnumerable<CustomerEntrySearchItem> items)
    {
        var list = items?.ToList() ?? new List<CustomerEntrySearchItem>();
        SearchResultsCollection.ItemsSource = list;
        SearchResultsBorder.IsVisible = list.Count > 0;
        NoResultsLabel.IsVisible = list.Count == 0;
    }

    public void ShowStatus(string message, string colorHex)
    {
        StatusLabel.Text = message;
        StatusLabel.TextColor = Color.FromArgb(colorHex);
        StatusLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    public void SetAddressSearchBusy(bool busy)
    {
        SearchPostcodeButton.IsEnabled = !busy;
        SearchPostcodeButton.Text = busy ? "Searching..." : "Search";
    }

    public void SetCustomerSearchBusy(bool busy)
    {
        SearchCustomerButton.IsEnabled = !busy;
        SearchCustomerButton.Text = busy ? "Searching..." : "Search Existing Customer";
    }

    public void SetContinueBusy(bool busy, string? text = null)
    {
        ContinueButton.IsEnabled = !busy;
        ContinueButton.Text = busy ? (text ?? "Opening order...") : "Continue to Order";
    }

    /// <summary>
    /// Applies a selected customer's name/phone, and — when <see cref="CustomerEntrySearchItem.Tag"/> carries
    /// a <see cref="DeliveryCustomerAddressInfo"/> or <see cref="DeliveryAddressFields"/> — the address fields too.
    /// </summary>
    public void ApplyCustomer(CustomerEntrySearchItem item)
    {
        _selectedCustomer = item;
        CustomerNameEntry.Text = item.Name;
        PhoneNumberEntry.Text = item.Phone;

        HouseNumberEntry.Text = string.Empty;
        RoadNameEntry.Text = string.Empty;
        CityEntry.Text = string.Empty;
        PostcodeResultEntry.Text = string.Empty;

        if (item.Tag is DeliveryAddressFields fields)
        {
            HouseNumberEntry.Text = fields.House;
            RoadNameEntry.Text = fields.Road;
            CityEntry.Text = fields.City;
            PostcodeResultEntry.Text = fields.Postcode;
            PostcodeEntry.Text = fields.Postcode;
        }
        else if (item.Tag is DeliveryCustomerAddressInfo info)
        {
            var addressLines = (info.RawAddress ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (addressLines.Length == 0)
            {
                PostcodeResultEntry.Text = info.Postcode ?? string.Empty;
                PostcodeEntry.Text = info.Postcode ?? string.Empty;
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

                var postcode = addressLines.Length > 1 ? addressLines[^1] : info.Postcode;
                PostcodeResultEntry.Text = postcode ?? string.Empty;
                PostcodeEntry.Text = postcode ?? string.Empty;
            }
        }
        else
        {
            PostcodeEntry.Text = string.Empty;
        }

        SearchResultsBorder.IsVisible = false;
        NoResultsLabel.IsVisible = false;
        ShowStatus("Existing customer selected.", "#10B981");
    }

    private async void OnCustomerNameFieldTapped(object? sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(CustomerNameEntry, "Customer name", numericOnly: false);

    private async void OnPhoneNumberFieldTapped(object? sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(PhoneNumberEntry, "Phone number", numericOnly: true);

    private async void OnPostcodeFieldTapped(object? sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(PostcodeEntry, "Address search", numericOnly: false);

    private async void OnHouseNumberFieldTapped(object? sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(HouseNumberEntry, "Flat or house number", numericOnly: false);

    private async void OnRoadNameFieldTapped(object? sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(RoadNameEntry, "Road name", numericOnly: false);

    private async void OnCityFieldTapped(object? sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(CityEntry, "City", numericOnly: false);

    private async void OnPostcodeResultFieldTapped(object? sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(PostcodeResultEntry, "Postcode", numericOnly: false);

    private async Task OpenKeyboardForEntryAsync(Entry entry, string title, bool numericOnly)
    {
        if (_isOpeningKeyboard)
        {
            return;
        }

        _isOpeningKeyboard = true;
        try
        {
            entry.Unfocus();
            var hostPage = FindHostPage();
            if (hostPage is null)
            {
                return;
            }

            var keyboard = new VirtualKeyboardDialog();
            keyboard.SetNumericOnly(numericOnly);
            keyboard.SetPrompt(title, "DONE");
            keyboard.SetInitialText(entry.Text ?? string.Empty);

            var result = await keyboard.ShowAsync(hostPage);
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

    private ContentPage? FindHostPage()
    {
        Element? current = this;
        while (current is not null)
        {
            if (current is ContentPage page)
            {
                return page;
            }

            current = current.Parent;
        }

        return null;
    }

    private void OnSearchPostcodeClicked(object? sender, EventArgs e) =>
        AddressSearchRequested?.Invoke(this, (GetAddressSearchTerm(), GetName(), GetPhone()));

    private void OnSearchCustomerClicked(object? sender, EventArgs e) =>
        CustomerSearchRequested?.Invoke(this, (GetName(), GetPhone(), GetAddressSearchTerm()));

    private void OnAddressSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is DeliveryAddressSuggestionItem address)
        {
            if (address.Tag is DeliveryAddressFields fields)
            {
                HouseNumberEntry.Text = fields.House;
                RoadNameEntry.Text = fields.Road;
                CityEntry.Text = fields.City;
                PostcodeResultEntry.Text = fields.Postcode;
                PostcodeEntry.Text = fields.Postcode;
            }

            AddressResultsBorder.IsVisible = false;
        }

        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }
    }

    private void OnCustomerSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is CustomerEntrySearchItem item)
        {
            ApplyCustomer(item);
        }

        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }
    }

    private void OnCustomerTapped(object? sender, TappedEventArgs e)
    {
        if (sender is VisualElement element && element.BindingContext is CustomerEntrySearchItem item)
        {
            ApplyCustomer(item);
        }
    }

    private void OnContinueClicked(object? sender, EventArgs e)
    {
        var name = GetName();
        var phone = GetPhone();

        if (string.IsNullOrWhiteSpace(GetHouseNumber()))
        {
            ShowStatus("Flat or house number is required to continue.", "#DC2626");
            return;
        }

        if (string.IsNullOrWhiteSpace(GetRoadName()))
        {
            ShowStatus("Road name is required to continue.", "#DC2626");
            return;
        }

        if (string.IsNullOrWhiteSpace(GetCity()))
        {
            ShowStatus("City is required to continue.", "#DC2626");
            return;
        }

        var normalizedPostcode = NormalizePostcode(GetPostcode());
        if (string.IsNullOrWhiteSpace(normalizedPostcode))
        {
            ShowStatus("Full postcode is required for delivery zone pricing.", "#DC2626");
            return;
        }

        name = string.IsNullOrWhiteSpace(name) ? "Delivery Customer" : name;
        phone = string.IsNullOrWhiteSpace(phone) ? "N/A" : phone;

        var formattedAddress = string.Join("\n", new[]
        {
            $"{GetHouseNumber()} {GetRoadName()}".Trim(),
            GetCity(),
            normalizedPostcode
        });

        ContinueRequested?.Invoke(
            this,
            new DeliveryCustomerEntryResult(name, phone, GetHouseNumber(), GetRoadName(), GetCity(), normalizedPostcode, formattedAddress));
    }

    private void OnCancelClicked(object? sender, EventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);

    public static string NormalizePostcode(string? postcode)
    {
        if (string.IsNullOrWhiteSpace(postcode))
        {
            return string.Empty;
        }

        var compact = new string(postcode.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return compact.Length <= 3 ? compact : $"{compact[..^3]} {compact[^3..]}";
    }

    private static (string Premise, string Road) SplitPremiseAndRoad(string? streetAddress)
    {
        var value = streetAddress?.Trim().Trim(',') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return (string.Empty, string.Empty);
        }

        var match = Regex.Match(
            value,
            @"^(?<premise>\d+[A-Za-z]?|[A-Za-z]+\s+\d+[A-Za-z]?|Flat\s+\w+|Apartment\s+\w+|Apt\s+\w+|Unit\s+\w+)\s+(?<road>.+)$",
            RegexOptions.IgnoreCase);
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
        Regex.IsMatch(
            value?.Trim() ?? string.Empty,
            @"^(\d+[A-Za-z]?|Flat\s+\w+|Apartment\s+\w+|Apt\s+\w+|Unit\s+\w+)$",
            RegexOptions.IgnoreCase);
}
