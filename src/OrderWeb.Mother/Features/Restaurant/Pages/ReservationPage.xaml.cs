using POS_in_NET.Services;
using POS_in_NET.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using OrderWeb.SharedUI.Views;

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
    private CancellationTokenSource? _syncRefreshCts;
    private bool _isBusy;
    private bool _hasLoadedOnce;
    private bool _syncHandlerAttached;
    private ReservationRow? _selectedReservation;

    public ObservableCollection<ReservationCalendarDay> CalendarDays { get; } = new();
    public ObservableCollection<ReservationRow> Reservations { get; } = new();

    public string SelectedDateDisplay => _selectedDate.ToString("dddd, dd MMMM yyyy", CultureInfo.InvariantCulture);
    public string CalendarMonthText => _displayedMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
    public string TodayCountText => TodayReservations.Count().ToString(CultureInfo.InvariantCulture);
    public string GuestCountText => TodayReservations.Sum(row => row.Guests).ToString(CultureInfo.InvariantCulture);
    public string NextReservationText => TodayReservations.OrderBy(row => row.Time).FirstOrDefault()?.TimeText ?? "--";
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
        SizeChanged += OnReservationPageSizeChanged;
        TopBar.SetPageTitle("Reservation");
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        _reservationSyncService = ServiceHelper.GetService<ReservationSyncService>();
        AttachReservationSyncHandler();
        BindingContext = this;
        WireSharedReservationView();
        RefreshView();
    }

    private void WireSharedReservationView()
    {
        Reservation.UpdateRequested += OnUpdateClicked;
        Reservation.NewReservationRequested += OnNewReservationClicked;
        Reservation.TodayRequested += OnTodayClicked;
        Reservation.PreviousMonthRequested += OnPreviousMonthClicked;
        Reservation.NextMonthRequested += OnNextMonthClicked;
        Reservation.SearchBoxFocused += OnSearchBoxTapped;
        Reservation.SearchTextChanged += OnSearchTextChanged;
        Reservation.CalendarDaySelected += OnCalendarDaySelected;
        Reservation.ReservationSelected += (_, row) => SelectedReservation = row;
        Reservation.ShowRequested += async (_, row) => await UpdateReservationAttendanceAsync(row, "arrived");
        Reservation.DetailsRequested += (_, row) => SelectedReservation = row;
        Reservation.NoShowRequested += async (_, row) => await UpdateReservationAttendanceAsync(row, "no_show");
        Reservation.CancelRequested += async (_, row) => await UpdateReservationAttendanceAsync(row, "cancelled");
        Reservation.DetailsClosed += (_, _) => SelectedReservation = null;
    }

    private void OnReservationPageSizeChanged(object? sender, EventArgs e) =>
        Reservation.ApplyResponsiveCalendarWidth(Width);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        AttachReservationSyncHandler();

        if (!_roleAccessService.CanAccessFeature(_authService.CurrentUser?.Role, "reservation"))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "You do not have permission to access Reservation.");
            await NavigationCoordinator.Shared.NavigateShellAsync(_roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role));
            return;
        }

        if (!_hasLoadedOnce)
        {
            await LoadReservationsAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        DetachReservationSyncHandler();
        _searchDebounceCts?.Cancel();
        _syncRefreshCts?.Cancel();
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

            if (result.Success)
            {
                NotificationService.Instance.ShowSuccess(
                    result.Message,
                    "Booking Confirmed");
            }
            else
            {
                NotificationService.Instance.ShowWarning(
                    result.Message,
                    "Reservation");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async void OnUpdateClicked(object sender, EventArgs e)
    {
        _searchText = string.Empty;
        Reservation.ClearSearch();

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

    private void OnSearchBoxTapped(object? sender, EventArgs e)
    {
        Reservation.FocusSearch();
    }

    private async void OnCalendarDaySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ReservationCalendarDay day)
        {
            return;
        }

        _selectedDate = day.Date;
        var monthChanged = day.Date.Month != _displayedMonth.Month || day.Date.Year != _displayedMonth.Year;
        _displayedMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        Reservation.ClearCalendarSelection();

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
        if (e.Result.Success && e.Result.NewReservations == 0 && e.Result.UpdatedReservations == 0)
        {
            return;
        }

        _syncRefreshCts?.Cancel();
        var cts = new CancellationTokenSource();
        _syncRefreshCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, cts.Token);
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }

                    _syncStatusText = e.Result.Success ? "Website sync ready" : "Website sync error";
                    if (e.Result.Success)
                    {
                        await LoadReservationsAsync(showBusy: false);
                    }
                    else
                    {
                        OnPropertyChanged(nameof(SyncStatusText));
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private void AttachReservationSyncHandler()
    {
        if (_reservationSyncService == null || _syncHandlerAttached)
        {
            return;
        }

        _reservationSyncService.SyncCompleted += OnReservationSyncCompleted;
        _syncHandlerAttached = true;
    }

    private void DetachReservationSyncHandler()
    {
        if (_reservationSyncService == null || !_syncHandlerAttached)
        {
            return;
        }

        _reservationSyncService.SyncCompleted -= OnReservationSyncCompleted;
        _syncHandlerAttached = false;
    }

    private static ReservationRow ToReservationRow(CloudReservation reservation)
    {
        var specialRequests = CleanSpecialRequests(reservation.Notes);
        var noteParts = new[]
        {
            reservation.IsPendingUpload ? "Pending cloud upload" : "",
            string.IsNullOrWhiteSpace(reservation.CustomerEmail) ? "" : $"Email: {reservation.CustomerEmail}",
            string.IsNullOrWhiteSpace(reservation.Allergies) ? "" : $"Allergies: {reservation.Allergies}",
            string.IsNullOrWhiteSpace(specialRequests) ? "" : $"Requests: {specialRequests}"
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
            reservation.Source,
            reservation.CustomerEmail,
            reservation.PromoCode,
            reservation.Allergies,
            specialRequests);
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

    private async Task UpdateReservationAttendanceAsync(ReservationRow row, string status)
    {
        if (_reservationSyncService == null)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Reservation", "Reservation sync service is not available.");
            return;
        }

        try
        {
            IsLoading = true;
            _syncStatusText = "Updating reservation...";
            OnPropertyChanged(nameof(SyncStatusText));

            var result = await _reservationSyncService.UpdateReservationStatusAsync(row.CloudId, status, row.LocalId);
            await LoadReservationsAsync(showBusy: false);

            if (!result.Success)
            {
                NotificationService.Instance.ShowWarning(result.Message, "Reservation");
            }
            else
            {
                NotificationService.Instance.ShowSuccess(result.Message, "Reservation");
            }
        }
        finally
        {
            IsLoading = false;
        }
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
