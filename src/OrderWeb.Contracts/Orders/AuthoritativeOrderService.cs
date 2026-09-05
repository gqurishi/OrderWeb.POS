using System.Collections.Concurrent;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;

namespace OrderWeb.Contracts.Orders;

/// <summary>
/// Mother-side authoritative order processor (Phase 12).
/// Child may show a temporary display total; Mother revalidates product, price,
/// tax, availability, discount, permission, table, quantity, and final total.
/// Duplicate RequestId returns the cached prior result. Revision mismatches conflict.
/// </summary>
public sealed class AuthoritativeOrderService : IOrderService, IMenuCatalogService
{
    public const string ManagerApprovalCode = "MGR-APPROVE";
    private const decimal TaxRate = 0.20m;

    private readonly OrderMutationGuard _guard = new();
    private readonly ConcurrentDictionary<string, MutableOrder> _orders = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MenuCategoryDto> _categories = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MenuProductDto> _products = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _tables = new(StringComparer.Ordinal);
    private readonly HashSet<string> _privilegedSessions = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public AuthoritativeOrderService()
    {
        SeedDemoCatalog();
    }

    public void SeedCatalog(
        IEnumerable<MenuCategoryDto> categories,
        IEnumerable<MenuProductDto> products,
        IEnumerable<string>? availableTableIds = null)
    {
        foreach (var category in categories)
            _categories[category.Id] = category;
        foreach (var product in products)
            _products[product.Id] = product;
        if (availableTableIds is null)
            return;
        foreach (var tableId in availableTableIds)
            _tables[tableId] = true;
    }

    public void GrantPrivilegedSession(string sessionId) => _privilegedSessions.Add(sessionId);

    public Task<OperationResult<IReadOnlyList<MenuCategoryDto>>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MenuCategoryDto> list = _categories.Values
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToList();
        return Task.FromResult(OperationResult<IReadOnlyList<MenuCategoryDto>>.Ok(list));
    }

    public Task<OperationResult<IReadOnlyList<MenuProductDto>>> GetProductsAsync(string categoryId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MenuProductDto> list = _products.Values
            .Where(p => p.CategoryId == categoryId)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Name)
            .ToList();
        return Task.FromResult(OperationResult<IReadOnlyList<MenuProductDto>>.Ok(list));
    }

    public Task<OperationResult<IReadOnlyList<ProductModifierGroupDto>>> GetModifierGroupsAsync(string productId, CancellationToken cancellationToken = default)
    {
        if (!_products.TryGetValue(productId, out var product))
            return Task.FromResult(OperationResult<IReadOnlyList<ProductModifierGroupDto>>.Fail(OperationError.NotFound("Product not found.")));

        IReadOnlyList<ProductModifierGroupDto> groups = product.ModifierGroups ?? Array.Empty<ProductModifierGroupDto>();
        return Task.FromResult(OperationResult<IReadOnlyList<ProductModifierGroupDto>>.Ok(groups));
    }

    public Task<OperationResult<OrderDto>> GetAsync(string orderId, CancellationToken cancellationToken = default)
    {
        if (!_orders.TryGetValue(orderId, out var order))
            return Task.FromResult(OperationResult<OrderDto>.Fail(OperationError.NotFound("Order not found.")));

        var dto = order.ToDto(TaxRate);
        _guard.Track(dto.Id, dto.Revision);
        return Task.FromResult(OperationResult<OrderDto>.Ok(dto));
    }

    public Task<OperationResult<OrderDto>> OpenOrCreateAsync(OpenOrderRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Mutate(request.Context, () =>
        {
            var tableId = request.TableId?.Trim();
            if (!string.IsNullOrWhiteSpace(tableId) && !_tables.ContainsKey(tableId))
                return OperationResult<OrderDto>.Fail(OperationError.Validation("Table is not available.", tableId));

            if (!string.IsNullOrWhiteSpace(tableId))
            {
                var existing = _orders.Values.FirstOrDefault(o =>
                    string.Equals(o.TableId, tableId, StringComparison.Ordinal) &&
                    !IsClosed(o.Status));
                if (existing is not null)
                    return OperationResult<OrderDto>.Ok(existing.ToDto(TaxRate));
            }

            var order = new MutableOrder
            {
                Id = Guid.NewGuid().ToString("N"),
                TableId = tableId,
                TableName = string.IsNullOrWhiteSpace(tableId)
                    ? request.OrderType
                    : $"Table {tableId}",
                GuestCount = Math.Max(1, request.GuestCount),
                Status = "Open",
                OrderType = string.IsNullOrWhiteSpace(request.OrderType) ? "Table" : request.OrderType,
                Revision = 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                ServerName = request.Context.SessionId
            };
            _orders[order.Id] = order;
            return OperationResult<OrderDto>.Ok(order.ToDto(TaxRate));
        }));

    public Task<OperationResult<OrderDto>> AddLineAsync(AddOrderLineRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Mutate(request.Context, () =>
        {
            if (!TryGetOrder(request.Context, out var order, out var error))
                return OperationResult<OrderDto>.Fail(error!);

            var validation = RevalidateProduct(request.ProductId, request.Quantity, request.SelectedModifierIds);
            if (validation.Error is not null)
                return OperationResult<OrderDto>.Fail(validation.Error);

            var value = validation.Value!;
            order.Lines.Add(new MutableLine
            {
                Id = Guid.NewGuid().ToString("N"),
                ProductId = value.Product.Id,
                ProductName = value.Product.Name,
                Quantity = request.Quantity,
                UnitPrice = value.UnitPrice,
                Notes = request.Notes,
                Modifiers = value.Modifiers.ToList()
            });
            Bump(order);
            return OperationResult<OrderDto>.Ok(order.ToDto(TaxRate));
        }));

    public Task<OperationResult<OrderDto>> UpdateLineQuantityAsync(UpdateOrderLineQuantityRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Mutate(request.Context, () =>
        {
            if (!TryGetOrder(request.Context, out var order, out var error))
                return OperationResult<OrderDto>.Fail(error!);

            if (request.Quantity <= 0)
                return OperationResult<OrderDto>.Fail(OperationError.Validation("Quantity must be greater than zero."));

            var line = order.Lines.FirstOrDefault(l => l.Id == request.LineId && !l.IsVoided);
            if (line is null)
                return OperationResult<OrderDto>.Fail(OperationError.NotFound("Order line not found."));

            var modifierIds = line.Modifiers.Select(m => m.Id).ToList();
            var validation = RevalidateProduct(line.ProductId, request.Quantity, modifierIds);
            if (validation.Error is not null)
                return OperationResult<OrderDto>.Fail(validation.Error);

            line.Quantity = request.Quantity;
            line.UnitPrice = validation.Value!.UnitPrice;
            line.ProductName = validation.Value.Product.Name;
            Bump(order);
            return OperationResult<OrderDto>.Ok(order.ToDto(TaxRate));
        }));

    public Task<OperationResult<OrderDto>> UpdateLineNotesAsync(UpdateOrderLineNotesRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Mutate(request.Context, () =>
        {
            if (!TryGetOrder(request.Context, out var order, out var error))
                return OperationResult<OrderDto>.Fail(error!);

            var line = order.Lines.FirstOrDefault(l => l.Id == request.LineId && !l.IsVoided);
            if (line is null)
                return OperationResult<OrderDto>.Fail(OperationError.NotFound("Order line not found."));

            line.Notes = request.Notes;
            Bump(order);
            return OperationResult<OrderDto>.Ok(order.ToDto(TaxRate));
        }));

    public Task<OperationResult<OrderDto>> VoidLineAsync(VoidOrderLineRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Mutate(request.Context, () =>
        {
            if (!TryGetOrder(request.Context, out var order, out var error))
                return OperationResult<OrderDto>.Fail(error!);

            var line = order.Lines.FirstOrDefault(l => l.Id == request.LineId && !l.IsVoided);
            if (line is null)
                return OperationResult<OrderDto>.Fail(OperationError.NotFound("Order line not found."));

            var allowed = _privilegedSessions.Contains(request.Context.SessionId) ||
                          string.Equals(request.ManagerApprovalCode, ManagerApprovalCode, StringComparison.Ordinal);
            if (!allowed)
            {
                return OperationResult<OrderDto>.Fail(OperationError.PermissionDenied(
                    "Manager approval required to void item.",
                    "void_line"));
            }

            line.IsVoided = true;
            line.VoidReason = request.Reason;
            Bump(order);
            return OperationResult<OrderDto>.Ok(order.ToDto(TaxRate));
        }));

    public Task<OperationResult<OrderDto>> ApplyDiscountAsync(ApplyDiscountRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Mutate(request.Context, () =>
        {
            if (!TryGetOrder(request.Context, out var order, out var error))
                return OperationResult<OrderDto>.Fail(error!);

            if (request.Amount < 0)
                return OperationResult<OrderDto>.Fail(OperationError.Validation("Discount cannot be negative."));

            var needsApproval = request.Amount > 0;
            var approved = string.Equals(request.ManagerApprovalCode, ManagerApprovalCode, StringComparison.Ordinal);
            if (needsApproval && !approved && !_privilegedSessions.Contains(request.Context.SessionId))
            {
                return OperationResult<OrderDto>.Fail(OperationError.PermissionDenied(
                    "Manager approval required to apply discount.",
                    "apply_discount"));
            }

            var subtotal = order.ActiveLines.Sum(l => l.UnitPrice * l.Quantity);
            var discount = request.IsPercent
                ? Math.Round(subtotal * (request.Amount / 100m), 2, MidpointRounding.AwayFromZero)
                : Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero);

            if (discount > subtotal)
                return OperationResult<OrderDto>.Fail(OperationError.Validation("Discount exceeds subtotal."));

            order.DiscountTotal = discount;
            order.DiscountReason = request.Reason;
            Bump(order);
            return OperationResult<OrderDto>.Ok(order.ToDto(TaxRate));
        }));

    public Task<OperationResult<OrderDto>> SendAsync(SendOrderRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(Mutate(request.Context, () =>
        {
            if (!TryGetOrder(request.Context, out var order, out var error))
                return OperationResult<OrderDto>.Fail(error!);

            if (!order.ActiveLines.Any())
                return OperationResult<OrderDto>.Fail(OperationError.Validation("Cannot send an empty order."));

            foreach (var line in order.ActiveLines)
            {
                var check = RevalidateProduct(line.ProductId, line.Quantity, line.Modifiers.Select(m => m.Id).ToList());
                if (check.Error is not null)
                    return OperationResult<OrderDto>.Fail(check.Error);
                line.UnitPrice = check.Value!.UnitPrice;
                line.ProductName = check.Value.Product.Name;
            }

            order.Status = "Sent";
            Bump(order);
            return OperationResult<OrderDto>.Ok(order.ToDto(TaxRate));
        }));

    private OperationResult<OrderDto> Mutate(OrderMutationContext context, Func<OperationResult<OrderDto>> action)
    {
        if (string.IsNullOrWhiteSpace(context.RequestId) ||
            string.IsNullOrWhiteSpace(context.TerminalId) ||
            string.IsNullOrWhiteSpace(context.SessionId))
        {
            return OperationResult<OrderDto>.Fail(OperationError.Validation(
                "RequestId, TerminalId, and SessionId are required on every modifying request."));
        }

        lock (_gate)
        {
            if (_guard.TryGetCachedResult(context, out var cached) && cached is not null)
                return cached;

            var conflict = _guard.ValidateRevision(context);
            if (conflict is not null)
                return _guard.Fail(context, conflict.Error!);

            try
            {
                var result = action();
                if (!result.IsSuccess || result.Value is null)
                    return _guard.Fail(context, result.Error ?? OperationError.Failure("Order mutation failed."));

                return _guard.Complete(context, result.Value);
            }
            catch (Exception ex)
            {
                return _guard.Fail(context, OperationError.Failure(ex.Message));
            }
        }
    }

    private bool TryGetOrder(OrderMutationContext context, out MutableOrder order, out OperationError? error)
    {
        order = null!;
        error = null;
        if (string.IsNullOrWhiteSpace(context.OrderId))
        {
            error = OperationError.Validation("OrderId is required.");
            return false;
        }

        if (!_orders.TryGetValue(context.OrderId, out order!))
        {
            error = OperationError.NotFound("Order not found.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(order.TableId) && !_tables.ContainsKey(order.TableId))
        {
            error = OperationError.Validation("Table is no longer available.", order.TableId);
            return false;
        }

        return true;
    }

    private ProductCheck RevalidateProduct(string productId, int quantity, IReadOnlyList<string> selectedModifierIds)
    {
        if (quantity <= 0)
            return ProductCheck.Fail(OperationError.Validation("Quantity must be greater than zero."));

        if (!_products.TryGetValue(productId, out var product))
            return ProductCheck.Fail(OperationError.NotFound("Product not found."));

        if (!product.IsAvailable)
            return ProductCheck.Fail(OperationError.Validation("Product is not available.", product.Name));

        var groups = product.ModifierGroups ?? Array.Empty<ProductModifierGroupDto>();
        var selected = new List<OrderLineModifierDto>();
        foreach (var group in groups)
        {
            var chosen = group.Options.Where(o => selectedModifierIds.Contains(o.Id, StringComparer.Ordinal)).ToList();
            if (chosen.Count < group.MinSelect || chosen.Count > group.MaxSelect)
            {
                return ProductCheck.Fail(OperationError.Validation(
                    $"Modifier group '{group.Name}' requires {group.MinSelect}-{group.MaxSelect} selections."));
            }

            selected.AddRange(chosen);
        }

        var unknown = selectedModifierIds.Except(selected.Select(s => s.Id), StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
            return ProductCheck.Fail(OperationError.Validation("Unknown modifier selection.", string.Join(",", unknown)));

        var unit = product.Price + selected.Sum(m => m.PriceDelta);
        return ProductCheck.Ok(product, unit, selected);
    }

    private static void Bump(MutableOrder order)
    {
        order.Revision += 1;
        order.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static bool IsClosed(string status) =>
        status.Equals("Paid", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("Voided", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("Closed", StringComparison.OrdinalIgnoreCase);

    private void SeedDemoCatalog()
    {
        _tables["1"] = true;
        _tables["2"] = true;
        _tables["12"] = true;

        _categories["cat-mains"] = new MenuCategoryDto("cat-mains", "Mains", 1);
        _categories["cat-drinks"] = new MenuCategoryDto("cat-drinks", "Drinks", 2);

        var steakMods = new ProductModifierGroupDto(
            "mod-steak",
            "Cooking",
            1,
            1,
            new[]
            {
                new OrderLineModifierDto("mod-rare", "Rare", 0m),
                new OrderLineModifierDto("mod-medium", "Medium", 0m),
                new OrderLineModifierDto("mod-well", "Well Done", 0m)
            });

        _products["prod-steak"] = new MenuProductDto("prod-steak", "cat-mains", "Ribeye Steak", 24.50m, true, 1, null, new[] { steakMods });
        _products["prod-burger"] = new MenuProductDto("prod-burger", "cat-mains", "House Burger", 14.00m, true, 2);
        _products["prod-cola"] = new MenuProductDto("prod-cola", "cat-drinks", "Cola", 2.50m, true, 1);
        _products["prod-unavailable"] = new MenuProductDto("prod-unavailable", "cat-mains", "Sold Out Special", 9.00m, false, 99);
    }

    private readonly record struct ProductCheck(MenuProductDto? Product, decimal UnitPrice, IReadOnlyList<OrderLineModifierDto> Modifiers, OperationError? Error)
    {
        public ProductValidation? Value => Error is null && Product is not null
            ? new ProductValidation(Product, UnitPrice, Modifiers)
            : null;

        public static ProductCheck Ok(MenuProductDto product, decimal unitPrice, IReadOnlyList<OrderLineModifierDto> modifiers) =>
            new(product, unitPrice, modifiers, null);

        public static ProductCheck Fail(OperationError error) =>
            new(null, 0, Array.Empty<OrderLineModifierDto>(), error);
    }

    private sealed record ProductValidation(MenuProductDto Product, decimal UnitPrice, IReadOnlyList<OrderLineModifierDto> Modifiers);

    private sealed class MutableOrder
    {
        public string Id { get; set; } = "";
        public string? TableId { get; set; }
        public string? TableName { get; set; }
        public int GuestCount { get; set; }
        public string Status { get; set; } = "Open";
        public string OrderType { get; set; } = "Table";
        public long Revision { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public string? ServerName { get; set; }
        public decimal DiscountTotal { get; set; }
        public string? DiscountReason { get; set; }
        public List<MutableLine> Lines { get; } = new();
        public IEnumerable<MutableLine> ActiveLines => Lines.Where(l => !l.IsVoided);

        public OrderDto ToDto(decimal taxRate)
        {
            var lineDtos = Lines.Select(l => new OrderLineDto(
                l.Id,
                l.ProductId,
                l.ProductName,
                l.Quantity,
                l.UnitPrice,
                Math.Round(l.UnitPrice * l.Quantity, 2, MidpointRounding.AwayFromZero),
                l.Notes,
                l.Modifiers,
                l.IsVoided)).ToList();

            var subtotal = Math.Round(ActiveLines.Sum(l => l.UnitPrice * l.Quantity), 2, MidpointRounding.AwayFromZero);
            var discount = Math.Min(DiscountTotal, subtotal);
            var taxable = Math.Max(0, subtotal - discount);
            var tax = Math.Round(taxable * taxRate, 2, MidpointRounding.AwayFromZero);
            var grand = taxable + tax;

            return new OrderDto(
                Id,
                TableId,
                TableName,
                GuestCount,
                Status,
                Revision,
                lineDtos,
                new OrderTotalsDto(subtotal, discount, tax, 0m, grand, IsDisplayEstimate: false),
                UpdatedAtUtc,
                ServerName);
        }
    }

    private sealed class MutableLine
    {
        public string Id { get; set; } = "";
        public string ProductId { get; set; } = "";
        public string ProductName { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public string? Notes { get; set; }
        public List<OrderLineModifierDto> Modifiers { get; set; } = new();
        public bool IsVoided { get; set; }
        public string? VoidReason { get; set; }
    }
}
