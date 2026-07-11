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
        TopBar.SetPageTitle("Reservation");
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        _reservationSyncService = ServiceHelper.GetService<ReservationSyncService>();
        AttachReservationSyncHandler();
        BindingContext = this;
        RefreshView();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        AttachReservationSyncHandler();

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

    private async void OnReservationShowClicked(object sender, EventArgs e)
    {
        if (sender is not BindableObject bindable || bindable.BindingContext is not ReservationRow row)
        {
            return;
        }

        await UpdateReservationAttendanceAsync(row, "arrived");
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

    private async void OnReservationNoShowClicked(object sender, EventArgs e)
    {
        if (sender is not BindableObject bindable || bindable.BindingContext is not ReservationRow row)
        {
            return;
        }

        await UpdateReservationAttendanceAsync(row, "no_show");
    }

    private async void OnReservationCancelClicked(object sender, EventArgs e)
    {
        if (sender is not BindableObject bindable || bindable.BindingContext is not ReservationRow row)
        {
            return;
        }

        await UpdateReservationAttendanceAsync(row, "cancelled");
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
        Note = note;
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
    public string Note { get; }
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
    public string ReferenceDisplay => string.IsNullOrWhiteSpace(Reference) ? "No reference" : Reference;
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
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
    public bool HasPromoCode => !string.IsNullOrWhiteSpace(PromoCode);
    public bool HasAllergies => !string.IsNullOrWhiteSpace(Allergies);
    public bool HasSpecialRequests => !string.IsNullOrWhiteSpace(SpecialRequests)
                                      && !IsUploadDiagnostic(SpecialRequests);
    public bool HasAnySpecialInfo => HasAllergies || HasSpecialRequests;
    public string PromoBadgeText => HasPromoCode ? $"Promo: {PromoCode}" : string.Empty;
    public string PromoColumnText => HasPromoCode ? PromoCode : "-";
    public Color PromoColumnTextColor => HasPromoCode ? Color.FromArgb("#1D4ED8") : Color.FromArgb("#94A3B8");
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

    private static bool IsUploadDiagnostic(string value)
    {
        var text = value.Trim();
        return text.StartsWith("Input string was not in a correct format", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("HTTP ", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Cloud upload failed", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Invalid cloud response", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsArrived => NormalizedStatus is "arrived" or "show" or "shown" or "seated";
    public bool IsNoShow => string.Equals(Status, "No Show", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(Status, "No-show", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(NormalizedStatus, "no_show", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(NormalizedStatus, "noshow", StringComparison.OrdinalIgnoreCase);
    public bool IsCancelled => string.Equals(Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(Status, "Canceled", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(NormalizedStatus, "cancelled", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(NormalizedStatus, "canceled", StringComparison.OrdinalIgnoreCase);
    public bool IsFinalAttendance => IsArrived || IsNoShow || IsCancelled;
    public bool IsShowButtonVisible => !IsNoShow && !IsCancelled;
    public bool IsNoShowButtonVisible => !IsArrived && !IsCancelled;
    public bool IsCancelButtonVisible => !IsArrived && !IsNoShow;
    public bool IsShowButtonEnabled => !IsFinalAttendance;
    public bool IsNoShowButtonEnabled => !IsFinalAttendance;
    public bool IsCancelButtonEnabled => !IsFinalAttendance;
    public string ShowButtonText => IsArrived ? "Shown" : "Show";
    public string NoShowButtonText => IsNoShow ? "No Show" : "No Show";
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
