using OrderWeb.Contracts.Compatibility;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Features;
using OrderWeb.Contracts.Navigation;
using OrderWeb.Contracts.Permissions;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Synchronization;

namespace OrderWeb.Contracts.Services;

public interface IAuthenticationService
{
    Task<OperationResult<UserSession>> LoginAsync(AuthenticationRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult> LogoutAsync(CancellationToken cancellationToken = default);
}

public interface ISessionService
{
    UserSession? CurrentSession { get; }
    Task<OperationResult<UserSession>> RefreshAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> RevokeAsync(string sessionId, CancellationToken cancellationToken = default);
}

public interface INavigationService
{
    IReadOnlyList<NavigationItemContract> GetAvailableItems();
    Task<OperationResult> NavigateAsync(string route, CancellationToken cancellationToken = default);
}

public interface IPermissionService
{
    Task<OperationResult<IReadOnlyList<PermissionGrant>>> GetPermissionsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<bool>> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default);
}

public interface IFeatureService
{
    Task<OperationResult<IReadOnlyList<PosFeature>>> GetFeaturesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<bool>> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default);
}

public interface IConnectionService
{
    ConnectionStatusDto CurrentStatus { get; }
    event Action<ConnectionStatusDto>? StatusChanged;
    Task<OperationResult> ReconnectAsync(CancellationToken cancellationToken = default);
}

public interface IConfigurationService
{
    Task<OperationResult<ConfigurationSectionDto>> GetSectionAsync(string section, CancellationToken cancellationToken = default);
}

public interface IMenuService
{
    Task<OperationResult<MenuSnapshotDto>> GetMenuAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<MenuSnapshotDto>> RefreshMenuAsync(CancellationToken cancellationToken = default);
}

public interface IFloorService
{
    Task<OperationResult<FloorSnapshotDto>> GetFloorsAsync(CancellationToken cancellationToken = default);
}

public interface ITableService
{
    Task<OperationResult<TableSnapshotDto>> GetTablesAsync(string floorId, CancellationToken cancellationToken = default);
    Task<OperationResult<RestaurantTableDto>> OpenTableAsync(string tableId, int guestCount, CancellationToken cancellationToken = default);
}

public interface IOrderService
{
    Task<OperationResult<IReadOnlyList<OrderDto>>> GetOpenOrdersAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<OrderDto>> SubmitAsync(SubmitOrderRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult<OrderDto>> GetAsync(string orderId, CancellationToken cancellationToken = default);
}

public interface ICustomerService
{
    Task<OperationResult<IReadOnlyList<CustomerDto>>> SearchAsync(CustomerSearchRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult<CustomerDto>> GetAsync(string customerId, CancellationToken cancellationToken = default);
}

public interface IPaymentService
{
    Task<OperationResult<PaymentResultDto>> TakePaymentAsync(PaymentRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult<PaymentResultDto>> GetResultAsync(string paymentId, CancellationToken cancellationToken = default);
}

public interface IPrintService
{
    Task<OperationResult<PrintResultDto>> PrintAsync(PrintRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult<PrintResultDto>> GetStatusAsync(string printJobId, CancellationToken cancellationToken = default);
}

public interface ISyncService
{
    Task<OperationResult<SyncVersionSet>> GetVersionsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<SyncVersionSet>> SynchronizeAsync(SyncVersionSet localVersions, CancellationToken cancellationToken = default);
}

public interface IDialogService
{
    Task<OperationResult<DialogResultDto>> ShowAsync(DialogRequest request, CancellationToken cancellationToken = default);
}

public interface ITerminalService
{
    Task<OperationResult<TerminalIdentityDto>> GetIdentityAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<ClientCompatibilityResult>> CheckCompatibilityAsync(ClientCompatibilityRequest request, CancellationToken cancellationToken = default);
}
