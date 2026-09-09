using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OrderWeb.SharedUI.Hosting;

/// <summary>
/// Order Place order kind for one shared shell (Table / Collection / Delivery).
/// Hosts map these to Mother/Client canonical types (<c>table</c>, <c>pickup</c>, <c>delivery</c>).
/// </summary>
public enum OrderPlaceOrderKind
{
    Table,
    Collection,
    Delivery
}

/// <summary>
/// Host-neutral snapshot for the Order Place chrome.
/// SharedUI may render from this; hosts own persistence, pricing, and Mother/HTTP.
/// </summary>
public sealed class OrderPlaceSessionState : INotifyPropertyChanged
{
    private OrderPlaceOrderKind _kind = OrderPlaceOrderKind.Table;
    private string _headerTitle = string.Empty;
    private string _headerDetail = string.Empty;
    private string? _selectedCategoryId;
    private string? _selectedSubcategoryId;
    private decimal _subtotal;
    private decimal _discount;
    private decimal _serviceCharge;
    private decimal _deliveryFee;
    private decimal _total;
    private bool _showServiceCharge;
    private bool _showDeliveryFee;
    private bool _isBusy;
    private string? _statusMessage;
    private string _printActionLabel = "PRINT";
    private string _paymentActionLabel = "PAYMENT";
    private string _sendActionLabel = "SEND TO KITCHEN";
    private bool _actionsBusy;

    public ObservableCollection<OrderPlaceCategoryItem> Categories { get; } = [];
    public ObservableCollection<OrderPlaceCategoryItem> Subcategories { get; } = [];
    public ObservableCollection<OrderPlaceProductItem> Products { get; } = [];
    public ObservableCollection<OrderPlaceBasketLine> Lines { get; } = [];

    public OrderPlaceOrderKind Kind
    {
        get => _kind;
        set => Set(ref _kind, value);
    }

    public string HeaderTitle
    {
        get => _headerTitle;
        set => Set(ref _headerTitle, value);
    }

    public string HeaderDetail
    {
        get => _headerDetail;
        set => Set(ref _headerDetail, value);
    }

    public string? SelectedCategoryId
    {
        get => _selectedCategoryId;
        set => Set(ref _selectedCategoryId, value);
    }

    public string? SelectedSubcategoryId
    {
        get => _selectedSubcategoryId;
        set => Set(ref _selectedSubcategoryId, value);
    }

    public decimal Subtotal
    {
        get => _subtotal;
        set => Set(ref _subtotal, value);
    }

    public decimal Discount
    {
        get => _discount;
        set => Set(ref _discount, value);
    }

    public decimal ServiceCharge
    {
        get => _serviceCharge;
        set => Set(ref _serviceCharge, value);
    }

    public decimal DeliveryFee
    {
        get => _deliveryFee;
        set => Set(ref _deliveryFee, value);
    }

    public decimal Total
    {
        get => _total;
        set => Set(ref _total, value);
    }

    public bool ShowServiceCharge
    {
        get => _showServiceCharge;
        set => Set(ref _showServiceCharge, value);
    }

    public bool ShowDeliveryFee
    {
        get => _showDeliveryFee;
        set => Set(ref _showDeliveryFee, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => Set(ref _isBusy, value);
    }

    /// <summary>
    /// True while Send/Print/Pay is in flight. Does not block product taps (busy-dinner speed).
    /// </summary>
    public bool ActionsBusy
    {
        get => _actionsBusy;
        set => Set(ref _actionsBusy, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set => Set(ref _statusMessage, value);
    }

    /// <summary>Table: PRINT BILL. Takeaway: PRINT RECEIPT (receipt + kitchen).</summary>
    public string PrintActionLabel
    {
        get => _printActionLabel;
        set => Set(ref _printActionLabel, value);
    }

    public string PaymentActionLabel
    {
        get => _paymentActionLabel;
        set => Set(ref _paymentActionLabel, value);
    }

    public string SendActionLabel
    {
        get => _sendActionLabel;
        set => Set(ref _sendActionLabel, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

public sealed record OrderPlaceCategoryItem(
    string Id,
    string Name,
    string? ParentId = null,
    bool IsSpecial = false);

public sealed record OrderPlaceProductItem(
    string Id,
    string Name,
    decimal Price,
    string? Badge = null,
    string? PriceText = null,
    bool IsAvailable = true);

public sealed record OrderPlaceBasketLine(
    string Id,
    string Name,
    decimal LineTotal,
    int Quantity,
    string? Details = null,
    bool IsSent = false,
    bool ShowNoteAction = true,
    string? TrailingActionText = null);

/// <summary>
/// What Mother or Client must supply to drive SharedUI Order Place.
/// No database, HTTP, printer, or PIN logic belongs in SharedUI — only the host.
/// </summary>
/// <remarks>
/// Mother: <c>MotherOrderPlaceHost</c> + <c>OrderPlacementPageSimple</c>.
/// Client: <c>ClientOrderPlaceHost</c> + <c>OrderPage</c>.
/// Both bind the same SharedUI <c>OrderPlaceShellView</c>.
/// See <c>docs/ORDER_PLACE_CLIENT_READINESS.md</c>.
/// </remarks>
public interface IOrderPlaceHost
{
    OrderPlaceSessionState Session { get; }

    /// <summary>Raised after any host mutation that SharedUI should rebind.</summary>
    event EventHandler? StateChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SelectCategoryAsync(string categoryId, CancellationToken cancellationToken = default);

    Task SelectSubcategoryAsync(string? subcategoryId, CancellationToken cancellationToken = default);

    /// <summary>Opens host add pipeline (variant → note → addon → add) for the product.</summary>
    Task AddProductAsync(string productId, CancellationToken cancellationToken = default);

    Task SetLineQuantityAsync(string lineId, int quantity, CancellationToken cancellationToken = default);

    Task EditLineNoteAsync(string lineId, CancellationToken cancellationToken = default);

    Task TrailingLineActionAsync(string lineId, CancellationToken cancellationToken = default);

    Task OrderNotesAsync(CancellationToken cancellationToken = default);

    Task VoidAsync(CancellationToken cancellationToken = default);

    Task MoreAsync(CancellationToken cancellationToken = default);

    Task SendAsync(CancellationToken cancellationToken = default);

    Task PrintAsync(CancellationToken cancellationToken = default);

    Task PayAsync(CancellationToken cancellationToken = default);
}
