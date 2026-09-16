using System.Globalization;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Host-neutral Collection customer entry screen (Mother CollectionCustomerModal / Client CollectionOrderPage
/// parity). Does not call HTTP/DB — the host wires <see cref="SearchRequested"/> / <see cref="ContinueRequested"/>
/// to its own customer service and feeds results back via <see cref="SetSearchResults"/>.
/// </summary>
public partial class CollectionCustomerEntryView : ContentView
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

    public CollectionCustomerEntryView()
    {
        InitializeComponent();
        _selectedTime = RoundUpToNextFiveMinutes(DateTime.Now.TimeOfDay);
        _pendingTime = _selectedTime;
        SetPendingTimePartsFrom(_pendingTime);
        RefreshScheduleDisplays();
    }

    /// <summary>Raised when the host should search for an existing customer by (name, phone).</summary>
    public event EventHandler<(string? Name, string? Phone)>? SearchRequested;

    /// <summary>Raised when the form is valid and the host should save the customer and open the order.</summary>
    public event EventHandler<CustomerEntryResult>? ContinueRequested;

    /// <summary>Raised when the user cancels out of this screen.</summary>
    public event EventHandler? CancelRequested;

    public string GetName() => CustomerNameEntry.Text?.Trim() ?? string.Empty;

    public string GetPhone() => PhoneNumberEntry.Text?.Trim() ?? string.Empty;

    public void SetSearchResults(IEnumerable<CustomerEntrySearchItem> results)
    {
        var list = results?.ToList() ?? new List<CustomerEntrySearchItem>();
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

    public void SetSearchBusy(bool busy)
    {
        SearchButton.IsEnabled = !busy;
        SearchButton.Text = busy ? "Searching..." : "Search Existing Customer";
    }

    public void SetContinueBusy(bool busy, string? text = null)
    {
        ContinueButton.IsEnabled = !busy;
        ContinueButton.Text = busy ? (text ?? "Opening order...") : "Continue to Order";
    }

    public void ApplyCustomer(CustomerEntrySearchItem item)
    {
        _selectedCustomer = item;
        CustomerNameEntry.Text = item.Name;
        PhoneNumberEntry.Text = item.Phone;
        SearchResultsBorder.IsVisible = false;
        NoResultsLabel.IsVisible = false;
        ShowStatus("Existing customer selected.", "#10B981");
    }

    private async void OnCustomerNameFieldTapped(object? sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(CustomerNameEntry, "Customer name", numericOnly: false);

    private async void OnPhoneNumberFieldTapped(object? sender, TappedEventArgs e) =>
        await OpenKeyboardForEntryAsync(PhoneNumberEntry, "Phone number", numericOnly: true);

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
                : VirtualKeyboardTextMode.Name);
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

    private void OnSearchClicked(object? sender, EventArgs e) =>
        SearchRequested?.Invoke(this, (GetName(), GetPhone()));

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

    private void OnCustomerTapped(object? sender, EventArgs e)
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
        ShowStatus(string.Empty, "#64748B");
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
            ShowStatus("Date set — please choose a collection time.", "#B45309");
            OnTimeFieldTapped(sender, new TappedEventArgs(null));
        }
        else
        {
            ShowStatus(string.Empty, "#64748B");
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
            ShowStatus("Time set — please choose a collection date.", "#B45309");
            OnDateFieldTapped(sender, new TappedEventArgs(null));
        }
        else
        {
            ShowStatus(string.Empty, "#64748B");
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
            DateDisplayLabel.TextColor = Color.FromArgb("#111827");
        }
        else
        {
            DateDisplayLabel.Text = "ASAP";
            DateDisplayLabel.TextColor = Color.FromArgb("#94A3B8");
        }

        if (_hasTime)
        {
            TimeDisplayLabel.Text = DateTime.Today.Add(_selectedTime).ToString("HH:mm", CultureInfo.InvariantCulture);
            TimeDisplayLabel.TextColor = Color.FromArgb("#111827");
        }
        else
        {
            TimeDisplayLabel.Text = "ASAP";
            TimeDisplayLabel.TextColor = Color.FromArgb("#94A3B8");
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
                BackgroundColor = isSelected ? Color.FromArgb("#10B981") : inMonth ? Colors.White : Color.FromArgb("#F8FAFC"),
                TextColor = isPast ? Color.FromArgb("#CBD5E1") : isSelected ? Colors.White : inMonth ? Color.FromArgb("#0F172A") : Color.FromArgb("#94A3B8"),
                BorderColor = isSelected ? Color.FromArgb("#10B981") : date.Date == DateTime.Today ? Color.FromArgb("#6EE7B7") : Color.FromArgb("#E2E8F0"),
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
            BackgroundColor = isSelected ? Color.FromArgb("#10B981") : Colors.White,
            TextColor = isSelected ? Colors.White : Color.FromArgb("#0F172A"),
            BorderColor = isSelected ? Color.FromArgb("#10B981") : Color.FromArgb("#E2E8F0")
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

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowStatus("Customer Name is required to continue.", "#DC2626");
            return;
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            ShowStatus("Phone Number is required to continue.", "#DC2626");
            return;
        }

        if (_hasDate && !_hasTime)
        {
            ShowStatus("Please choose a collection time (or clear the date for ASAP).", "#DC2626");
            OnTimeFieldTapped(sender, new TappedEventArgs(null));
            return;
        }

        if (_hasTime && !_hasDate)
        {
            ShowStatus("Please choose a collection date (or clear the time for ASAP).", "#DC2626");
            OnDateFieldTapped(sender, new TappedEventArgs(null));
            return;
        }

        DateTime? scheduledTime = null;
        if (_hasDate && _hasTime)
        {
            scheduledTime = _selectedDate.Date.Add(_selectedTime);
            if (scheduledTime.Value < DateTime.Now.AddMinutes(-1))
            {
                ShowStatus("Collection date/time must be in the future.", "#DC2626");
                return;
            }
        }

        ContinueRequested?.Invoke(
            this,
            new CustomerEntryResult(name, phone, _selectedCustomer?.MotherId, scheduledTime));
    }

    private void OnCancelClicked(object? sender, EventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);
}
