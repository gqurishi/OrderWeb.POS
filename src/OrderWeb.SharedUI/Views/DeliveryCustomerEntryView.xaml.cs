using System.Globalization;
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
    private bool _hasDate;
    private bool _hasTime;
    private DateTime _selectedDate = DateTime.Today;
    private DateTime _pickerMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private TimeSpan _selectedTime;
    private TimeSpan _pendingTime;
    private int _pendingHour24;
    private int _pendingMinute;
    private readonly int[] _minuteOptions = [0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55];

    public DeliveryCustomerEntryView()
    {
        InitializeComponent();
        _selectedTime = RoundUpToNextFiveMinutes(DateTime.Now.TimeOfDay);
        _pendingTime = _selectedTime;
        SetPendingTimePartsFrom(_pendingTime);
        RefreshScheduleDisplays();
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
            keyboard.SetTextMode(numericOnly
                ? VirtualKeyboardTextMode.Phone
                : title.Contains("name", StringComparison.OrdinalIgnoreCase)
                    ? VirtualKeyboardTextMode.Name
                    : VirtualKeyboardTextMode.Address);
            keyboard.SetPrompt(title, "DONE");
            keyboard.SetPlaceholder(entry.Placeholder);
            keyboard.SetMaximumLength(entry.MaxLength == int.MaxValue ? 0 : entry.MaxLength);
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

    private void OnDateFieldTapped(object? sender, TappedEventArgs e)
    {
        if (!_hasDate)
        {
            _selectedDate = DateTime.Today;
        }

        _pickerMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        BuildDateOptions();
        ShowPicker(showDate: true);
    }

    private void OnTimeFieldTapped(object? sender, TappedEventArgs e)
    {
        if (!_hasTime)
        {
            _selectedTime = RoundUpToNextFiveMinutes(DateTime.Now.TimeOfDay);
        }

        _pendingTime = _selectedTime;
        SetPendingTimePartsFrom(_pendingTime);
        RefreshPendingTimeDisplay();
        BuildTimeOptions();
        ShowPicker(showDate: false);
    }

    private void OnClearScheduleClicked(object? sender, EventArgs e)
    {
        _hasDate = false;
        _hasTime = false;
        RefreshScheduleDisplays();
        ShowStatus(string.Empty, "#718096");
    }

    private void OnPreviousMonthClicked(object? sender, EventArgs e)
    {
        var todayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        if (_pickerMonth <= todayMonth)
        {
            return;
        }

        _pickerMonth = _pickerMonth.AddMonths(-1);
        BuildDateOptions();
    }

    private void OnNextMonthClicked(object? sender, EventArgs e)
    {
        _pickerMonth = _pickerMonth.AddMonths(1);
        BuildDateOptions();
    }

    private void OnDateOptionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: DateTime date } || date.Date < DateTime.Today)
        {
            return;
        }

        _selectedDate = date.Date;
        _hasDate = true;
        RefreshScheduleDisplays();
        HidePicker();

        if (!_hasTime)
        {
            ShowStatus("Date set — please choose a delivery time.", "#B45309");
            OnTimeFieldTapped(sender, new TappedEventArgs(null));
        }
        else
        {
            ShowStatus(string.Empty, "#718096");
        }
    }

    private void OnHourOptionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: int hour })
        {
            return;
        }

        _pendingHour24 = hour;
        UpdatePendingTimeFromParts();
        RefreshPendingTimeDisplay();
        BuildTimeOptions();
    }

    private void OnMinuteOptionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: int minute })
        {
            return;
        }

        _pendingMinute = minute;
        UpdatePendingTimeFromParts();
        RefreshPendingTimeDisplay();
        BuildTimeOptions();
    }

    private void OnPickerDoneClicked(object? sender, EventArgs e)
    {
        _selectedTime = _pendingTime;
        _hasTime = true;
        RefreshScheduleDisplays();
        HidePicker();

        if (!_hasDate)
        {
            ShowStatus("Time set — please choose a delivery date.", "#B45309");
            OnDateFieldTapped(sender, new TappedEventArgs(null));
        }
        else
        {
            ShowStatus(string.Empty, "#718096");
        }
    }

    private void OnPickerCancelClicked(object? sender, EventArgs e) => HidePicker();

    private void ShowPicker(bool showDate)
    {
        DatePickerPanel.IsVisible = showDate;
        TimePickerPanel.IsVisible = !showDate;
        PickerOverlay.InputTransparent = false;
        PickerOverlay.IsVisible = true;
    }

    private void HidePicker()
    {
        PickerOverlay.IsVisible = false;
        PickerOverlay.InputTransparent = true;
        DatePickerPanel.IsVisible = false;
        TimePickerPanel.IsVisible = false;
    }

    private void RefreshScheduleDisplays()
    {
        if (_hasDate)
        {
            DateDisplayLabel.Text = _selectedDate.ToString("ddd, dd MMM yyyy", CultureInfo.InvariantCulture);
            DateDisplayLabel.TextColor = Color.FromArgb("#1A202C");
        }
        else
        {
            DateDisplayLabel.Text = "ASAP";
            DateDisplayLabel.TextColor = Color.FromArgb("#A0AEC0");
        }

        if (_hasTime)
        {
            TimeDisplayLabel.Text = DateTime.Today.Add(_selectedTime).ToString("HH:mm", CultureInfo.InvariantCulture);
            TimeDisplayLabel.TextColor = Color.FromArgb("#1A202C");
        }
        else
        {
            TimeDisplayLabel.Text = "ASAP";
            TimeDisplayLabel.TextColor = Color.FromArgb("#A0AEC0");
        }

        ClearScheduleButton.IsVisible = _hasDate || _hasTime;
    }

    private void RefreshPendingTimeDisplay()
    {
        PickerTimePreviewLabel.Text = DateTime.Today.Add(_pendingTime).ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private void BuildDateOptions()
    {
        PickerMonthLabel.Text = _pickerMonth.ToString("MMM yyyy", CultureInfo.InvariantCulture);
        CalendarDaysGrid.Children.Clear();

        var firstOfMonth = _pickerMonth;
        var firstDayOffset = ((int)firstOfMonth.DayOfWeek + 6) % 7;
        var gridStart = firstOfMonth.AddDays(-firstDayOffset);

        for (var index = 0; index < 42; index++)
        {
            var date = gridStart.AddDays(index);
            var inMonth = date.Month == _pickerMonth.Month;
            var isPast = date.Date < DateTime.Today;
            var isSelected = _hasDate && date.Date == _selectedDate.Date;
            var button = new Button
            {
                Style = null,
                Text = date.Day.ToString(CultureInfo.InvariantCulture),
                CommandParameter = date,
                HeightRequest = 38,
                MinimumHeightRequest = 38,
                MinimumWidthRequest = 0,
                Padding = new Thickness(0),
                CornerRadius = 12,
                BorderWidth = 1,
                FontSize = 13,
                BackgroundColor = isSelected ? Color.FromArgb("#2563EB") : inMonth ? Colors.White : Color.FromArgb("#F8FAFC"),
                TextColor = isPast ? Color.FromArgb("#CBD5E1") : isSelected ? Colors.White : inMonth ? Color.FromArgb("#0F172A") : Color.FromArgb("#94A3B8"),
                BorderColor = isSelected ? Color.FromArgb("#2563EB") : date.Date == DateTime.Today ? Color.FromArgb("#93C5FD") : Color.FromArgb("#E2E8F0"),
                FontAttributes = isSelected || date.Date == DateTime.Today ? FontAttributes.Bold : FontAttributes.None,
                IsEnabled = !isPast
            };
            button.Clicked += OnDateOptionClicked;
            Grid.SetColumn(button, index % 7);
            Grid.SetRow(button, index / 7);
            CalendarDaysGrid.Children.Add(button);
        }
    }

    private void BuildTimeOptions()
    {
        HourOptionsStack.Children.Clear();
        MinuteOptionsStack.Children.Clear();

        for (var hour = 0; hour <= 23; hour++)
        {
            HourOptionsStack.Children.Add(CreateTimeOptionButton(
                hour.ToString("00", CultureInfo.InvariantCulture),
                hour,
                hour == _pendingHour24,
                OnHourOptionClicked));
        }

        foreach (var minute in _minuteOptions)
        {
            MinuteOptionsStack.Children.Add(CreateTimeOptionButton(
                minute.ToString("00", CultureInfo.InvariantCulture),
                minute,
                minute == _pendingMinute,
                OnMinuteOptionClicked));
        }
    }

    private static Button CreateTimeOptionButton(string text, object parameter, bool isSelected, EventHandler clicked)
    {
        var button = new Button
        {
            Style = null,
            Text = text,
            CommandParameter = parameter,
            HeightRequest = 42,
            MinimumHeightRequest = 42,
            MinimumWidthRequest = 0,
            Padding = new Thickness(0),
            CornerRadius = 11,
            BorderWidth = 1,
            FontSize = 16,
            FontAttributes = isSelected ? FontAttributes.Bold : FontAttributes.None,
            BackgroundColor = isSelected ? Color.FromArgb("#2563EB") : Colors.White,
            TextColor = isSelected ? Colors.White : Color.FromArgb("#0F172A"),
            BorderColor = isSelected ? Color.FromArgb("#2563EB") : Color.FromArgb("#E2E8F0")
        };
        button.Clicked += clicked;
        return button;
    }

    private void SetPendingTimePartsFrom(TimeSpan time)
    {
        _pendingHour24 = time.Hours;
        _pendingMinute = _minuteOptions
            .OrderBy(minute => Math.Abs(minute - time.Minutes))
            .First();
        UpdatePendingTimeFromParts();
    }

    private void UpdatePendingTimeFromParts()
    {
        _pendingTime = new TimeSpan(_pendingHour24, _pendingMinute, 0);
    }

    private static TimeSpan RoundUpToNextFiveMinutes(TimeSpan time)
    {
        var totalMinutes = (int)Math.Ceiling(time.TotalMinutes / 5d) * 5;
        totalMinutes %= 24 * 60;
        return TimeSpan.FromMinutes(totalMinutes);
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

        if (_hasDate && !_hasTime)
        {
            ShowStatus("Please choose a delivery time (or clear the date for ASAP).", "#DC2626");
            OnTimeFieldTapped(sender, new TappedEventArgs(null));
            return;
        }

        if (_hasTime && !_hasDate)
        {
            ShowStatus("Please choose a delivery date (or clear the time for ASAP).", "#DC2626");
            OnDateFieldTapped(sender, new TappedEventArgs(null));
            return;
        }

        DateTime? scheduledTime = null;
        if (_hasDate && _hasTime)
        {
            scheduledTime = _selectedDate.Date.Add(_selectedTime);
            if (scheduledTime.Value < DateTime.Now.AddMinutes(-1))
            {
                ShowStatus("Delivery date/time must be in the future.", "#DC2626");
                return;
            }
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
            new DeliveryCustomerEntryResult(
                name,
                phone,
                GetHouseNumber(),
                GetRoadName(),
                GetCity(),
                normalizedPostcode,
                formattedAddress,
                scheduledTime));
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
