using POS_in_NET.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace POS_in_NET.Pages;

public partial class ReservationPage : ContentPage, INotifyPropertyChanged
{
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;
    private readonly List<ReservationRow> _allReservations = new();
    private DateTime _selectedDate = DateTime.Today;
    private DateTime _displayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _lastUpdatedAt = DateTime.Now;
    private string _searchText = string.Empty;

    public ObservableCollection<ReservationCalendarDay> CalendarDays { get; } = new();
    public ObservableCollection<ReservationRow> Reservations { get; } = new();

    public string SelectedDateDisplay => _selectedDate.ToString("dddd, dd MMMM yyyy", CultureInfo.InvariantCulture);
    public string CalendarMonthText => _displayedMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
    public string TodayCountText => TodayReservations.Count().ToString(CultureInfo.InvariantCulture);
    public string GuestCountText => TodayReservations.Sum(row => row.Guests).ToString(CultureInfo.InvariantCulture);
    public string NextReservationText => TodayReservations.OrderBy(row => row.Time).FirstOrDefault()?.TimeText ?? "--";
    public string SyncStatusText => "Website sync ready";
    public string LastUpdatedText => $"Updated {_lastUpdatedAt:HH:mm}";

    private IEnumerable<ReservationRow> TodayReservations => _allReservations.Where(row => row.Date.Date == DateTime.Today);

    public ReservationPage()
    {
        InitializeComponent();
        TopBar.SetPageTitle("Reservation");
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        BindingContext = this;
        RefreshView();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_roleAccessService.CanAccessFeature(_authService.CurrentUser?.Role, "reservation"))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "You do not have permission to access Reservation.");
            await Shell.Current.GoToAsync($"//{_roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role)}");
        }
    }

    public void SetReservations(IEnumerable<ReservationRow> websiteReservations)
    {
        _allReservations.Clear();
        _allReservations.AddRange(websiteReservations);
        RefreshView();
    }

    private void OnTodayClicked(object sender, EventArgs e)
    {
        _selectedDate = DateTime.Today;
        _displayedMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        RefreshView();
    }

    private void OnPreviousMonthClicked(object sender, EventArgs e)
    {
        _displayedMonth = _displayedMonth.AddMonths(-1);
        _selectedDate = _displayedMonth;
        RefreshView();
    }

    private void OnNextMonthClicked(object sender, EventArgs e)
    {
        _displayedMonth = _displayedMonth.AddMonths(1);
        _selectedDate = _displayedMonth;
        RefreshView();
    }

    private async void OnNewReservationClicked(object sender, EventArgs e)
    {
        await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
            "Reservation",
            "New reservations will be created from the website and synced here.");
    }

    private async void OnUpdateClicked(object sender, EventArgs e)
    {
        _selectedDate = DateTime.Today;
        _displayedMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        _searchText = string.Empty;
        _lastUpdatedAt = DateTime.Now;
        SearchEntry.Text = string.Empty;
        RefreshView();

        await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
            "Reservation",
            "Reservation information refreshed.");
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = e.NewTextValue?.Trim() ?? string.Empty;
        RefreshReservations();
    }

    private void OnSearchBoxTapped(object sender, TappedEventArgs e)
    {
        SearchEntry.Focus();
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

    private void RefreshView()
    {
        BuildCalendarDays();
        RefreshReservations();

        OnPropertyChanged(nameof(SelectedDateDisplay));
        OnPropertyChanged(nameof(CalendarMonthText));
        OnPropertyChanged(nameof(TodayCountText));
        OnPropertyChanged(nameof(GuestCountText));
        OnPropertyChanged(nameof(NextReservationText));
        OnPropertyChanged(nameof(LastUpdatedText));
        OnPropertyChanged(nameof(SyncStatusText));
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

    private void BuildCalendarDays()
    {
        CalendarDays.Clear();

        var monthStart = new DateTime(_displayedMonth.Year, _displayedMonth.Month, 1);
        var offset = ((int)monthStart.DayOfWeek + 6) % 7;
        var gridStart = monthStart.AddDays(-offset);
        var counts = _allReservations
            .GroupBy(row => row.Date.Date)
            .ToDictionary(group => group.Key, group => group.Count());

        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            counts.TryGetValue(date.Date, out var count);

            CalendarDays.Add(new ReservationCalendarDay(
                date,
                date.Month == _displayedMonth.Month,
                date.Date == _selectedDate.Date,
                count));
        }
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
    public string CountText => ReservationCount > 0 ? $"{ReservationCount} booking" : string.Empty;
    public Color BackgroundColor => IsSelected ? Color.FromArgb("#2563EB") : ReservationCount > 0 ? Color.FromArgb("#EFF6FF") : Colors.White;
    public Color BorderColor => IsSelected ? Color.FromArgb("#2563EB") : ReservationCount > 0 ? Color.FromArgb("#BFDBFE") : Color.FromArgb("#E2E8F0");
    public Color TextColor => IsSelected ? Colors.White : IsCurrentMonth ? Color.FromArgb("#0F172A") : Color.FromArgb("#CBD5E1");
    public Color CountColor => IsSelected ? Colors.White : Color.FromArgb("#2563EB");
}

public sealed class ReservationRow
{
    public ReservationRow(DateTime date, TimeOnly time, string name, string phone, int guests, string table, string status, string note)
    {
        Date = date.Date;
        Time = time;
        Name = name;
        Phone = phone;
        Guests = guests;
        Table = table;
        Status = string.IsNullOrWhiteSpace(status) ? "Booked" : status;
        Note = note;
    }

    public DateTime Date { get; }
    public TimeOnly Time { get; }
    public string TimeText => Time.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string Name { get; }
    public string Phone { get; }
    public int Guests { get; }
    public string Table { get; }
    public string Status { get; }
    public string Note { get; }

    public Color StatusBackground => string.Equals(Status, "Confirmed", StringComparison.OrdinalIgnoreCase)
        ? Color.FromArgb("#ECFDF5")
        : Color.FromArgb("#EFF6FF");

    public Color StatusBorder => string.Equals(Status, "Confirmed", StringComparison.OrdinalIgnoreCase)
        ? Color.FromArgb("#A7F3D0")
        : Color.FromArgb("#BFDBFE");

    public Color StatusTextColor => string.Equals(Status, "Confirmed", StringComparison.OrdinalIgnoreCase)
        ? Color.FromArgb("#047857")
        : Color.FromArgb("#1D4ED8");
}
