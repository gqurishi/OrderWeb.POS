using System.Collections.ObjectModel;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class RiderPage : ContentPage
{
    private readonly RiderOperationsService _riderService;
    private readonly AuthenticationService _authenticationService;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly ObservableCollection<RiderOperation> _visibleOrders = new();
    private List<RiderOperation> _allOrders = new();
    private CancellationTokenSource? _pageCts;
    private IDispatcherTimer? _refreshTimer;
    private string _filter = "all";
    private bool _subscribed;

    public RiderPage()
    {
        InitializeComponent();
        _riderService = ServiceHelper.GetService<RiderOperationsService>()
            ?? throw new InvalidOperationException("Rider operations service is unavailable.");
        _authenticationService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        RiderOrdersCollection.ItemsSource = _visibleOrders;
        TopBar.SetPageTitle("Rider");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _pageCts = new CancellationTokenSource();
        if (!_subscribed)
        {
            AppDataRefreshService.DataChanged += OnDataChanged;
            _subscribed = true;
        }
        ConfigureBusinessDayLabel();
        StartTimer();
        _ = LoadAsync(showSpinner: true);
    }

    protected override void OnDisappearing()
    {
        _pageCts?.Cancel();
        _pageCts?.Dispose();
        _pageCts = null;
        if (_refreshTimer != null) _refreshTimer.Stop();
        if (_subscribed)
        {
            AppDataRefreshService.DataChanged -= OnDataChanged;
            _subscribed = false;
        }
        base.OnDisappearing();
    }

    private void StartTimer()
    {
        _refreshTimer ??= Dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(20);
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e) => _ = LoadAsync(showSpinner: false);

    private void OnDataChanged(object? sender, AppDataChangedEventArgs e)
    {
        if (e.HasKind(AppDataChangeKind.Orders)) _ = LoadAsync(showSpinner: false);
    }

    private void ConfigureBusinessDayLabel()
    {
        var start = RiderOperationPolicy.GetDayStart(DateTime.Now);
        var end = RiderOperationPolicy.GetDayEnd(DateTime.Now);
        BusinessDayLabel.Text = $"Operational day · {start:ddd d MMM, 2:00 tt} to {end:ddd d MMM, 1:59 tt}";
    }

    private async Task LoadAsync(bool showSpinner)
    {
        var token = _pageCts?.Token ?? CancellationToken.None;
        if (!await _loadGate.WaitAsync(0, token)) return;
        try
        {
            if (showSpinner)
            {
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;
            }
            await _riderService.RefreshActiveDispatchesAsync(token);
            _allOrders = (await _riderService.GetBoardAsync(DateTime.Now, token)).ToList();
            ApplyFilter();
            ConfigureBusinessDayLabel();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Rider board load failed: {ex.Message}");
            if (showSpinner)
                await AppAlertService.ShowAlertAsync("Rider Board", "Delivery operations could not be loaded. Check that the latest database update has been installed.");
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
            _loadGate.Release();
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<RiderOperation> rows = _filter switch
        {
            "kitchen" => _allOrders.Where(o => o.OperationStatusDisplay == "Awaiting kitchen"),
            "ready" => _allOrders.Where(o => o.OperationStatusDisplay == "Ready for rider"),
            "quote" => _allOrders.Where(o => o.OperationStatusDisplay is "Quote available" or "Requesting quote"),
            "active" => _allOrders.Where(o => o.OperationStatusDisplay is "Finding rider" or "Rider assigned" or "Collected" or "Delivering"),
            "delivered" => _allOrders.Where(o => o.OperationStatusDisplay == "Delivered"),
            "problems" => _allOrders.Where(o => o.HasProblem),
            _ => _allOrders
        };

        _visibleOrders.Clear();
        foreach (var row in rows) _visibleOrders.Add(row);
        UpdateFilterButtons();
    }

    private void UpdateFilterButtons()
    {
        foreach (var button in FilterTabs.Children.OfType<Button>())
        {
            var key = button.CommandParameter?.ToString() ?? "all";
            var count = key switch
            {
                "kitchen" => _allOrders.Count(o => o.OperationStatusDisplay == "Awaiting kitchen"),
                "ready" => _allOrders.Count(o => o.OperationStatusDisplay == "Ready for rider"),
                "quote" => _allOrders.Count(o => o.OperationStatusDisplay is "Quote available" or "Requesting quote"),
                "active" => _allOrders.Count(o => o.OperationStatusDisplay is "Finding rider" or "Rider assigned" or "Collected" or "Delivering"),
                "delivered" => _allOrders.Count(o => o.OperationStatusDisplay == "Delivered"),
                "problems" => _allOrders.Count(o => o.HasProblem),
                _ => _allOrders.Count
            };
            var title = key switch
            {
                "kitchen" => "Kitchen", "ready" => "Ready", "quote" => "Quote",
                "active" => "Active rider", "delivered" => "Delivered", "problems" => "Problems", _ => "All"
            };
            button.Text = $"{title}  {count}";
            var active = key == _filter;
            button.BackgroundColor = Color.FromArgb(active ? "#0F766E" : "#E2E8F0");
            button.TextColor = Color.FromArgb(active ? "#FFFFFF" : "#334155");
        }
    }

    private async void OnRefreshClicked(object sender, EventArgs e) => await LoadAsync(showSpinner: true);

    private void OnFilterClicked(object sender, EventArgs e)
    {
        if (sender is not Button selected) return;
        _filter = selected.CommandParameter?.ToString() ?? "all";
        ApplyFilter();
    }

    private async void OnPrimaryActionClicked(object sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: RiderOperation operation } || operation.IsBusy) return;
        operation.IsBusy = true;
        try
        {
            if (operation.HasQuote)
            {
                var collectionText = operation.CashCollectionAmount > 0
                    ? $"The rider must collect £{operation.CashCollectionAmount:0.00}."
                    : "The rider must collect £0.00; this order is already paid.";
                var confirmed = await DisplayAlert(
                    "Confirm Rider",
                    $"Confirm {operation.QuoteCurrency} {operation.QuoteAmount:0.00} for {operation.DisplayOrderNumber}?\n\n{collectionText}",
                    "Confirm Rider",
                    "Back");
                if (!confirmed) return;

                var result = await _riderService.ConfirmDispatchAsync(operation.OrderDbId, _authenticationService.CurrentUser, _pageCts?.Token ?? CancellationToken.None);
                await AppAlertService.ShowAlertAsync(result.Success ? "Rider Confirmed" : "Rider Request", result.Message);
            }
            else
            {
                var result = await _riderService.RequestQuoteAsync(operation.OrderDbId, _authenticationService.CurrentUser, _pageCts?.Token ?? CancellationToken.None);
                if (!result.Success) await AppAlertService.ShowAlertAsync("Rider Quote", result.Message);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            operation.IsBusy = false;
            await LoadAsync(showSpinner: false);
        }
    }
}
