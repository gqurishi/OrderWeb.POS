using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace OrderWeb.SharedUI.ViewModels;

/// <summary>
/// Host-neutral order-entry state. Mother and Client provide data and handle
/// commands; this type contains no database, HTTP, or platform behaviour.
/// </summary>
public sealed record OrderCategoryModel(string Id, string Name, string? ParentId = null, bool IsAvailable = true);
public sealed record OrderModifierModel(string Id, string Name, decimal PriceDelta, bool IsSelected = false);
public sealed record OrderProductModel(string Id, string CategoryId, string Name, decimal Price, string? ImageSource = null, bool IsAvailable = true, IReadOnlyList<OrderModifierModel>? Modifiers = null);
public sealed record OrderBasketLineModel(string Id, string ProductId, string Name, decimal Quantity, decimal UnitPrice, string? Notes = null, IReadOnlyList<OrderModifierModel>? Modifiers = null)
{
    public decimal Total => Quantity * UnitPrice + (Modifiers?.Where(item => item.IsSelected).Sum(item => item.PriceDelta) ?? 0m) * Quantity;
}

public enum OrderEntryAction { Send, Void, More, Discount, Note, Quantity, Remove, Refresh }
public sealed record OrderEntryActionRequest(OrderEntryAction Action, string? LineId = null);

public sealed class OrderEntryViewModel : INotifyPropertyChanged
{
    private string _title = "New Order";
    private string _status = "Draft — totals are estimates until Mother confirms.";
    private string? _selectedCategoryId;
    private bool _isLoading;
    private bool _hasConflict;
    private string? _errorMessage;
    private decimal _tax;
    private decimal _serviceCharge;

    public ObservableCollection<OrderCategoryModel> Categories { get; } = [];
    public ObservableCollection<OrderProductModel> Products { get; } = [];
    public ObservableCollection<OrderBasketLineModel> Lines { get; } = [];
    public ICommand SelectCategoryCommand { get; }
    public ICommand SelectProductCommand { get; }
    public ICommand ActionCommand { get; }

    public OrderEntryViewModel()
    {
        SelectCategoryCommand = new Command<OrderCategoryModel>(category => { if (category is not null) SelectedCategoryId = category.Id; });
        SelectProductCommand = new Command<OrderProductModel>(product => { if (product?.IsAvailable == true) ProductSelected?.Invoke(this, product); });
        ActionCommand = new Command<OrderEntryActionRequest>(request => { if (request is not null) ActionRequested?.Invoke(this, request); });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<OrderProductModel>? ProductSelected;
    public event EventHandler<OrderEntryActionRequest>? ActionRequested;
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string? SelectedCategoryId { get => _selectedCategoryId; set { if (Set(ref _selectedCategoryId, value)) OnPropertyChanged(nameof(VisibleProducts)); } }
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }
    public bool HasConflict { get => _hasConflict; set => Set(ref _hasConflict, value); }
    public string? ErrorMessage { get => _errorMessage; set => Set(ref _errorMessage, value); }
    public decimal Tax { get => _tax; set { if (Set(ref _tax, value)) RaiseTotals(); } }
    public decimal ServiceCharge { get => _serviceCharge; set { if (Set(ref _serviceCharge, value)) RaiseTotals(); } }
    public IReadOnlyList<OrderProductModel> VisibleProducts => Products.Where(product => string.IsNullOrWhiteSpace(SelectedCategoryId) || product.CategoryId == SelectedCategoryId).ToList();
    public decimal Subtotal => Lines.Sum(line => line.Total);
    public decimal Total => Subtotal + Tax + ServiceCharge;
    public void ReplaceLines(IEnumerable<OrderBasketLineModel> lines) { Lines.Clear(); foreach (var line in lines) Lines.Add(line); RaiseTotals(); }
    public void ReplaceMenu(IEnumerable<OrderCategoryModel> categories, IEnumerable<OrderProductModel> products)
    {
        Categories.Clear(); foreach (var category in categories) Categories.Add(category);
        Products.Clear(); foreach (var product in products) Products.Add(product);
        SelectedCategoryId ??= Categories.FirstOrDefault()?.Id;
        OnPropertyChanged(nameof(VisibleProducts));
    }
    private void RaiseTotals() { OnPropertyChanged(nameof(Subtotal)); OnPropertyChanged(nameof(Total)); }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(name); return true; }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
