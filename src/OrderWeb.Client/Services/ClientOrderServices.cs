using OrderWeb.Client.Models;
using OrderWeb.Contracts.Orders;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;

namespace OrderWeb.Client.Services;

/// <summary>
/// Client menu catalog over local SQLite cache. Seeds Mother so product IDs can be revalidated.
/// </summary>
public sealed class ClientMenuCatalogService : IMenuCatalogService
{
    private readonly ClientCacheService _cache;
    private readonly AuthoritativeOrderService? _authoritative;

    public ClientMenuCatalogService(ClientCacheService cache, AuthoritativeOrderService? authoritative = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _authoritative = authoritative;
    }

    public async Task<OperationResult<IReadOnlyList<MenuCategoryDto>>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var categories = await _cache.GetMenuCategoriesAsync().ConfigureAwait(false);
        IReadOnlyList<MenuCategoryDto> mapped = categories
            .OrderBy(c => c.SortOrder)
            .Select(c => new MenuCategoryDto(c.Id.ToString(), c.Name, c.SortOrder))
            .ToList();
        _authoritative?.SeedCatalog(mapped, Array.Empty<MenuProductDto>());
        return OperationResult<IReadOnlyList<MenuCategoryDto>>.Ok(mapped);
    }

    public async Task<OperationResult<IReadOnlyList<MenuProductDto>>> GetProductsAsync(string categoryId, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(categoryId, out var id))
            return OperationResult<IReadOnlyList<MenuProductDto>>.Fail(OperationError.Validation("Invalid category id."));

        var products = await _cache.GetProductsByCategoryAsync(id).ConfigureAwait(false);
        IReadOnlyList<MenuProductDto> mapped = products.Select(MapProduct).ToList();
        _authoritative?.SeedCatalog(Array.Empty<MenuCategoryDto>(), mapped);
        return OperationResult<IReadOnlyList<MenuProductDto>>.Ok(mapped);
    }

    public async Task<OperationResult<IReadOnlyList<ProductModifierGroupDto>>> GetModifierGroupsAsync(string productId, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(productId, out var id))
            return OperationResult<IReadOnlyList<ProductModifierGroupDto>>.Fail(OperationError.Validation("Invalid product id."));

        foreach (var category in await _cache.GetMenuCategoriesAsync().ConfigureAwait(false))
        {
            var products = await _cache.GetProductsByCategoryAsync(category.Id).ConfigureAwait(false);
            var product = products.FirstOrDefault(p => p.Id == id);
            if (product is null)
                continue;

            var mapped = MapProduct(product);
            _authoritative?.SeedCatalog(Array.Empty<MenuCategoryDto>(), new[] { mapped });
            IReadOnlyList<ProductModifierGroupDto> groups = mapped.ModifierGroups ?? Array.Empty<ProductModifierGroupDto>();
            return OperationResult<IReadOnlyList<ProductModifierGroupDto>>.Ok(groups);
        }

        return OperationResult<IReadOnlyList<ProductModifierGroupDto>>.Fail(OperationError.NotFound("Product not found in cache."));
    }

    private static MenuProductDto MapProduct(CachedProduct product)
    {
        var groups = product.ModifierGroups.Select(g => new ProductModifierGroupDto(
            g.Id.ToString(),
            g.Name,
            g.MinSelect,
            g.MaxSelect,
            g.Modifiers.Select(m => new OrderLineModifierDto(m.Id.ToString(), m.Name, m.PriceDelta)).ToList())).ToList();

        return new MenuProductDto(
            product.Id.ToString(),
            product.CategoryId.ToString(),
            product.Name,
            product.Price,
            IsAvailable: true,
            SortOrder: 0,
            Colour: null,
            ModifierGroups: groups);
    }
}

/// <summary>
/// Child order facade. Mutations are stamped and forwarded to Mother for authoritative revalidation.
/// Duplicate RequestId returns Mother's cached prior result; revision mismatch conflicts.
/// </summary>
public sealed class ClientOrderService : IOrderService
{
    private readonly AuthoritativeOrderService _mother;

    public ClientOrderService(AuthoritativeOrderService motherAuthoritative)
        => _mother = motherAuthoritative ?? throw new ArgumentNullException(nameof(motherAuthoritative));

    public AuthoritativeOrderService Mother => _mother;

    public void EnsureTable(string tableId) =>
        _mother.SeedCatalog(Array.Empty<MenuCategoryDto>(), Array.Empty<MenuProductDto>(), new[] { tableId });

    public Task<OperationResult<OrderDto>> OpenOrCreateAsync(OpenOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.TableId))
            EnsureTable(request.TableId!);
        return _mother.OpenOrCreateAsync(request, cancellationToken);
    }

    public Task<OperationResult<OrderDto>> GetAsync(string orderId, CancellationToken cancellationToken = default)
        => _mother.GetAsync(orderId, cancellationToken);

    public Task<OperationResult<OrderDto>> AddLineAsync(AddOrderLineRequest request, CancellationToken cancellationToken = default)
        => _mother.AddLineAsync(request, cancellationToken);

    public Task<OperationResult<OrderDto>> UpdateLineQuantityAsync(UpdateOrderLineQuantityRequest request, CancellationToken cancellationToken = default)
        => _mother.UpdateLineQuantityAsync(request, cancellationToken);

    public Task<OperationResult<OrderDto>> UpdateLineNotesAsync(UpdateOrderLineNotesRequest request, CancellationToken cancellationToken = default)
        => _mother.UpdateLineNotesAsync(request, cancellationToken);

    public Task<OperationResult<OrderDto>> VoidLineAsync(VoidOrderLineRequest request, CancellationToken cancellationToken = default)
        => _mother.VoidLineAsync(request, cancellationToken);

    public Task<OperationResult<OrderDto>> ApplyDiscountAsync(ApplyDiscountRequest request, CancellationToken cancellationToken = default)
        => _mother.ApplyDiscountAsync(request, cancellationToken);

    public Task<OperationResult<OrderDto>> SendAsync(SendOrderRequest request, CancellationToken cancellationToken = default)
        => _mother.SendAsync(request, cancellationToken);
}

public sealed class ClientOrderSession
{
    public string TerminalId { get; set; } = "client-terminal";
    public string SessionId { get; set; } = "client-session";
    public (string TerminalId, string SessionId) Current() => (TerminalId, SessionId);
}
