using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class StaffAttendancePage : ContentPage
{
    private readonly TimeClockService _timeClockService;
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;
    private DateTime _startDate;
    private DateTime _endDate;

    public StaffAttendancePage()
    {
        InitializeComponent();
        _timeClockService = ServiceHelper.GetService<TimeClockService>() ?? new TimeClockService(new DatabaseService());
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        TopBar.SetPageTitle("Staff Clock");

        var today = TradingDayHelper.GetBusinessDate();
        _startDate = today;
        _endDate = today;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!await SessionAccessGuard.RequireSignedInAsync(_authService))
        {
            return;
        }

        if (!_roleAccessService.IsAdmin(_authService.CurrentUser?.Role))
        {
            await AppAlertService.ShowAlertAsync("Access Denied", "Only Admin can view Staff Clock.");
            await NavigationCoordinator.Shared.NavigateShellAsync(_roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role));
            return;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var (rows, summary) = await _timeClockService.GetLabourReportAsync(_startDate, _endDate);

            TotalHoursLabel.Text = summary.TotalHoursDisplay;
            StaffCountLabel.Text = summary.StaffCount.ToString();
            OpenShiftsLabel.Text = summary.OpenSessionCount.ToString();
            PendingCloudLabel.Text = rows.Count(r => !r.IsSynced && !r.IsOpenShift).ToString();

            SessionsCollection.ItemsSource = rows
                .OrderByDescending(r => r.IsOpenShift)
                .ThenByDescending(r => r.ClockInAt)
                .ToList();

            var rangeText = _startDate.Date == _endDate.Date
                ? _startDate.ToString("ddd dd MMM yyyy")
                : $"{_startDate:dd MMM} – {_endDate:dd MMM yyyy}";
            SummaryLabel.Text = $"{rows.Count} session(s) · {rangeText} · cloud upload with daily report";
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Staff Clock", $"Could not load attendance: {ex.Message}");
        }
    }

    private async void OnRefreshClicked(object sender, EventArgs e) => await LoadAsync();

    private async void OnTodayClicked(object sender, EventArgs e)
    {
        var today = TradingDayHelper.GetBusinessDate();
        _startDate = today;
        _endDate = today;
        SetRangeButtonStyles(TodayButton, SevenDaysButton);
        await LoadAsync();
    }

    private async void OnSevenDaysClicked(object sender, EventArgs e)
    {
        _endDate = TradingDayHelper.GetBusinessDate();
        _startDate = _endDate.AddDays(-6);
        SetRangeButtonStyles(SevenDaysButton, TodayButton);
        await LoadAsync();
    }

    private static void SetRangeButtonStyles(Button active, Button inactive)
    {
        active.BackgroundColor = Color.FromArgb("#6366F1");
        active.TextColor = Colors.White;
        inactive.BackgroundColor = Color.FromArgb("#E2E8F0");
        inactive.TextColor = Color.FromArgb("#334155");
    }
}
