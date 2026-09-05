using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using OrderWeb.Contracts.Orders;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;

namespace OrderWeb.SharedUI.ViewModels;

/// <summary>
/// Shared order-entry ViewModel (Phase 12).
/// Child may calculate a temporary display total; Mother returns the authoritative order.
/// Every mutation includes RequestId, TerminalId, SessionId, OrderId, OrderRevision, Timestamp.
/// </summary>
public sealed class OrderEntryViewModel : INotifyPropertyChanged
{
    private readonly IOrderService _orders;
    private readonly IMenuCatalogService _menu;
    private readonly Func<(string TerminalId, string SessionId)> _identity;
    private readonly Func<Task<string?>>? _requestManagerApproval;

    private string? _orderId;
    private long _revision;
    private string? _tableId;
    private string? _tableName;
    private string? _selectedCategoryId;
    private string? _selectedLineId;
    private string? _statusMessage;
    private string _orderStatus = "Open";
    private string? _pendingNotes;
    private bool _isBusy;
    private bool _isDisplayEstimate;
    private decimal _displaySubtotal;
    private decimal _displayTax;
    private decimal _displayDiscount;
    private decimal _displayTotal;
    private decimal _authoritativeSubtotal;
    private decimal _authoritativeTax;
    private decimal _authoritativeDiscount;
    private decimal _authoritativeTotal;
    private int _guestCount = 1;
    private string _orderType = "Table";
    private decimal _displayServiceCharge;
    private decimal _authoritativeServiceCharge;

    public OrderEntryViewModel(
        IOrderService orders,
        IMenuCatalogService menu,
        Func<(string TerminalId, string SessionId)> identity,
        Func<Task<string?>>? requestManagerApproval = null)
    {
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        _menu = menu ?? throw new ArgumentNullException(nameof(menu));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _requestManagerApproval = requestManagerApproval;

        Categories = new ObservableCollection<MenuCategoryDto>();
        Products = new ObservableCollection<MenuProductDto>();
        Lines = new ObservableCollection<OrderLineDto>();
        ModifierGroups = new ObservableCollection<ProductModifierGroupDto>();
        SelectedModifierIds = new ObservableCollection<string>();

        SelectCategoryCommand = new RelayCommand<string>(async id =>
        {
            if (!string.IsNullOrWhiteSpace(id))
                await SelectCategoryAsync(id);
        });
        AddProductCommand = new RelayCommand<string>(async id =>
        {
            if (!string.IsNullOrWhiteSpace(id))
                await AddProductAsync(id);
        });
        IncreaseQuantityCommand = new RelayCommand<string>(async id =>
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            var line = Lines.FirstOrDefault(l => l.Id == id);
            if (line is not null)
                await ChangeQuantityAsync(id, line.Quantity + 1);
        });
        DecreaseQuantityCommand = new RelayCommand<string>(async id =>
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            var line = Lines.FirstOrDefault(l => l.Id == id);
            if (line is not null && line.Quantity > 1)
                await ChangeQuantityAsync(id, line.Quantity - 1);
        });
        VoidLineCommand = new RelayCommand<string>(async id =>
        {
            if (!string.IsNullOrWhiteSpace(id))
                await VoidLineAsync(id);
        });
        SendOrderCommand = new RelayCommand(async () => await SendOrderAsync());
        ApplyDiscountCommand = new RelayCommand(async () => await ApplyDiscountAsync(DiscountDraftAmount, DiscountDraftIsPercent));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<OrderDto>? OrderUpdated;
    public event EventHandler<OperationError>? ErrorOccurred;
    public event EventHandler<OperationError>? ManagerApprovalRequired;

    public ObservableCollection<MenuCategoryDto> Categories { get; }
    public ObservableCollection<MenuProductDto> Products { get; }
    public ObservableCollection<OrderLineDto> Lines { get; }
    public ObservableCollection<ProductModifierGroupDto> ModifierGroups { get; }
    public ObservableCollection<string> SelectedModifierIds { get; }

    public ICommand SelectCategoryCommand { get; }
    public ICommand AddProductCommand { get; }
    public ICommand IncreaseQuantityCommand { get; }
    public ICommand DecreaseQuantityCommand { get; }
    public ICommand VoidLineCommand { get; }
    public ICommand SendOrderCommand { get; }
    public ICommand ApplyDiscountCommand { get; }

    public string? OrderId { get => _orderId; private set => SetField(ref _orderId, value); }
    public long Revision { get => _revision; private set => SetField(ref _revision, value); }
    public string? TableId { get => _tableId; private set => SetField(ref _tableId, value); }
    public string? TableName { get => _tableName; private set => SetField(ref _tableName, value); }
    public int GuestCount { get => _guestCount; private set => SetField(ref _guestCount, value); }
    public string OrderType { get => _orderType; private set => SetField(ref _orderType, value); }
    public string? SelectedCategoryId { get => _selectedCategoryId; private set => SetField(ref _selectedCategoryId, value); }
    public string? SelectedLineId { get => _selectedLineId; set => SetField(ref _selectedLineId, value); }
    public string? StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public string OrderStatus { get => _orderStatus; private set => SetField(ref _orderStatus, value); }
    public string? PendingNotes { get => _pendingNotes; set => SetField(ref _pendingNotes, value); }
    public bool IsBusy { get => _isBusy; private set => SetField(ref _isBusy, value); }
    public bool IsDisplayEstimate { get => _isDisplayEstimate; private set => SetField(ref _isDisplayEstimate, value); }

    public decimal DisplaySubtotal { get => _displaySubtotal; private set { if (SetField(ref _displaySubtotal, value)) NotifyMoney(); } }
    public decimal DisplayTax { get => _displayTax; private set { if (SetField(ref _displayTax, value)) NotifyMoney(); } }
    public decimal DisplayDiscount { get => _displayDiscount; private set { if (SetField(ref _displayDiscount, value)) NotifyMoney(); } }
    public decimal DisplayTotal { get => _displayTotal; private set { if (SetField(ref _displayTotal, value)) NotifyMoney(); } }
    public decimal DisplayServiceCharge { get => _displayServiceCharge; private set { if (SetField(ref _displayServiceCharge, value)) NotifyMoney(); } }
    public decimal AuthoritativeServiceCharge { get => _authoritativeServiceCharge; private set => SetField(ref _authoritativeServiceCharge, value); }
    public decimal AuthoritativeSubtotal { get => _authoritativeSubtotal; private set => SetField(ref _authoritativeSubtotal, value); }
    public decimal AuthoritativeTax { get => _authoritativeTax; private set => SetField(ref _authoritativeTax, value); }
    public decimal AuthoritativeDiscount { get => _authoritativeDiscount; private set => SetField(ref _authoritativeDiscount, value); }
    public decimal AuthoritativeTotal { get => _authoritativeTotal; private set => SetField(ref _authoritativeTotal, value); }

    public string DisplaySubtotalText => FormatMoney(DisplaySubtotal);
    public string DisplayTaxText => FormatMoney(DisplayTax);
    public string DisplayDiscountText => FormatMoney(DisplayDiscount);
    public string DisplayTotalText => FormatMoney(DisplayTotal);
    public string DisplayServiceChargeText => FormatMoney(DisplayServiceCharge);

    public decimal DiscountDraftAmount { get; set; }
    public bool DiscountDraftIsPercent { get; set; }

    public async Task InitializeAsync(
        string? tableId = null,
        int guestCount = 1,
        string? existingOrderId = null,
        string orderType = "Table",
        CancellationToken cancellationToken = default)
    {
        TableId = tableId;
        GuestCount = Math.Max(1, guestCount);
        OrderType = string.IsNullOrWhiteSpace(orderType) ? "Table" : orderType.Trim();
        await LoadMenuAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(existingOrderId))
        {
            await RefreshOrderAsync(existingOrderId!, cancellationToken).ConfigureAwait(false);
            return;
        }

        // ClientOrderService → Mother authority. Estimates may show locally,
        // but send waits for Mother's authoritative response.
        await OpenOrCreateAsync(
            string.IsNullOrWhiteSpace(tableId) ? null : tableId,
            GuestCount,
            OrderType,
            cancellationToken).ConfigureAwait(false);
    }

    public void ToggleModifier(string modifierId)
    {
        if (SelectedModifierIds.Contains(modifierId))
            SelectedModifierIds.Remove(modifierId);
        else
            SelectedModifierIds.Add(modifierId);
    }

    public async Task OpenOrCreateAsync(
        string? tableId,
        int guestCount = 1,
        string orderType = "Table",
        CancellationToken cancellationToken = default)
    {
        OrderType = string.IsNullOrWhiteSpace(orderType) ? "Table" : orderType.Trim();
        await RunMutationAsync(() =>
        {
            var context = CreateContext(orderId: null, revision: 0);
            var request = new OpenOrderRequest(context, tableId, Math.Max(1, guestCount), OrderType);
            return _orders.OpenOrCreateAsync(request, cancellationToken);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddProductAsync(string productId, int quantity = 1, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(OrderId))
        {
            RaiseError(OperationError.Validation("No open order. Open a table first."));
            return;
        }

        ApplyOptimisticAdd(productId, quantity);

        var modifiers = SelectedModifierIds.ToList();
        var notes = PendingNotes;
        PendingNotes = null;
        SelectedModifierIds.Clear();

        await RunMutationAsync(() =>
        {
            var context = CreateContext(OrderId, Revision);
            var request = new AddOrderLineRequest(context, productId, quantity, notes, modifiers);
            return _orders.AddLineAsync(request, cancellationToken);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ChangeQuantityAsync(string lineId, int quantity, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(OrderId))
            return;

        ApplyOptimisticQuantity(lineId, quantity);

        await RunMutationAsync(() =>
        {
            var context = CreateContext(OrderId, Revision);
            return _orders.UpdateLineQuantityAsync(new UpdateOrderLineQuantityRequest(context, lineId, quantity), cancellationToken);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetNotesAsync(string lineId, string notes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(OrderId))
            return;

        await RunMutationAsync(() =>
        {
            var context = CreateContext(OrderId, Revision);
            return _orders.UpdateLineNotesAsync(new UpdateOrderLineNotesRequest(context, lineId, notes), cancellationToken);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyDiscountAsync(decimal amount, bool isPercent = false, string? reason = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(OrderId))
            return;

        await RunMutationAsync(async () =>
        {
            var context = CreateContext(OrderId, Revision);
            var request = new ApplyDiscountRequest(context, amount, isPercent, reason, null);
            var result = await _orders.ApplyDiscountAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess && result.Error?.Code == OperationErrorCode.PermissionDenied)
            {
                var code = await RequestManagerApprovalAsync(result.Error).ConfigureAwait(false);
                if (code is null)
                    return result;
                context = CreateContext(OrderId, Revision);
                request = new ApplyDiscountRequest(context, amount, isPercent, reason, code);
                result = await _orders.ApplyDiscountAsync(request, cancellationToken).ConfigureAwait(false);
            }
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task VoidLineAsync(string lineId, string? reason = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(OrderId))
            return;

        await RunMutationAsync(async () =>
        {
            var context = CreateContext(OrderId, Revision);
            var request = new VoidOrderLineRequest(context, lineId, reason, null);
            var result = await _orders.VoidLineAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess && result.Error?.Code == OperationErrorCode.PermissionDenied)
            {
                var code = await RequestManagerApprovalAsync(result.Error).ConfigureAwait(false);
                if (code is null)
                    return result;
                context = CreateContext(OrderId, Revision);
                request = new VoidOrderLineRequest(context, lineId, reason, code);
                result = await _orders.VoidLineAsync(request, cancellationToken).ConfigureAwait(false);
            }
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendOrderAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(OrderId))
            return;

        StatusMessage = "Submitting to Mother — waiting for authoritative confirmation…";
        IsDisplayEstimate = true;
        await RunMutationAsync(() =>
        {
            var context = CreateContext(OrderId, Revision);
            return _orders.SendAsync(new SendOrderRequest(context), cancellationToken);
        }, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(OrderId) && !IsDisplayEstimate)
            StatusMessage = "Order submitted — Mother confirmation received.";
    }

    public async Task RefreshOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var result = await _orders.GetAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            RaiseError(result.Error ?? OperationError.NotFound("Order not found."));
            return;
        }

        ApplyAuthoritativeOrder(result.Value);
    }

    private OrderMutationContext CreateContext(string? orderId, long revision)
    {
        var (terminalId, sessionId) = _identity();
        return OrderMutationContextFactory.Create(terminalId, sessionId, orderId, revision);
    }

    private async Task RunMutationAsync(Func<Task<OperationResult<OrderDto>>> action, CancellationToken cancellationToken)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var result = await action().ConfigureAwait(false);
            if (!result.IsSuccess || result.Value is null)
            {
                if (result.Error?.Code == OperationErrorCode.Conflict && !string.IsNullOrWhiteSpace(OrderId))
                {
                    StatusMessage = "Order changed on another terminal. Refreshing...";
                    await RefreshOrderAsync(OrderId!, cancellationToken).ConfigureAwait(false);
                }

                RaiseError(result.Error ?? OperationError.Failure("Order mutation failed."));
                RecalculateDisplayTotalsFromAuthoritative();
                return;
            }

            ApplyAuthoritativeOrder(result.Value);
        }
        catch (Exception ex)
        {
            RaiseError(OperationError.Failure(ex.Message));
            RecalculateDisplayTotalsFromAuthoritative();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyAuthoritativeOrder(OrderDto order)
    {
        OrderId = order.Id;
        Revision = order.Revision;
        TableId = order.TableId;
        TableName = order.TableName;
        GuestCount = order.GuestCount;
        OrderStatus = order.Status;

        AuthoritativeSubtotal = order.Totals.Subtotal;
        AuthoritativeTax = order.Totals.TaxTotal;
        AuthoritativeDiscount = order.Totals.DiscountTotal;
        AuthoritativeTotal = order.Totals.GrandTotal;

        Lines.Clear();
        foreach (var line in order.Lines.Where(l => !l.IsVoided))
            Lines.Add(line);

        DisplaySubtotal = order.Totals.Subtotal;
        DisplayTax = order.Totals.TaxTotal;
        DisplayServiceCharge = order.Totals.ServiceChargeTotal;
        AuthoritativeServiceCharge = order.Totals.ServiceChargeTotal;
        DisplayDiscount = order.Totals.DiscountTotal;
        DisplayTotal = order.Totals.GrandTotal;
        IsDisplayEstimate = false;
        StatusMessage = "Authoritative order from Mother.";
        OrderUpdated?.Invoke(this, order);
    }

    private void ApplyOptimisticAdd(string productId, int quantity)
    {
        var product = Products.FirstOrDefault(p => p.Id == productId);
        if (product is null)
            return;

        DisplaySubtotal += product.Price * quantity;
        DisplayTax = Math.Round(DisplaySubtotal * 0.20m, 2, MidpointRounding.AwayFromZero);
        DisplayTotal = DisplaySubtotal + DisplayTax - DisplayDiscount;
        IsDisplayEstimate = true;
        StatusMessage = "Display total (pending Mother revalidation).";
    }

    private void ApplyOptimisticQuantity(string lineId, int quantity)
    {
        var line = Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            return;

        var deltaQty = quantity - line.Quantity;
        DisplaySubtotal += line.UnitPrice * deltaQty;
        DisplayTax = Math.Round(DisplaySubtotal * 0.20m, 2, MidpointRounding.AwayFromZero);
        DisplayTotal = DisplaySubtotal + DisplayTax - DisplayDiscount;
        IsDisplayEstimate = true;
        StatusMessage = "Display total (pending Mother revalidation).";
    }

    private void RecalculateDisplayTotalsFromAuthoritative()
    {
        DisplaySubtotal = AuthoritativeSubtotal;
        DisplayTax = AuthoritativeTax;
        DisplayDiscount = AuthoritativeDiscount;
        DisplayTotal = AuthoritativeTotal;
        IsDisplayEstimate = false;
    }

    private async Task<string?> RequestManagerApprovalAsync(OperationError error)
    {
        ManagerApprovalRequired?.Invoke(this, error);
        if (_requestManagerApproval is null)
            return null;
        return await _requestManagerApproval().ConfigureAwait(false);
    }

    private void RaiseError(OperationError error)
    {
        StatusMessage = error.Message;
        ErrorOccurred?.Invoke(this, error);
    }

    private void NotifyMoney()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplaySubtotalText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTaxText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayDiscountText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTotalText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayServiceChargeText)));
    }

    private static string FormatMoney(decimal value) => value.ToString("C", CultureInfo.GetCultureInfo("en-GB"));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

internal sealed class RelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    public RelayCommand(Func<Task> execute) => _execute = execute;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public async void Execute(object? parameter) => await _execute();
}

internal sealed class RelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    public RelayCommand(Func<T?, Task> execute) => _execute = execute;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public async void Execute(object? parameter) => await _execute(parameter is T t ? t : default);
}
