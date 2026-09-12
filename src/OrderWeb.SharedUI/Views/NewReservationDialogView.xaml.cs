using System.Globalization;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

public partial class NewReservationDialogView : ContentView
{
    private DateTime _selectedDate = DateTime.Today;
    private DateTime _pickerMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private TimeSpan _selectedTime;
    private TimeSpan _pendingTime;
    private int _pendingHour24;
    private int _pendingMinute;
    private readonly int[] _minuteOptions = [0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55];

    public NewReservationDialogView()
    {
        InitializeComponent();
        HookTouchFields();
        Prepare(DateTime.Today, null);
    }

    public event EventHandler? Cancelled;
    public event EventHandler<NewReservationDraft>? Submitted;

    /// <summary>Host-owned alert. Falls back to a page alert when unset.</summary>
    public Func<string, string, Task>? AlertAsync { get; set; }

    public void Prepare(DateTime defaultDate, TimeSpan? defaultTime)
    {
        var today = DateTime.Today;
        _selectedDate = defaultDate.Date < today ? today : defaultDate.Date;
        _pickerMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        _selectedTime = defaultTime ?? RoundUpToNextFiveMinutes(DateTime.Now.TimeOfDay);
        _pendingTime = _selectedTime;
        SetPendingTimePartsFrom(_pendingTime);
        RefreshDateTimeDisplays();
        BuildDateOptions();
        BuildTimeOptions();
        HidePicker();
    }

    private void OnCancelClicked(object? sender, EventArgs e) => Cancelled?.Invoke(this, EventArgs.Empty);

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameEntry.Text))
        {
            await ShowAlertAsync("Required", "Please enter a customer name.");
            return;
        }

        if (!int.TryParse(CoversEntry.Text?.Trim(), out var covers) || covers <= 0)
        {
            await ShowAlertAsync("Required", "Covers must be at least 1.");
            return;
        }

        var selectedDateTime = _selectedDate.Date.Add(_selectedTime);
        if (selectedDateTime < DateTime.Now.AddMinutes(-1))
        {
            await ShowAlertAsync("Booking Time", "Please choose a future date and time.");
            return;
        }

        Submitted?.Invoke(this, new NewReservationDraft
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
            Allergies = AllergiesEditor.Text?.Trim() ?? string.Empty
        });
    }

    private void OnDateFieldTapped(object? sender, TappedEventArgs e)
    {
        _pickerMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        BuildDateOptions();
        ShowPicker(showDate: true);
    }

    private void OnTimeFieldTapped(object? sender, TappedEventArgs e)
    {
        _pendingTime = _selectedTime;
        SetPendingTimePartsFrom(_pendingTime);
        RefreshPendingTimeDisplay();
        BuildTimeOptions();
        ShowPicker(showDate: false);
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
        if (_pickerMonth >= DateTime.Today.AddYears(3))
        {
            return;
        }

        _pickerMonth = _pickerMonth.AddMonths(1);
        BuildDateOptions();
    }

    private void OnDateOptionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: DateTime date })
        {
            return;
        }

        _selectedDate = date.Date;
        RefreshDateTimeDisplays();
        BuildDateOptions();
        HidePicker();
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

    private void OnPickerCancelClicked(object? sender, EventArgs e) => HidePicker();

    private void OnPickerDoneClicked(object? sender, EventArgs e)
    {
        _selectedTime = _pendingTime;
        RefreshDateTimeDisplays();
        HidePicker();
    }

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

    private bool _keyboardOpen;

    private void HookTouchFields()
    {
        HookField(CoversEntry);
        HookField(TableEntry);
        HookField(NameEntry);
        HookField(PhoneEntry);
        HookField(EmailEntry);
        HookField(PromoCodeEntry);
        HookField(AllergiesEditor);
        HookField(NotesEditor);
    }

    private void HookField(VisualElement field)
    {
        SharedTouchKeyboard.SetEnabled(field, false);
        field.HandlerChanged += (_, _) => SharedTouchKeyboard.SetEnabled(field, false);
        if (field is Entry entry)
        {
            entry.Focused += async (_, _) => await OpenFieldKeyboardAsync(field);
        }
        else if (field is Editor editor)
        {
            editor.Focused += async (_, _) => await OpenFieldKeyboardAsync(field);
        }

        if (field.Parent is not Border border)
        {
            return;
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenFieldKeyboardAsync(field);
        border.GestureRecognizers.Add(tap);
    }

    private async Task OpenFieldKeyboardAsync(VisualElement field)
    {
        if (_keyboardOpen || !field.IsEnabled)
        {
            return;
        }

        _keyboardOpen = true;
        try
        {
            if (field is Entry entry)
            {
                entry.Unfocus();
            }
            else if (field is Editor editor)
            {
                editor.Unfocus();
            }

            await Task.Delay(30);

            var keyboard = new VirtualKeyboardDialog();
            var title = FieldTitle(field);
            keyboard.SetPrompt(title, "Done");
            keyboard.SetRequired(false);

            if (field is Entry covers && ReferenceEquals(covers, CoversEntry))
            {
                keyboard.SetPlaceholder(covers.Placeholder ?? "2");
                keyboard.SetMaximumLength(covers.MaxLength == int.MaxValue ? 3 : Math.Max(covers.MaxLength, 1));
                keyboard.SetNumericMode(VirtualKeyboardNumericMode.Quantity, minimum: 1, maximum: 999);
                keyboard.SetInitialText(covers.Text ?? string.Empty);
            }
            else if (field is Entry phone && ReferenceEquals(phone, PhoneEntry))
            {
                keyboard.SetTextMode(VirtualKeyboardTextMode.Phone);
                keyboard.SetPlaceholder(phone.Placeholder ?? "07...");
                keyboard.SetMaximumLength(phone.MaxLength == int.MaxValue ? 20 : Math.Max(phone.MaxLength, 1));
                keyboard.SetInitialText(phone.Text ?? string.Empty);
            }
            else if (field is Entry email && ReferenceEquals(email, EmailEntry))
            {
                keyboard.SetTextMode(VirtualKeyboardTextMode.Email);
                keyboard.SetPlaceholder(email.Placeholder ?? "you@example.com");
                keyboard.SetMaximumLength(email.MaxLength == int.MaxValue ? 80 : Math.Max(email.MaxLength, 1));
                keyboard.SetInitialText(email.Text ?? string.Empty);
            }
            else if (field is Entry textEntry)
            {
                keyboard.SetTextMode(ReferenceEquals(textEntry, NameEntry) ? VirtualKeyboardTextMode.Name : VirtualKeyboardTextMode.Text);
                keyboard.SetPlaceholder(textEntry.Placeholder ?? "Type here");
                keyboard.SetMaximumLength(textEntry.MaxLength == int.MaxValue ? 80 : Math.Max(textEntry.MaxLength, 1));
                keyboard.SetInitialText(textEntry.Text ?? string.Empty);
            }
            else if (field is Editor notes)
            {
                keyboard.SetTextMode(VirtualKeyboardTextMode.Notes);
                keyboard.SetPlaceholder(notes.Placeholder ?? "Type here");
                keyboard.SetMaximumLength(notes.MaxLength == int.MaxValue ? 240 : Math.Max(notes.MaxLength, 1));
                keyboard.SetInitialText(notes.Text ?? string.Empty);
            }

            var result = await keyboard.ShowOverAsync(DialogRoot, FindHostPage());
            if (result is null)
            {
                return;
            }

            var value = result.Trim();
            if (field is Entry targetEntry)
            {
                targetEntry.Text = value;
            }
            else if (field is Editor targetEditor)
            {
                targetEditor.Text = value;
            }
        }
        finally
        {
            _keyboardOpen = false;
        }
    }

    private string FieldTitle(VisualElement field) => field switch
    {
        _ when ReferenceEquals(field, CoversEntry) => "Covers",
        _ => field switch
        {
            Entry entry when !string.IsNullOrWhiteSpace(entry.Placeholder) => entry.Placeholder,
            Editor editor when !string.IsNullOrWhiteSpace(editor.Placeholder) => editor.Placeholder,
            Editor => "Notes",
            _ => "Enter text"
        }
    };

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

    private Task ShowAlertAsync(string title, string message)
    {
        if (AlertAsync != null)
        {
            return AlertAsync(title, message);
        }

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        return page?.DisplayAlertAsync(title, message, "OK") ?? Task.CompletedTask;
    }
}
