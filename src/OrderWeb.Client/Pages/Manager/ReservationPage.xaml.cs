namespace OrderWeb.Client.Pages.Manager;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;

public partial class ReservationPage : ContentPage, INotifyPropertyChanged
{
    private readonly ClientCacheService _cache = new();
    private readonly MotherReservationClient _motherReservations;
    private readonly IDispatcherTimer _clockTimer;
    private readonly IDispatcherTimer _refreshTimer;
    private readonly List<ReservationRow> _allReservations = new();
    private DateTime _selectedDate = DateTime.Today;
    private DateTime _displayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _lastUpdatedAt = DateTime.Now;
    private string _searchText = string.Empty;
    private string _syncStatusText = "Website sync ready";
    private CancellationTokenSource? _searchDebounceCts;
    private bool _isLoading;
    private bool _isVisible;
    private ReservationRow? _selectedReservation;

    public ReservationPage()
    {
        InitializeComponent();
        BindingContext = this;
        _motherReservations = new MotherReservationClient(_cache);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);

        _clockTimer = Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        _refreshTimer = Dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(20);
        _refreshTimer.Tick += (_, _) =>
        {
            if (_isVisible && !IsLoading)
            {
                _ = LoadReservationsAsync(showBusy: false);
            }
        };

        UpdateClock();
        RefreshCalendar();
    }

    public ObservableCollection<ReservationCalendarDay> CalendarDays { get; } = new();
    public ObservableCollection<ReservationRow> Reservations { get; } = new();

    public string SelectedDateDisplay => _selectedDate.ToString("dddd, dd MMMM yyyy", CultureInfo.InvariantCulture);
    public string CalendarMonthText => _displayedMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
    public string TodayCountText => TodayReservations.Count().ToString(CultureInfo.InvariantCulture);
    public string GuestCountText => TodayReservations.Sum(row => row.Guests).ToString(CultureInfo.InvariantCulture);
    public string NextReservationText => TodayReservations
        .Where(row => row.Date.Add(row.Time.ToTimeSpan()) >= DateTime.Now)
        .OrderBy(row => row.Time)
        .FirstOrDefault()?.TimeText
        ?? TodayReservations.OrderBy(row => row.Time).FirstOrDefault()?.TimeText
        ?? "--";
    public string SyncStatusText => _syncStatusText;
    public string LastUpdatedText => $"Updated {_lastUpdatedAt:HH:mm}";
    public ReservationRow? SelectedReservation
    {
        get => _selectedReservation;
        private set
        {
            if (_selectedReservation == value)
            {
                return;
            }

            _selectedReservation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDetailsVisible));
        }
    }

    public bool IsDetailsVisible => SelectedReservation != null;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;
            OnPropertyChanged();
        }
    }

    private IEnumerable<ReservationRow> TodayReservations => _allReservations.Where(row => row.Date.Date == DateTime.Today);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isVisible = true;
        MotherEventClient.SharedAuthoritativeDataChanged += OnMotherDataChanged;
        if (!_clockTimer.IsRunning)
        {
            _clockTimer.Start();
        }

        _refreshTimer.Start();
        await LoadReservationsAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isVisible = false;
        MotherEventClient.SharedAuthoritativeDataChanged -= OnMotherDataChanged;
        _clockTimer.Stop();
        _refreshTimer.Stop();
        _searchDebounceCts?.Cancel();
    }

    private async void OnMotherDataChanged(object? sender, MotherDataChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.EventType) ||
            e.EventType.Contains("reservation", StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() => LoadReservationsAsync(showBusy: false));
    }

    private async Task LoadReservationsAsync(bool showBusy = true)
    {
        try
        {
            if (showBusy)
            {
                IsLoading = true;
            }

            var startDate = _displayedMonth.AddDays(-7);
            var endDate = _displayedMonth.AddMonths(1).AddDays(7);
            var live = await _motherReservations.GetReservationsAsync(startDate, endDate);
            IEnumerable<ReservationRow> rows;
            if (live != null)
            {
                rows = live.Select(ToReservationRow);
                _syncStatusText = "Website sync ready";
            }
            else
            {
                var cached = await _cache.GetCachedReservationsAsync(startDate, endDate.AddDays(1));
                rows = cached.Select(ToReservationRow);
                _syncStatusText = "Showing last Mother POS cache";
            }

            _allReservations.Clear();
            _allReservations.AddRange(rows);
            _lastUpdatedAt = DateTime.Now;
            RefreshView();
        }
        catch (Exception ex)
        {
            _syncStatusText = "Website sync error";
            RefreshView();
            await DisplayAlert("Reservation", $"Could not load reservations: {ex.Message}", "OK");
        }
        finally
        {
            if (showBusy)
            {
                IsLoading = false;
            }
        }
    }

    private async void OnMenuClicked(object sender, EventArgs e) => await OpenSidebarAsync();

    private async void OnLogoutClicked(object sender, EventArgs e) => await Navigation.PopToRootAsync(false);

    private async void OnBackdropTapped(object sender, TappedEventArgs e) => await CloseSidebarAsync();

    private async void OnTodayClicked(object sender, EventArgs e)
    {
        _selectedDate = DateTime.Today;
        _displayedMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        await LoadReservationsAsync();
    }

    private async void OnPreviousMonthClicked(object sender, EventArgs e)
    {
        _displayedMonth = _displayedMonth.AddMonths(-1);
        _selectedDate = _displayedMonth;
        await LoadReservationsAsync();
    }

    private async void OnNextMonthClicked(object sender, EventArgs e)
    {
        _displayedMonth = _displayedMonth.AddMonths(1);
        _selectedDate = _displayedMonth;
        await LoadReservationsAsync();
    }

    private async void OnUpdateClicked(object sender, EventArgs e)
    {
        SearchEntry.Text = string.Empty;
        _searchText = string.Empty;

        try
        {
            IsLoading = true;
            _syncStatusText = "Syncing website reservations...";
            OnPropertyChanged(nameof(SyncStatusText));

            var result = await _motherReservations.SyncDateAsync(_selectedDate);
            if (result.Reservations != null)
            {
                _allReservations.Clear();
                _allReservations.AddRange(result.Reservations.Select(ToReservationRow));
                _lastUpdatedAt = DateTime.Now;
                _syncStatusText = result.Success ? "Website sync ready" : "Website sync error";
                RefreshView();
            }
            else
            {
                await LoadReservationsAsync(showBusy: false);
            }

            await DisplayAlert(result.Success ? "Reservation Sync" : "Sync Failed", result.Message, "OK");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async void OnNewReservationClicked(object sender, EventArgs e)
    {
        var dialog = new NewReservationDialog(_selectedDate);
        var draft = await dialog.ShowAsync(RootGrid);
        if (draft == null)
        {
            return;
        }

        try
        {
            IsLoading = true;
            _syncStatusText = "Saving booking...";
            OnPropertyChanged(nameof(SyncStatusText));

            var result = await _motherReservations.CreateAsync(draft);
            await LoadReservationsAsync(showBusy: false);
            await DisplayAlert(result.Success ? "Booking Confirmed" : "Reservation", result.Message, "OK");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;
        var nextValue = e.NewTextValue?.Trim() ?? string.Empty;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(180, cts.Token);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }

                    _searchText = nextValue;
                    RefreshReservations();
                });
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private async void OnCalendarDaySelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ReservationCalendarDay day)
        {
            return;
        }

        _selectedDate = day.Date;
        var monthChanged = day.Date.Month != _displayedMonth.Month || day.Date.Year != _displayedMonth.Year;
        _displayedMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        CalendarCollectionView.SelectedItem = null;

        if (monthChanged)
        {
            await LoadReservationsAsync();
            return;
        }

        RefreshView();
    }

    private void OnReservationRowTapped(object sender, TappedEventArgs e)
    {
        if (sender is BindableObject bindable && bindable.BindingContext is ReservationRow row)
        {
            SelectedReservation = row;
        }
    }

    private void OnReservationDetailsClicked(object sender, EventArgs e)
    {
        if (sender is BindableObject bindable && bindable.BindingContext is ReservationRow row)
        {
            SelectedReservation = row;
        }
    }

    private void OnCloseDetailsClicked(object sender, EventArgs e)
    {
        SelectedReservation = null;
    }

    private async void OnReservationShowClicked(object sender, EventArgs e)
    {
        if (sender is BindableObject bindable && bindable.BindingContext is ReservationRow row)
        {
            await UpdateReservationAttendanceAsync(row, "arrived");
        }
    }

    private async void OnReservationNoShowClicked(object sender, EventArgs e)
    {
        if (sender is BindableObject bindable && bindable.BindingContext is ReservationRow row)
        {
            await UpdateReservationAttendanceAsync(row, "no_show");
        }
    }

    private async void OnReservationCancelClicked(object sender, EventArgs e)
    {
        if (sender is BindableObject bindable && bindable.BindingContext is ReservationRow row)
        {
            await UpdateReservationAttendanceAsync(row, "cancelled");
        }
    }

    private async Task UpdateReservationAttendanceAsync(ReservationRow row, string status)
    {
        try
        {
            IsLoading = true;
            _syncStatusText = "Updating reservation...";
            OnPropertyChanged(nameof(SyncStatusText));

            var result = await _motherReservations.UpdateStatusAsync(row.CloudId, row.LocalId, status);
            await LoadReservationsAsync(showBusy: false);
            await DisplayAlert(result.Success ? "Reservation" : "Reservation", result.Message, "OK");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task OpenSidebarAsync()
    {
        SidebarLayer.IsVisible = true;
        Sidebar.TranslationX = -280;
        await Sidebar.TranslateTo(0, 0, 180, Easing.CubicOut);
    }

    private async Task CloseSidebarAsync()
    {
        await Sidebar.TranslateTo(-280, 0, 160, Easing.CubicIn);
        SidebarLayer.IsVisible = false;
    }

    private async Task NavigateFromSidebarAsync(string menu)
    {
        await CloseSidebarAsync();
        if (menu == "Reservation" || !ClientHostAccess.CanOpenMenu(menu))
        {
            return;
        }

        await Navigation.PushAsync(menu switch
        {
            "Cash Drawer" => new CashDrawerPage(),
            "Restaurant" => new TableLayoutPage(),
            "Collection" => new CollectionOrderPage(),
            "Delivery" => new DeliveryOrderPage(),
            "Live Order" => new LiveOrderPage(),
            "Gift Cards" => new GiftCardPage(),
            "Loyalty Points" => new LoyaltyPage(),
            "Order History" => new OrderHistoryPage(),
            _ => new ReservationPage()
        }, false);
    }

    private void RefreshView()
    {
        RefreshCalendar();
        RefreshReservations();
        OnPropertyChanged(nameof(SelectedDateDisplay));
        OnPropertyChanged(nameof(CalendarMonthText));
        OnPropertyChanged(nameof(TodayCountText));
        OnPropertyChanged(nameof(GuestCountText));
        OnPropertyChanged(nameof(NextReservationText));
        OnPropertyChanged(nameof(SyncStatusText));
        OnPropertyChanged(nameof(LastUpdatedText));
    }

    private void RefreshReservations()
    {
        Reservations.Clear();
        var rows = _allReservations
            .Where(row => row.Date.Date == _selectedDate.Date)
            .Where(row => string.IsNullOrWhiteSpace(_searchText)
                || row.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || row.Phone.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.Time)
            .ToList();

        foreach (var row in rows)
        {
            Reservations.Add(row);
        }

        OnPropertyChanged(nameof(TodayCountText));
        OnPropertyChanged(nameof(GuestCountText));
        OnPropertyChanged(nameof(NextReservationText));
    }

    private void RefreshCalendar()
    {
        CalendarDays.Clear();
        var firstOfMonth = _displayedMonth.Date;
        var offset = ((int)firstOfMonth.DayOfWeek + 6) % 7;
        var gridStart = firstOfMonth.AddDays(-offset);
        var counts = _allReservations
            .GroupBy(row => row.Date.Date)
            .ToDictionary(group => group.Key, group => group.Count());

        for (var index = 0; index < 42; index++)
        {
            var date = gridStart.AddDays(index);
            counts.TryGetValue(date.Date, out var count);
            CalendarDays.Add(new ReservationCalendarDay(
                date,
                date.Month == _displayedMonth.Month,
                date.Date == _selectedDate.Date,
                count));
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        DateLabel.Text = now.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
        TimeLabel.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static ReservationRow ToReservationRow(MotherReservationDto reservation)
    {
        var dateTime = MotherReservationClient.CombineDateTime(reservation.Date, reservation.Time);
        var specialRequests = CleanSpecialRequests(reservation.Notes);
        return new ReservationRow(
            reservation.CloudId,
            reservation.LocalId,
            dateTime.Date,
            TimeOnly.FromTimeSpan(dateTime.TimeOfDay),
            string.IsNullOrWhiteSpace(reservation.CustomerName) ? "Guest" : reservation.CustomerName,
            reservation.CustomerPhone ?? string.Empty,
            reservation.Covers,
            string.IsNullOrWhiteSpace(reservation.TableNumber) ? "-" : reservation.TableNumber,
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase((reservation.Status ?? "confirmed").Replace("_", " ")),
            reservation.Reference ?? string.Empty,
            reservation.Source ?? string.Empty,
            reservation.CustomerEmail ?? string.Empty,
            reservation.PromoCode ?? string.Empty,
            reservation.Allergies ?? string.Empty,
            specialRequests);
    }

    private static ReservationRow ToReservationRow(CachedReservation reservation)
    {
        var fromPayload = TryReadPayload(reservation.PayloadJson);
        if (fromPayload != null)
        {
            return ToReservationRow(fromPayload);
        }

        var dateTime = DateTimeOffset.TryParse(reservation.ReservationUtc, out var parsed)
            ? parsed.LocalDateTime
            : DateTime.Now;
        return new ReservationRow(
            reservation.Id,
            reservation.MotherId,
            dateTime.Date,
            TimeOnly.FromDateTime(dateTime),
            string.IsNullOrWhiteSpace(reservation.CustomerName) ? "Guest" : reservation.CustomerName,
            reservation.Phone ?? string.Empty,
            reservation.PartySize,
            reservation.TableNumber ?? reservation.TableId?.ToString(CultureInfo.InvariantCulture) ?? "-",
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase((reservation.Status ?? "pending").Replace("_", " ")),
            string.Empty,
            string.Empty,
            string.Empty,
            ReadPayloadValue(reservation.PayloadJson, "promoCode") ?? ReadPayloadValue(reservation.PayloadJson, "promo_code") ?? string.Empty,
            string.Empty,
            string.Empty);
    }

    private static MotherReservationDto? TryReadPayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MotherReservationDto>(payloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadPayloadValue(string? payloadJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.TryGetProperty(propertyName, out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string CleanSpecialRequests(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return string.Empty;
        }

        var text = notes.Trim();
        return IsUploadDiagnostic(text) ? string.Empty : text;
    }

    private static bool IsUploadDiagnostic(string value)
    {
        var text = value.Trim();
        return text.StartsWith("Input string was not in a correct format", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("HTTP ", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Cloud upload failed", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Invalid cloud response", StringComparison.OrdinalIgnoreCase);
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected new void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ReservationCalendarDay
{
    public ReservationCalendarDay(DateTime date, bool isCurrentMonth, bool isSelected, int reservationCount)
    {
        Date = date;
        IsCurrentMonth = isCurrentMonth;
        IsSelected = isSelected;
        ReservationCount = reservationCount;
    }

    public DateTime Date { get; }
    public bool IsCurrentMonth { get; }
    public bool IsSelected { get; }
    public int ReservationCount { get; }
    public string DayText => Date.Day.ToString(CultureInfo.InvariantCulture);
    public Color BackgroundColor => IsSelected
        ? Color.FromArgb("#2563EB")
        : ReservationCount > 0
            ? Color.FromArgb("#DBEAFE")
            : Colors.White;
    public Color BorderColor => IsSelected
        ? Color.FromArgb("#2563EB")
        : ReservationCount > 0
            ? Color.FromArgb("#60A5FA")
            : Color.FromArgb("#E2E8F0");
    public Color TextColor => IsSelected
        ? Colors.White
        : ReservationCount > 0
            ? Color.FromArgb("#1D4ED8")
            : IsCurrentMonth ? Color.FromArgb("#0F172A") : Color.FromArgb("#CBD5E1");
}

public sealed class ReservationRow
{
    public ReservationRow(
        string cloudId,
        string? localId,
        DateTime date,
        TimeOnly time,
        string name,
        string phone,
        int guests,
        string table,
        string status,
        string reference,
        string source,
        string email,
        string promoCode,
        string allergies,
        string specialRequests)
    {
        CloudId = cloudId;
        LocalId = localId;
        Date = date.Date;
        Time = time;
        Name = name;
        Phone = phone;
        Guests = guests;
        Table = table;
        Status = string.IsNullOrWhiteSpace(status) ? "Booked" : status;
        Reference = reference;
        Source = source;
        Email = email;
        PromoCode = promoCode;
        Allergies = allergies;
        SpecialRequests = specialRequests;
    }

    public string CloudId { get; }
    public string? LocalId { get; }
    public DateTime Date { get; }
    public TimeOnly Time { get; }
    public string TimeText => Time.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string Name { get; }
    public string Phone { get; }
    public int Guests { get; }
    public string Table { get; }
    public string Status { get; }
    public string Reference { get; }
    public string Source { get; }
    public string Email { get; }
    public string PromoCode { get; }
    public string Allergies { get; }
    public string SpecialRequests { get; }
    public string DateTimeText => $"{Date:dddd, dd MMMM yyyy} at {TimeText}";
    public string PhoneDisplay => string.IsNullOrWhiteSpace(Phone) ? "No phone saved" : Phone;
    public string EmailDisplay => string.IsNullOrWhiteSpace(Email) ? "No email saved" : Email;
    public string GuestsText => Guests == 1 ? "1 guest" : $"{Guests} guests";
    public string TableDisplay => Table == "-" ? "Table not assigned" : $"Table {Table}";
    public string StatusDisplay => $"Status: {Status}";
    public string PromoCodeDisplay => string.IsNullOrWhiteSpace(PromoCode) ? "No promocode" : PromoCode;
    public string AllergiesDisplay => string.IsNullOrWhiteSpace(Allergies) ? "No allergies or dietary notes" : Allergies;
    public string SpecialRequestsDisplay => HasSpecialRequests ? SpecialRequests.Trim() : string.Empty;
    public string SourceDisplay => Source.Trim().ToLowerInvariant() switch
    {
        "online" => "Online",
        "walk_in" or "walkin" or "walk-in" => "Walk-in",
        "phone" => "Phone",
        "pos" => "POS",
        _ => string.IsNullOrWhiteSpace(Source) ? "Booking" : Source
    };
    public bool HasPromoCode => !string.IsNullOrWhiteSpace(PromoCode);
    public bool HasAllergies => !string.IsNullOrWhiteSpace(Allergies);
    public bool HasSpecialRequests => !string.IsNullOrWhiteSpace(SpecialRequests);
    public string PromoBadgeText => HasPromoCode ? $"Promo: {PromoCode}" : string.Empty;
    public Color StatusBadgeBackground => NormalizedStatus switch
    {
        "arrived" or "show" or "shown" or "seated" => Color.FromArgb("#DCFCE7"),
        "no_show" or "noshow" => Color.FromArgb("#FFF1F2"),
        "cancelled" or "canceled" => Color.FromArgb("#F1F5F9"),
        _ => Color.FromArgb("#EFF6FF")
    };
    public Color StatusBadgeBorder => NormalizedStatus switch
    {
        "arrived" or "show" or "shown" or "seated" => Color.FromArgb("#86EFAC"),
        "no_show" or "noshow" => Color.FromArgb("#FECDD3"),
        "cancelled" or "canceled" => Color.FromArgb("#CBD5E1"),
        _ => Color.FromArgb("#BFDBFE")
    };
    public Color StatusBadgeTextColor => NormalizedStatus switch
    {
        "arrived" or "show" or "shown" or "seated" => Color.FromArgb("#047857"),
        "no_show" or "noshow" => Color.FromArgb("#E11D48"),
        "cancelled" or "canceled" => Color.FromArgb("#475569"),
        _ => Color.FromArgb("#1D4ED8")
    };
    public bool IsArrived => NormalizedStatus is "arrived" or "show" or "shown" or "seated";
    public bool IsNoShow => NormalizedStatus is "no_show" or "noshow";
    public bool IsCancelled => NormalizedStatus is "cancelled" or "canceled";
    public bool IsFinalAttendance => IsArrived || IsNoShow || IsCancelled;
    public bool IsShowButtonVisible => !IsNoShow && !IsCancelled;
    public bool IsNoShowButtonVisible => !IsArrived && !IsCancelled;
    public bool IsCancelButtonVisible => !IsArrived && !IsNoShow;
    public bool IsShowButtonEnabled => !IsFinalAttendance;
    public bool IsNoShowButtonEnabled => !IsFinalAttendance;
    public bool IsCancelButtonEnabled => !IsFinalAttendance;
    public string ShowButtonText => IsArrived ? "Shown" : "Show";
    public string NoShowButtonText => "No Show";
    public string CancelButtonText => IsCancelled ? "Cancelled" : "Cancel";
    public Color ShowButtonBackground => IsArrived ? Color.FromArgb("#064E3B") : Color.FromArgb("#DCFCE7");
    public Color ShowButtonBorderColor => IsArrived ? Color.FromArgb("#064E3B") : Color.FromArgb("#86EFAC");
    public Color ShowButtonTextColor => IsArrived ? Colors.White : Color.FromArgb("#047857");
    public Color NoShowButtonBackground => IsNoShow ? Color.FromArgb("#7F1D1D") : Color.FromArgb("#FFF1F2");
    public Color NoShowButtonBorderColor => IsNoShow ? Color.FromArgb("#FCA5A5") : Color.FromArgb("#FECDD3");
    public Color NoShowButtonTextColor => IsNoShow ? Colors.White : Color.FromArgb("#E11D48");
    public Color CancelButtonBackground => IsCancelled ? Color.FromArgb("#334155") : Color.FromArgb("#FEF2F2");
    public Color CancelButtonBorderColor => IsCancelled ? Color.FromArgb("#334155") : Color.FromArgb("#FECACA");
    public Color CancelButtonTextColor => IsCancelled ? Colors.White : Color.FromArgb("#DC2626");
    private string NormalizedStatus => Status.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
}
