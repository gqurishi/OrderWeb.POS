using OrderWeb.Contracts.Orders;
using OrderWeb.Contracts.Results;

namespace OrderWeb.Contracts.Services;

/// <summary>
/// Authoritative order mutations. Mother revalidates product, price, tax,
/// availability, discount, permission, table, quantity, and final total.
/// </summary>
public interface IOrderService
{
    Task<OperationResult<OrderDto>> OpenOrCreateAsync(OpenOrderRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult<OrderDto>> GetAsync(string orderId, CancellationToken cancellationToken = default);
    Task<OperationResult<OrderDto>> AddLineAsync(AddOrderLineRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult<OrderDto>> UpdateLineQuantityAsync(UpdateOrderLineQuantityRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult<OrderDto>> UpdateLineNotesAsync(UpdateOrderLineNotesRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult<OrderDto>> VoidLineAsync(VoidOrderLineRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult<OrderDto>> ApplyDiscountAsync(ApplyDiscountRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult<OrderDto>> SendAsync(SendOrderRequest request, CancellationToken cancellationToken = default);
}

public interface IMenuCatalogService
{
    Task<OperationResult<IReadOnlyList<MenuCategoryDto>>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<IReadOnlyList<MenuProductDto>>> GetProductsAsync(string categoryId, CancellationToken cancellationToken = default);
    Task<OperationResult<IReadOnlyList<ProductModifierGroupDto>>> GetModifierGroupsAsync(string productId, CancellationToken cancellationToken = default);
}
