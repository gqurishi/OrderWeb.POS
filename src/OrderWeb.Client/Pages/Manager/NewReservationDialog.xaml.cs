using System.Globalization;
using OrderWeb.Client.Services;

namespace OrderWeb.Client.Pages.Manager;

public partial class NewReservationDialog : ContentView
{
    private TaskCompletionSource<MotherReservationDraft?>? _taskCompletionSource;
    private Grid? _parentGrid;
    private DateTime _selectedDate;
    private DateTime _pickerMonth;
    private TimeSpan _selectedTime;
    private TimeSpan _pendingTime;
    private int _pendingHour24;
    private int _pendingMinute;
    private readonly int[] _minuteOptions = [0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55];

    public NewReservationDialog(DateTime defaultDate, TimeSpan? defaultTime = null)
    {
        InitializeComponent();
        var today = DateTime.Today;
        _selectedDate = defaultDate.Date < today ? today : defaultDate.Date;
        _pickerMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        _selectedTime = defaultTime ?? RoundUpToNextFiveMinutes(DateTime.Now.TimeOfDay);
        _pendingTime = _selectedTime;
        SetPendingTimePartsFrom(_pendingTime);
        RefreshDateTimeDisplays();
        BuildDateOptions();
        BuildTimeOptions();
    }

    public async Task<MotherReservationDraft?> ShowAsync(Grid host)
    {
        _taskCompletionSource = new TaskCompletionSource<MotherReservationDraft?>();
        _parentGrid = host;
        Grid.SetRowSpan(this, host.RowDefinitions.Count > 0 ? host.RowDefinitions.Count : 1);
        Grid.SetColumnSpan(this, host.ColumnDefinitions.Count > 0 ? host.ColumnDefinitions.Count : 1);
        host.Children.Add(this);
        return await _taskCompletionSource.Task;
    }

    private void OnCancelClicked(object sender, EventArgs e)
    {
        _taskCompletionSource?.TrySetResult(null);
        CloseDialog();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameEntry.Text))
        {
            await DisplayAlertAsync("Required", "Please enter a customer name.");
            return;
        }

        if (!int.TryParse(CoversEntry.Text?.Trim(), out var covers) || covers <= 0)
        {
            await DisplayAlertAsync("Required", "Covers must be at least 1.");
            return;
        }

        var selectedDateTime = _selectedDate.Date.Add(_selectedTime);
        if (selectedDateTime < DateTime.Now.AddMinutes(-1))
        {
            await DisplayAlertAsync("Booking Time", "Please choose a future date and time.");
            return;
        }

        _taskCompletionSource?.TrySetResult(new MotherReservationDraft
        {
            ReservationDate = _selectedDate,
            ReservationTime = _selectedTime,
            Covers = covers,
            CustomerName = NameEntry.Text.Trim(),
            CustomerPhone = PhoneEntry.Text?.Trim() ?? string.Empty,
            CustomerEmail = EmailEntry.Text?.Trim() ?? string.Empty,
            PromoCode = PromoCodeEntry.Text?.Trim() ?? string.Empty,
            TableNumber = TableEntry.Text?.Trim() ?? string.Empty,
            Notes = NotesEditor.Text?.Trim() ?? string.Empty,
            Allergies = AllergiesEditor.Text?.Trim() ?? string.Empty,
            Channel = "pos"
        });
        CloseDialog();
    }

    private void OnDateFieldTapped(object sender, TappedEventArgs e)
    {
        _pickerMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        BuildDateOptions();
        ShowPicker(showDate: true);
    }

    private void OnTimeFieldTapped(object sender, TappedEventArgs e)
    {
        _pendingTime = _selectedTime;
        SetPendingTimePartsFrom(_pendingTime);
        RefreshPendingTimeDisplay();
        BuildTimeOptions();
        ShowPicker(showDate: false);
    }

    private void OnPreviousMonthClicked(object sender, EventArgs e)
    {
        var todayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        if (_pickerMonth <= todayMonth)
        {
            return;
        }

        _pickerMonth = _pickerMonth.AddMonths(-1);
        BuildDateOptions();
    }

    private void OnNextMonthClicked(object sender, EventArgs e)
    {
        if (_pickerMonth >= DateTime.Today.AddYears(3))
        {
            return;
        }

        _pickerMonth = _pickerMonth.AddMonths(1);
        BuildDateOptions();
    }

    private void OnDateOptionClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not DateTime date)
        {
            return;
        }

        _selectedDate = date.Date;
        RefreshDateTimeDisplays();
        BuildDateOptions();
        HidePicker();
    }

    private void OnHourOptionClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not int hour)
        {
            return;
        }

        _pendingHour24 = hour;
        UpdatePendingTimeFromParts();
        RefreshPendingTimeDisplay();
        BuildTimeOptions();
    }

    private void OnMinuteOptionClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not int minute)
        {
            return;
        }

        _pendingMinute = minute;
        UpdatePendingTimeFromParts();
        RefreshPendingTimeDisplay();
        BuildTimeOptions();
    }

    private void OnPickerCancelClicked(object sender, EventArgs e)
    {
        HidePicker();
    }

    private void OnPickerDoneClicked(object sender, EventArgs e)
    {
        _selectedTime = _pendingTime;
        RefreshDateTimeDisplays();
        HidePicker();
    }

    private void ShowPicker(bool showDate)
    {
        DatePickerPanel.IsVisible = showDate;
        TimePickerPanel.IsVisible = !showDate;
        PickerOverlay.IsVisible = true;
    }

    private void HidePicker()
    {
        PickerOverlay.IsVisible = false;
        DatePickerPanel.IsVisible = false;
        TimePickerPanel.IsVisible = false;
    }

    private void RefreshDateTimeDisplays()
    {
        DateDisplayLabel.Text = _selectedDate.ToString("ddd, dd MMM yyyy", CultureInfo.InvariantCulture);
        TimeDisplayLabel.Text = DateTime.Today.Add(_selectedTime).ToString("HH:mm", CultureInfo.InvariantCulture);
        RefreshPendingTimeDisplay();
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
            var isSelected = date.Date == _selectedDate.Date;
            var button = new Button
            {
                Text = date.Day.ToString(CultureInfo.InvariantCulture),
                CommandParameter = date,
                HeightRequest = 38,
                Padding = new Thickness(0),
                CornerRadius = 12,
                BorderWidth = 1,
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
            Text = text,
            CommandParameter = parameter,
            HeightRequest = 42,
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

    private void CloseDialog()
    {
        InputTransparent = true;
        IsVisible = false;
        _parentGrid?.Children.Remove(this);
    }

    private static Task DisplayAlertAsync(string title, string message)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page
                   ?? Application.Current?.MainPage;
        return page?.DisplayAlert(title, message, "OK") ?? Task.CompletedTask;
    }
}
