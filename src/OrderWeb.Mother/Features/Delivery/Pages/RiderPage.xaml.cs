using OrderWeb.SharedUI.Views;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class RiderPage : ContentPage
{
    private readonly RiderOperationsService _riderService;
    private readonly AuthenticationService _authenticationService;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private CancellationTokenSource? _pageCts;
    private IDispatcherTimer? _refreshTimer;
    private bool _subscribed;
    private bool _pendingReload;
    private List<RiderOperation> _allOrders = new();

    public RiderPage()
    {
        InitializeComponent();
        _riderService = ServiceHelper.GetService<RiderOperationsService>()
            ?? throw new InvalidOperationException("Rider operations service is unavailable.");
        _authenticationService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        TopBar.SetPageTitle("Rider");
        Board.RefreshRequested += async (_, _) => await LoadAsync(showSpinner: true);
        Board.ActionRequested += async (_, e) => await OnActionAsync(e.Card);
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
        Board.SetDayText(FormatDay(DateTime.Now));
        StartTimer();
        _ = LoadAsync(showSpinner: true);
    }

    protected override void OnDisappearing()
    {
        _pageCts?.Cancel();
        _pageCts?.Dispose();
        _pageCts = null;
        _refreshTimer?.Stop();
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
        _refreshTimer.Interval = TimeSpan.FromSeconds(30);
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e) => _ = LoadAsync(showSpinner: false);

    private void OnDataChanged(object? sender, AppDataChangedEventArgs e)
    {
        if (e.HasKind(AppDataChangeKind.Orders))
        {
            _ = LoadAsync(showSpinner: false);
        }
    }

    private async Task LoadAsync(bool showSpinner)
    {
        var token = _pageCts?.Token ?? CancellationToken.None;
        if (!await _loadGate.WaitAsync(0, token))
        {
            _pendingReload = true;
            return;
        }

        try
        {
            if (showSpinner)
            {
                Board.SetLoading(true);
            }

            await _riderService.RefreshActiveDispatchesAsync(token);
            _allOrders = (await _riderService.GetBoardAsync(DateTime.Now, token)).ToList();
            Board.SetDayText(FormatDay(DateTime.Now));
            Board.SetCards(_allOrders.Select(RiderBoardMapping.ToCard).ToList());
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Rider board load failed: {ex.Message}");
            if (showSpinner)
            {
                await AppAlertService.ShowAlertAsync("Rider Board", "Delivery operations could not be loaded. Check that the latest database update has been installed.");
            }
        }
        finally
        {
            Board.SetLoading(false);
            _loadGate.Release();
            if (_pendingReload)
            {
                _pendingReload = false;
                _ = LoadAsync(showSpinner: false);
            }
        }
    }

    private async Task OnActionAsync(RiderBoardCard card)
    {
        if (!int.TryParse(card.Id, out var orderDbId))
        {
            return;
        }

        var operation = _allOrders.FirstOrDefault(row => row.OrderDbId == orderDbId);
        if (operation == null || operation.IsBusy)
        {
            return;
        }

        operation.IsBusy = true;
        try
        {
            if (card.IsConfirm)
            {
                var collectionText = operation.CashCollectionAmount > 0
                    ? $"The rider must collect £{operation.CashCollectionAmount:0.00}."
                    : "The rider must collect £0.00; this order is already paid.";
                var confirmed = await DisplayAlert(
                    "Confirm Rider",
                    $"Confirm {operation.QuoteCurrency} {operation.QuoteAmount:0.00} for {operation.DisplayOrderNumber}?\n\n{collectionText}",
                    "Confirm Rider",
                    "Back");
                if (!confirmed)
                {
                    return;
                }

                var result = await _riderService.ConfirmDispatchAsync(orderDbId, _authenticationService.CurrentUser, _pageCts?.Token ?? CancellationToken.None);
                await AppAlertService.ShowAlertAsync(result.Success ? "Rider Confirmed" : "Rider Request", result.Message);
            }
            else
            {
                var result = await _riderService.RequestQuoteAsync(orderDbId, _authenticationService.CurrentUser, _pageCts?.Token ?? CancellationToken.None);
                if (!result.Success)
                {
                    await AppAlertService.ShowAlertAsync("Rider Quote", result.Message);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            operation.IsBusy = false;
            await LoadAsync(showSpinner: false);
        }
    }

    internal static string FormatDay(DateTime now)
    {
        var start = RiderOperationPolicy.GetDayStart(now);
        var end = RiderOperationPolicy.GetDayEnd(now);
        return $"Operational day · {start:ddd d MMM, h:mm tt} to {end.AddMinutes(-1):ddd d MMM, h:mm tt}";
    }
}

internal static class RiderBoardMapping
{
    public static RiderBoardCard ToCard(RiderOperation operation) => new()
    {
        Id = operation.OrderDbId.ToString(),
        OrderNumber = operation.DisplayOrderNumber,
        Source = operation.SourceDisplay,
        TimeText = operation.TimeDisplay,
        StatusText = operation.OperationStatusDisplay,
        StatusColor = operation.StatusColor,
        CustomerName = operation.CustomerDisplay,
        Phone = operation.CustomerPhone,
        Address = operation.CustomerAddress,
        TotalText = operation.TotalDisplay,
        PaymentText = operation.PaymentDisplay,
        CollectText = operation.CashCollectionDisplay,
        QuoteText = operation.QuoteDisplay,
        HasRider = operation.HasRider,
        RiderName = operation.RiderName,
        RiderPhone = operation.RiderPhone,
        ErrorText = operation.LastError,
        ActionText = operation.IsBusy ? "Please wait…" : operation.PrimaryActionText,
        ActionEnabled = operation.HasQuote ? operation.CanConfirmQuote : operation.CanRequestQuote,
        IsConfirm = operation.HasQuote,
        IsProblem = operation.HasProblem
    };
}
