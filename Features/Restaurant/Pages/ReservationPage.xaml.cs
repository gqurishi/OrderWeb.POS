using POS_in_NET.Services;
using POS_in_NET.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace POS_in_NET.Pages;

public partial class ReservationPage : ContentPage, INotifyPropertyChanged
{
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;
    private readonly ReservationSyncService? _reservationSyncService;
    private readonly List<ReservationRow> _allReservations = new();
    private DateTime _selectedDate = DateTime.Today;
    private DateTime _displayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _lastUpdatedAt = DateTime.Now;
    private string _searchText = string.Empty;
    private string _syncStatusText = "Website sync ready";
    private CancellationTokenSource? _searchDebounceCts;
    private bool _isBusy;
    private bool _hasLoadedOnce;

    public ObservableCollection<ReservationCalendarDay> CalendarDays { get; } = new();
    public ObservableCollection<ReservationRow> Reservations { get; } = new();

    public string SelectedDateDisplay => _selectedDate.ToString("dddd, dd MMMM yyyy", CultureInfo.InvariantCulture);
    public string CalendarMonthText => _displayedMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
    public string TodayCountText => TodayReservations.Count().ToString(CultureInfo.InvariantCulture);
    public string GuestCountText => TodayReservations.Sum(row => row.Guests).ToString(CultureInfo.InvariantCulture);
    public string NextReservationText => TodayReservations.OrderBy(row => row.Time).FirstOrDefault()?.TimeText ?? "--";
    public string SyncStatusText => _syncStatusText;
    public string LastUpdatedText => $"Updated {_lastUpdatedAt:HH:mm}";
    public bool IsLoading
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged();
        }
    }

    private IEnumerable<ReservationRow> TodayReservations => _allReservations.Where(row => row.Date.Date == DateTime.Today);

    public ReservationPage()
    {
        InitializeComponent();
        TopBar.SetPageTitle("Reservation");
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        _reservationSyncService = ServiceHelper.GetService<ReservationSyncService>();
        if (_reservationSyncService != null)
        {
            _reservationSyncService.SyncCompleted += OnReservationSyncCompleted;
        }
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
            return;
        }

        if (!_hasLoadedOnce)
        {
            await LoadReservationsAsync();
        }
    }

    public void SetReservations(IEnumerable<ReservationRow> websiteReservations)
    {
        _allReservations.Clear();
        _allReservations.AddRange(websiteReservations);
        RefreshView();
    }

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

    private async void OnNewReservationClicked(object sender, EventArgs e)
    {
        if (_reservationSyncService == null)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Reservation", "Reservation sync service is not available.");
            return;
        }

        var dialog = new POS_in_NET.Views.NewReservationDialog(_selectedDate);
        var request = await dialog.ShowAsync();
        if (request == null)
        {
            return;
        }

        try
        {
            IsLoading = true;
            _syncStatusText = "Saving booking...";
            OnPropertyChanged(nameof(SyncStatusText));

            var result = await _reservationSyncService.CreatePosReservationAsync(request);
            await LoadReservationsAsync(showBusy: false);

            await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                result.Success ? "Reservation Saved" : "Reservation",
                result.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async void OnUpdateClicked(object sender, EventArgs e)
    {
        _searchText = string.Empty;
        SearchEntry.Text = string.Empty;

        if (_reservationSyncService == null)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Reservation", "Reservation sync service is not available.");
            return;
        }

        try
        {
            IsLoading = true;
            _syncStatusText = "Syncing website reservations...";
            OnPropertyChanged(nameof(SyncStatusText));

            var result = await _reservationSyncService.SyncDateAsync(_selectedDate, useSince: false, includeCancelled: true);
            await LoadReservationsAsync(showBusy: false);

            await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                result.Success ? "Reservation Sync" : "Sync Failed",
                result.Message);
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

    private void OnSearchBoxTapped(object sender, TappedEventArgs e)
    {
        SearchEntry.Focus();
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

    private async Task LoadReservationsAsync(bool showBusy = true)
    {
        try
        {
            if (showBusy)
            {
                IsLoading = true;
            }

            if (_reservationSyncService == null)
            {
                _syncStatusText = "Website sync not available";
                RefreshView();
                return;
            }

            var startDate = _displayedMonth.AddDays(-7);
            var endDate = _displayedMonth.AddMonths(1).AddDays(7);
            var reservations = await _reservationSyncService.GetReservationsAsync(startDate, endDate);

            _allReservations.Clear();
            _allReservations.AddRange(reservations.Select(ToReservationRow));
            _hasLoadedOnce = true;
            _lastUpdatedAt = DateTime.Now;
            _syncStatusText = "Website sync ready";
            RefreshView();
        }
        catch (Exception ex)
        {
            _syncStatusText = "Website sync error";
            RefreshView();
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Reservation", $"Could not load reservations: {ex.Message}");
        }
        finally
        {
            if (showBusy)
            {
                IsLoading = false;
            }
        }
    }

    private void OnReservationSyncCompleted(object? sender, ReservationSyncCompletedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _syncStatusText = e.Result.Success ? "Website sync ready" : "Website sync error";
            await LoadReservationsAsync();
        });
    }

    private static ReservationRow ToReservationRow(CloudReservation reservation)
    {
        var noteParts = new[]
        {
            reservation.IsPendingUpload ? "Pending cloud upload" : "",
            string.IsNullOrWhiteSpace(reservation.Reference) ? "" : reservation.Reference,
            FormatSourceLabel(reservation.Source),
            reservation.Notes,
            string.IsNullOrWhiteSpace(reservation.Allergies) ? "" : $"Allergies: {reservation.Allergies}"
        }.Where(part => !string.IsNullOrWhiteSpace(part));

        return new ReservationRow(
            reservation.CloudId,
            reservation.LocalId,
            reservation.ReservationDate,
            TimeOnly.FromTimeSpan(reservation.ReservationTime),
            string.IsNullOrWhiteSpace(reservation.CustomerName) ? "Guest" : reservation.CustomerName,
            reservation.CustomerPhone,
            reservation.Covers,
            string.IsNullOrWhiteSpace(reservation.TableNumber) ? "-" : reservation.TableNumber,
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(reservation.Status.Replace("_", " ")),
            string.Join(" | ", noteParts),
            reservation.Reference,
            reservation.Source);
    }

    private static string FormatSourceLabel(string source)
    {
        return source.Trim().ToLowerInvariant() switch
        {
            "online" => "Online",
            "walk_in" or "walkin" or "walk-in" => "Walk-in",
            "phone" => "Phone",
            "pos" => "POS",
            _ => string.IsNullOrWhiteSpace(source) ? "" : source
        };
    }

    private async void OnReservationTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject bindable || bindable.BindingContext is not ReservationRow row)
        {
            return;
        }

        if (_reservationSyncService == null)
        {
            return;
        }

        var dialog = new POS_in_NET.Views.ModernActionSheetDialog();
        dialog.SetActionSheet(
            $"{row.Name} — update status",
            new List<string> { "Arrived", "Seated", "Completed", "No-show", "Cancelled" },
            icon: "R",
            iconBgColor: "#2563EB");

        var choice = await dialog.ShowAsync();
        if (string.IsNullOrWhiteSpace(choice))
        {
            return;
        }

        var status = choice.ToLowerInvariant() switch
        {
            "arrived" => "arrived",
            "seated" => "seated",
            "completed" => "completed",
            "no-show" => "no_show",
            "cancelled" => "cancelled",
            _ => choice.ToLowerInvariant()
        };

        var result = await _reservationSyncService.UpdateReservationStatusAsync(row.CloudId, status, row.LocalId);
        await LoadReservationsAsync(showBusy: false);
        await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
            result.Success ? "Reservation Updated" : "Update Failed",
            result.Message);
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
        string note,
        string reference,
        string source)
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
        Note = note;
        Reference = reference;
        Source = source;
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
    public string Note { get; }
    public string Reference { get; }
    public string Source { get; }

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
