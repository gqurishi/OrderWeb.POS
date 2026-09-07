using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using OrderWeb.Contracts.Capabilities;
using OrderWeb.Contracts.Features;

namespace OrderWeb.SharedUI.ViewModels;

public sealed record DashboardTileModel(
    string Title,
    string Icon,
    string Route,
    string RequiredCapability,
    string? RequiredFeature = null,
    string? Subtitle = null,
    string? Badge = null,
    bool IsEnabled = true,
    bool IsLoading = false);

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private string _title = "Dashboard";
    private string _subtitle = "Welcome";
    private bool _isOffline;

    public DashboardViewModel()
    {
        SelectTileCommand = new Command<DashboardTileModel>(tile =>
        {
            if (tile is null) return;
            TileSelected?.Invoke(this, tile);
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<DashboardTileModel>? TileSelected;

    public ObservableCollection<DashboardTileModel> Tiles { get; } = new();
    public ICommand SelectTileCommand { get; }

    public string Title
    {
        get => _title;
        set { if (_title == value) return; _title = value; OnPropertyChanged(); }
    }

    public string Subtitle
    {
        get => _subtitle;
        set { if (_subtitle == value) return; _subtitle = value; OnPropertyChanged(); }
    }

    public bool IsOffline
    {
        get => _isOffline;
        set { if (_isOffline == value) return; _isOffline = value; OnPropertyChanged(); }
    }

    public void ApplyCapabilities(
        IReadOnlySet<string> capabilities,
        IReadOnlySet<string>? features = null,
        IReadOnlySet<string>? allowedRoutes = null)
    {
        Tiles.Clear();
        foreach (var tile in Catalog.Where(t =>
                     capabilities.Contains(t.RequiredCapability) &&
                     (allowedRoutes is null || allowedRoutes.Contains(t.Route)) &&
                     (t.RequiredFeature is null || features is null || features.Contains(t.RequiredFeature))))
        {
            Tiles.Add(tile);
        }
        OnPropertyChanged(nameof(Tiles));
    }

    /// <summary>
    /// Lets a host supply its own capability-filtered tile definitions and live
    /// badge/loading state without teaching SharedUI about that host's data source.
    /// </summary>
    public void SetTiles(IEnumerable<DashboardTileModel> tiles)
    {
        Tiles.Clear();
        foreach (var tile in tiles)
        {
            Tiles.Add(tile);
        }

        OnPropertyChanged(nameof(Tiles));
    }

    private static readonly DashboardTileModel[] Catalog =
    [
        new("Restaurant", "restaurant.png", "restaurant", PosCapabilityKeys.OpenTables, PosFeatureKeys.DineIn),
        new("Collection", "collection.png", "collection", PosCapabilityKeys.CreateOrders, PosFeatureKeys.Collection),
        new("Delivery", "delivery.png", "delivery", PosCapabilityKeys.CreateOrders, PosFeatureKeys.Delivery),
        new("Live Order", "liveorder.png", "liveorder", PosCapabilityKeys.ViewDashboard, PosFeatureKeys.LiveOrders),
        new("Customers", "customers.png", "customers", PosCapabilityKeys.ViewCustomers, PosFeatureKeys.Customers),
        new("Reservations", "reservation.png", "reservation", PosCapabilityKeys.OpenTables, PosFeatureKeys.Reservations),
        new("Gift Cards", "giftcards.png", "giftcards", PosCapabilityKeys.TakePayments, PosFeatureKeys.GiftCards),
        new("Loyalty", "loyalty.png", "loyalty", PosCapabilityKeys.ViewCustomers, PosFeatureKeys.CustomerPoints),
        new("Reports", "report.png", "report", PosCapabilityKeys.ViewReports),
        new("Menu Admin", "foodmenu.png", "foodmenu", PosCapabilityKeys.EditMenu),
        new("Printers", "printers.png", "printersetup", PosCapabilityKeys.ConfigurePrinters),
        new("Settings", "settings.png", "settings", PosCapabilityKeys.AccessSettings)
    ];

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
