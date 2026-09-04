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
    private readonly IDispatcherTimer _clockTimer;
    private readonly List<ReservationRow> _allReservations = new();
    private DateTime _selectedDate = DateTime.Today;
    private DateTime _displayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _lastUpdatedAt = DateTime.Now;
    private string _searchText = string.Empty;
    private string _syncStatusText = "Website sync ready";
    private bool _isLoading;

    public ReservationPage()
    {
        InitializeComponent();
        BindingContext = this;
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);

        _clockTimer = Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        UpdateClock();
        RefreshCalendar();
        _ = LoadReservationsAsync();
    }

    public ObservableCollection<ReservationCalendarDay> CalendarDays { get; } = new();
    public ObservableCollection<ReservationRow> Reservations { get; } = new();

    public string SelectedDateDisplay => _selectedDate.ToString("dddd, dd MMMM yyyy", CultureInfo.InvariantCulture);
    public string CalendarMonthText => _displayedMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
    public string TodayCountText => _allReservations.Count(row => row.Date.Date == DateTime.Today).ToString(CultureInfo.InvariantCulture);
    public string GuestCountText => _allReservations.Where(row => row.Date.Date == DateTime.Today).Sum(row => row.Guests).ToString(CultureInfo.InvariantCulture);
    public string NextReservationText => _allReservations
        .Where(row => row.Date.Date == DateTime.Today && row.DateTime >= DateTime.Now)
        .OrderBy(row => row.DateTime)
        .FirstOrDefault()?.TimeText ?? "--";
    public string SyncStatusText => _syncStatusText;
    public string LastUpdatedText => $"Updated {_lastUpdatedAt:HH:mm}";

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

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _clockTimer.Stop();
    }

    private async Task LoadReservationsAsync()
    {
        try
        {
            IsLoading = true;
            var monthStart = _displayedMonth.Date;
            var monthEnd = monthStart.AddMonths(1);
            var cachedReservations = await _cache.GetCachedReservationsAsync(monthStart, monthEnd);

            _allReservations.Clear();
            _allReservations.AddRange(cachedReservations.Select(ToReservationRow));
            _lastUpdatedAt = DateTime.Now;
            _syncStatusText = "Website sync ready";
            RefreshView();
        }
        finally
        {
            IsLoading = false;
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
        _syncStatusText = "Reading local cache...";
        OnPropertyChanged(nameof(SyncStatusText));
        await LoadReservationsAsync();
        await DisplayAlert("Reservation", "Client POS reservations are updated when Mother POS syncs. Create a new pairing/bootstrap or run Mother sync to refresh website reservations.", "OK");
    }

    private async void OnNewReservationClicked(object sender, EventArgs e)
    {
        await DisplayAlert("Reservation", "New reservations must be created on Mother POS or the website. Client POS will show them after Mother syncs.", "OK");
    }

    private async void OnReservationDetailsClicked(object sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not ReservationRow reservation)
        {
            return;
        }

        await DisplayAlert(
            "Reservation Details",
            $"{reservation.TimeText} · {reservation.Name}\nPhone: {reservation.Phone}\nCovers: {reservation.Guests}\nTable: {reservation.Table}\nStatus: {reservation.StatusDisplay}",
            "OK");
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = e.NewTextValue?.Trim() ?? string.Empty;
        RefreshReservations();
    }

    private void OnCalendarDaySelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ReservationCalendarDay day)
        {
            return;
        }

        _selectedDate = day.Date;
        _displayedMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        CalendarCollectionView.SelectedItem = null;
        RefreshView();
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
        if (menu == "Reservation")
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
            "Web Orders" => new OnlineOrdersPage(),
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
            .OrderBy(row => row.DateTime)
            .ToList();

        foreach (var row in rows)
        {
            Reservations.Add(row);
        }
    }

    private void RefreshCalendar()
    {
        CalendarDays.Clear();
        var firstOfMonth = _displayedMonth.Date;
        var offset = ((int)firstOfMonth.DayOfWeek + 6) % 7;
        var gridStart = firstOfMonth.AddDays(-offset);

        for (var index = 0; index < 42; index++)
        {
            var date = gridStart.AddDays(index);
            var inMonth = date.Month == _displayedMonth.Month;
            var selected = date.Date == _selectedDate.Date;
            var today = date.Date == DateTime.Today;

            CalendarDays.Add(new ReservationCalendarDay(
                date,
                date.Day.ToString(CultureInfo.InvariantCulture),
                selected ? "#2563EB" : "#FFFFFF",
                selected ? "#2563EB" : today ? "#93C5FD" : "#E2E8F0",
                selected ? "#FFFFFF" : inMonth ? "#0F172A" : "#CBD5E1"));
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        DateLabel.Text = now.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
        TimeLabel.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static ReservationRow ToReservationRow(CachedReservation reservation)
    {
        var dateTime = ParseReservationDate(reservation.ReservationUtc);
        var promo = ReadPayloadValue(reservation.PayloadJson, "promo_code") ?? ReadPayloadValue(reservation.PayloadJson, "promoCode");
        return new ReservationRow(
            reservation.Id,
            reservation.MotherId,
            reservation.CustomerName,
            reservation.Phone,
            reservation.PartySize,
            reservation.TableNumber ?? reservation.TableId?.ToString(CultureInfo.InvariantCulture) ?? "--",
            reservation.Status,
            promo,
            dateTime);
    }

    private static DateTime ParseReservationDate(string reservationUtc)
    {
        return DateTimeOffset.TryParse(reservationUtc, out var value)
            ? value.ToLocalTime().DateTime
            : DateTime.Now;
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

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected new void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record ReservationCalendarDay(
    DateTime Date,
    string DayText,
    string BackgroundColor,
    string BorderColor,
    string TextColor);

public sealed record ReservationRow(
    string Id,
    string MotherId,
    string Name,
    string Phone,
    int Guests,
    string Table,
    string Status,
    string? PromoCode,
    DateTime DateTime)
{
    public DateTime Date => DateTime.Date;
    public string TimeText => DateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string StatusDisplay => string.IsNullOrWhiteSpace(Status) ? "Pending" : Status;
    public string PromoColumnText => string.IsNullOrWhiteSpace(PromoCode) ? "--" : PromoCode;
}
