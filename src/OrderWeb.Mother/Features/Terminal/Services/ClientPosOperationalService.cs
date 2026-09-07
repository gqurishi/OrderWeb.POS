using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Services;
using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Mother-side operational APIs for Client POS: food menu, delivery zones,
/// and table/collection/delivery orders. Reuses the same services Mother's own till uses.
/// </summary>
public sealed class ClientPosOperationalService
{
    private const string LiveOrderSourceFilter = @"
        (
            LOWER(COALESCE(NULLIF(o.source_channel, ''), 'local')) = 'local'
            OR (
                LOWER(COALESCE(o.source_channel, '')) = 'web'
                AND LOWER(COALESCE(o.order_type, '')) IN ('pickup', 'collection', 'col', 'takeaway', 'delivery', 'del')
                AND REPLACE(REPLACE(REPLACE(LOWER(COALESCE(o.payment_method, 'cash')), ' ', ''), '_', ''), '-', '')
                    IN ('cash', 'cod', 'cashondelivery', 'cashoncollection')
            )
        )";

    private const string ActiveLifecycleFilter = @"
        AND COALESCE(o.is_open, 1) = 1
        AND LOWER(COALESCE(NULLIF(o.local_lifecycle_state, ''), 'active')) NOT IN ('paid', 'voided')";

    private readonly DatabaseService _databaseService;
    private readonly ReservationSyncService _reservationSync;
    private readonly OrderService _orderService = new();
    private readonly MenuCategoryService _categoryService = new();
    private readonly MenuItemService _menuItemService = new();

    public ClientPosOperationalService(DatabaseService databaseService, ReservationSyncService? reservationSync = null)
    {
        _databaseService = databaseService;
        _reservationSync = reservationSync
            ?? ServiceHelper.GetService<ReservationSyncService>()
            ?? new ReservationSyncService(databaseService);
    }

    public async Task<ClientMenuSnapshot> BuildMenuSnapshotAsync(string version)
    {
        var categories = (await _categoryService.GetAllCategoriesAsync())
            .Where(category => category.Active)
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.Name)
            .ToList();
        var items = await _menuItemService.GetAllItemsAsync();
        var categoryIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var snapshotCategories = new List<ClientMenuCategoryDto>();
        var categoryId = 0;
        foreach (var category in categories)
        {
            categoryId++;
            categoryIds[category.Id] = categoryId;
            snapshotCategories.Add(new ClientMenuCategoryDto(
                categoryId,
                category.Id,
                category.Name,
                string.IsNullOrWhiteSpace(category.Color) ? "#3B82F6" : category.Color,
                category.DisplayOrder,
                true));
        }

        var products = new List<ClientMenuProductDto>();
        var prices = new List<ClientMenuPriceDto>();
        var modifierGroups = new List<ClientMenuModifierGroupDto>();
        var modifiers = new List<ClientMenuModifierDto>();
        var productModifiers = new List<ClientMenuProductModifierDto>();
        var productId = 0;
        var priceId = 0;
        var groupId = 0;
        var modifierId = 0;

        foreach (var item in items
                     .Where(item => !string.IsNullOrWhiteSpace(item.CategoryId) && categoryIds.ContainsKey(item.CategoryId))
                     .OrderBy(item => item.DisplayOrder)
                     .ThenBy(item => item.Name))
        {
            productId++;
            var snapshotCategoryId = categoryIds[item.CategoryId];
            products.Add(new ClientMenuProductDto(
                productId,
                item.Id,
                snapshotCategoryId,
                item.Name,
                item.Description ?? string.Empty,
                null,
                true));

            var takeaway = item.GetEffectivePrice("takeaway");
            var dineIn = item.GetEffectivePrice("table");
            priceId++;
            prices.Add(new ClientMenuPriceDto(priceId, productId, "takeaway", takeaway, "GBP", null));
            if (dineIn != takeaway)
            {
                priceId++;
                prices.Add(new ClientMenuPriceDto(priceId, productId, "dine_in", dineIn, "GBP", null));
            }

            var addons = item.Addons
                .Where(addon => !string.IsNullOrWhiteSpace(addon.Name))
                .ToList();
            if (addons.Count == 0)
            {
                continue;
            }

            groupId++;
            modifierGroups.Add(new ClientMenuModifierGroupDto(
                groupId,
                $"{item.Id}:addons",
                "Add-ons",
                0,
                Math.Max(addons.Count, 1),
                true));
            productModifiers.Add(new ClientMenuProductModifierDto(productId, groupId, 0));
            foreach (var addon in addons)
            {
                modifierId++;
                modifiers.Add(new ClientMenuModifierDto(
                    modifierId,
                    string.IsNullOrWhiteSpace(addon.Id) ? $"{item.Id}:{addon.Name}" : addon.Id,
                    groupId,
                    addon.Name,
                    addon.Price,
                    true));
            }
        }

        return new ClientMenuSnapshot(version, snapshotCategories, products, prices, modifierGroups, modifiers, productModifiers);
    }

    public async Task<ClientDeliveryQuote> QuoteDeliveryZoneAsync(string? postcode)
    {
        var zoneService = new DeliveryZoneService(_databaseService);
        var normalized = DeliveryZoneService.NormalizePostcode(postcode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new ClientDeliveryQuote(string.Empty, false, null, 0m);
        }

        var match = await zoneService.FindZoneForPostcodeAsync(normalized);
        if (match == null)
        {
            await zoneService.SaveUnassignedPostcodeAsync(normalized);
            return new ClientDeliveryQuote(normalized, false, null, 0m);
        }

        return new ClientDeliveryQuote(match.Postcode, true, match.ZoneName, match.DeliveryFee);
    }

    public async Task<IReadOnlyList<ClientAddressSuggestion>> LookupAddressesAsync(string? postcode)
    {
        if (string.IsNullOrWhiteSpace(postcode))
        {
            return Array.Empty<ClientAddressSuggestion>();
        }

        try
        {
            var lookup = new PostcodeLookupService(_databaseService);
            var results = await lookup.LookupPostcodeAsync(postcode.Trim());
            return results
                .Select(address => new ClientAddressSuggestion(
                    address.DisplayText,
                    address.AddressLine1,
                    address.AddressLine2,
                    address.AddressLine3,
                    address.City,
                    address.County,
                    address.Postcode,
                    string.IsNullOrWhiteSpace(address.Country) ? "United Kingdom" : address.Country))
                .ToList();
        }
        catch
        {
            return Array.Empty<ClientAddressSuggestion>();
        }
    }

    public async Task<ClientOrderUpsertResult> UpsertOrderAsync(ClientOrderUpsertRequest request)
    {
        var orderType = NormalizeSavedOrderType(request.OrderType);
        if (orderType == "table" && (!request.TableId.HasValue || string.IsNullOrWhiteSpace(request.TableNumber)))
        {
            return ClientOrderUpsertResult.Fail(400, "A Mother table id and table number are required for a table order.");
        }

        var customerName = orderType == "table"
            ? $"Table {request.TableNumber!.Trim()}"
            : string.IsNullOrWhiteSpace(request.CustomerName) ? "Customer" : request.CustomerName.Trim();
        var incomingLines = (request.Lines ?? Array.Empty<ClientOrderLineRequest>())
            .Where(line => !IsDeliveryFeeLine(line) && line.Quantity > 0 && !string.IsNullOrWhiteSpace(line.Name))
            .ToList();

        var menuItems = incomingLines.Count == 0
            ? new List<FoodMenuItem>()
            : await _menuItemService.GetAllItemsAsync();
        var menuById = menuItems.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

        int? tableSessionId = null;
        if (orderType == "table")
        {
            var tableSessions = new TableSessionService();
            var activeSession = await tableSessions.GetActiveSessionByTableIdAsync(request.TableId!.Value);
            if (activeSession != null)
            {
                tableSessionId = activeSession.Id;
            }
            else
            {
                var opened = await tableSessions.OpenTableWithSessionAsync(
                    request.TableId.Value,
                    Math.Max(1, request.Guests),
                    "Opened from Client POS");
                if (!opened.success || !opened.sessionId.HasValue)
                {
                    return ClientOrderUpsertResult.Fail(409, opened.message);
                }
                tableSessionId = opened.sessionId.Value;
            }
        }

        var order = new Order
        {
            OrderId = string.IsNullOrWhiteSpace(request.OrderId) ? Guid.NewGuid().ToString("N") : request.OrderId.Trim(),
            CustomerName = customerName,
            CustomerPhone = string.IsNullOrWhiteSpace(request.CustomerPhone) ? null : request.CustomerPhone.Trim(),
            CustomerEmail = string.IsNullOrWhiteSpace(request.CustomerEmail) ? null : request.CustomerEmail.Trim(),
            CustomerAddress = string.IsNullOrWhiteSpace(request.CustomerAddress) ? null : request.CustomerAddress.Trim(),
            OrderType = orderType,
            SourceChannel = "local",
            TableSessionId = tableSessionId,
            DeliveryFee = Math.Max(0m, request.DeliveryFee),
            SpecialInstructions = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            ScheduledTime = ParseScheduledTime(request.ScheduledTime),
            Status = OrderStatus.New,
            LocalLifecycleState = LocalLifecycleState.Draft,
            IsOpen = true,
            CreatedAt = DateTime.Now
        };

        foreach (var line in incomingLines)
        {
            order.Items.Add(BuildOrderItem(line, menuById, orderType));
        }

        var foodSubtotal = order.Items.Sum(item => item.TotalPrice);
        order.SubtotalAmount = foodSubtotal;
        order.TaxAmount = 0m;
        order.TotalAmount = foodSubtotal + order.DeliveryFee;

        var existing = await _orderService.GetOrderByExternalIdAsync(order.OrderId);
        if (existing == null)
        {
            var numbers = new OrderNumberService(_databaseService);
            order.OrderNumber = await numbers.GenerateOrderNumberAsync(orderType switch
            {
                "delivery" => "DELIVERY",
                "table" => "TABLE",
                _ => "COLLECTION"
            });
        }
        else
        {
            order.OrderNumber = existing.OrderNumber;
            order.CreatedAt = existing.CreatedAt;
            order.Status = existing.Status;
            order.LocalLifecycleState = existing.LocalLifecycleState;
            order.IsOpen = existing.IsOpen;
        }

        var saved = await _orderService.SaveOrderAsync(order);
        if (!saved.Success)
        {
            return ClientOrderUpsertResult.Fail(422, saved.Message);
        }

        var persisted = await _orderService.GetOrderByExternalIdAsync(order.OrderId);
        if (persisted == null)
        {
            return ClientOrderUpsertResult.Fail(500, "Mother POS saved the order but could not reload it.");
        }

        if (tableSessionId.HasValue)
        {
            var linked = await new TableSessionService().LinkOrderToSessionAsync(
                tableSessionId.Value,
                persisted.OrderId,
                TableSessionStatus.Ordering,
                "Client POS");
            if (!linked.success)
            {
                return ClientOrderUpsertResult.Fail(409, linked.message);
            }
        }

        var clientOrder = ToClientOrder(persisted);
        if (orderType == "table")
        {
            clientOrder = clientOrder with
            {
                TableId = request.TableId,
                TableNumber = request.TableNumber?.Trim(),
                Guests = Math.Max(1, request.Guests)
            };
        }
        return ClientOrderUpsertResult.Ok(clientOrder);
    }

    public async Task<IReadOnlyList<ClientOperationalOrder>> ListOpenOrdersAsync(string? orderType)
    {
        var ids = await ListOpenOrderIdsAsync(orderType);
        var orders = new List<ClientOperationalOrder>();
        foreach (var id in ids)
        {
            var order = await _orderService.GetOrderByExternalIdAsync(id);
            if (order != null)
            {
                orders.Add(ToClientOrder(order));
            }
        }

        return orders;
    }

    private async Task<IReadOnlyList<string>> ListOpenOrderIdsAsync(string? orderType)
    {
        var normalized = NormalizeClientOrderType(orderType);
        var typeFilter = normalized switch
        {
            "pickup" => "AND LOWER(COALESCE(o.order_type, '')) IN ('pickup', 'collection', 'col', 'takeaway')",
            "delivery" => "AND LOWER(COALESCE(o.order_type, '')) IN ('delivery', 'del')",
            "table" => "AND LOWER(COALESCE(o.order_type, '')) IN ('table', 'tbl', 'dine_in', 'dine-in')",
            _ => "AND LOWER(COALESCE(o.order_type, '')) IN ('pickup', 'collection', 'col', 'takeaway', 'delivery', 'del', 'table', 'tbl', 'dine_in', 'dine-in')"
        };

        var ids = new List<string>();
        await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new MySqlCommand($@"
            SELECT o.order_id
            FROM orders o
            WHERE {LiveOrderSourceFilter}
              {ActiveLifecycleFilter}
              {typeFilter}
            ORDER BY o.updated_at DESC, o.created_at DESC
            LIMIT 200", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.IsDBNull(0) ? null : reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static OrderItem BuildOrderItem(
        ClientOrderLineRequest line,
        IReadOnlyDictionary<string, FoodMenuItem> menuById,
        string orderType)
    {
        menuById.TryGetValue(line.ProductId ?? string.Empty, out var menuItem);
        var selectedNames = (line.Modifiers ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToList();
        var addons = new List<OrderItemAddon>();
        if (menuItem != null)
        {
            foreach (var name in selectedNames)
            {
                var addon = menuItem.Addons.FirstOrDefault(item =>
                    string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                addons.Add(new OrderItemAddon
                {
                    AddonId = addon?.Id,
                    AddonName = addon?.Name ?? name,
                    AddonPrice = addon?.Price ?? 0m,
                    Quantity = 1
                });
            }
        }
        else
        {
            addons.AddRange(selectedNames.Select(name => new OrderItemAddon
            {
                AddonName = name,
                AddonPrice = 0m,
                Quantity = 1
            }));
        }

        var addonTotal = addons.Sum(addon => addon.AddonPrice ?? 0m);
        var basePrice = menuItem?.GetEffectivePrice(orderType) ?? Math.Max(0m, line.UnitPrice - addonTotal);

        return new OrderItem
        {
            ClientItemId = string.IsNullOrWhiteSpace(line.Id) ? Guid.NewGuid().ToString("N") : line.Id.Trim(),
            MenuItemId = menuItem?.Id ?? (IsLikelyMotherMenuId(line.ProductId) ? line.ProductId : null),
            ItemName = menuItem?.Name ?? line.Name.Trim(),
            DisplayName = menuItem?.Name ?? line.Name.Trim(),
            PrintGroupId = menuItem?.PrintGroupId,
            PrintInRed = menuItem?.PrintInRed ?? false,
            Quantity = line.Quantity,
            ItemPrice = basePrice,
            SpecialInstructions = string.IsNullOrWhiteSpace(line.Notes) ? null : line.Notes.Trim(),
            Addons = addons
        };
    }

    public static ClientOperationalOrder ToClientOrder(Order order)
    {
        var displayType = NormalizeClientOrderType(order.OrderType) switch
        {
            "delivery" => "Delivery",
            "table" => "Table",
            _ => "Collection"
        };
        var lines = order.Items
            .Select(item => new ClientOperationalOrderLine(
                string.IsNullOrWhiteSpace(item.ClientItemId) ? item.Id.ToString() : item.ClientItemId,
                item.MenuItemId,
                item.ItemName,
                item.Quantity,
                (item.ItemPrice ?? 0m) + item.Addons.Sum(addon => addon.AddonPrice ?? 0m),
                item.SpecialInstructions,
                item.Addons.Select(addon => addon.AddonName).Where(name => !string.IsNullOrWhiteSpace(name)).ToList()))
            .ToList();

        if (order.DeliveryFee > 0 && lines.All(line => !string.Equals(line.Name, "Delivery Fee", StringComparison.OrdinalIgnoreCase)))
        {
            lines.Add(new ClientOperationalOrderLine(
                "delivery-fee",
                null,
                "Delivery Fee",
                1,
                order.DeliveryFee,
                null,
                Array.Empty<string>()));
        }

        var customer = string.Join(" · ", new[] { order.CustomerName, order.CustomerPhone }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        return new ClientOperationalOrder(
            order.OrderId,
            order.OrderNumber ?? order.OrderId,
            displayType,
            string.IsNullOrWhiteSpace(order.CustomerName) ? order.LocalLifecycleDisplay : order.CustomerName,
            null,
            null,
            0,
            lines,
            order.SubtotalAmount,
            order.TaxAmount,
            order.TotalAmount,
            Math.Max(1, (int)(order.UpdatedAt.Ticks % int.MaxValue)),
            (order.UpdatedAt == default ? DateTime.Now : order.UpdatedAt).ToUniversalTime().ToString("O"),
            customer);
    }

    private static bool IsDeliveryFeeLine(ClientOrderLineRequest line) =>
        string.Equals(line.Id, "delivery-fee", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(line.Name?.Trim(), "Delivery Fee", StringComparison.OrdinalIgnoreCase);

    private static bool IsLikelyMotherMenuId(string? productId) =>
        !string.IsNullOrWhiteSpace(productId) && !int.TryParse(productId, out _);

    private static string NormalizeSavedOrderType(string? orderType)
    {
        return (orderType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "delivery" or "del" => "delivery",
            "table" or "tbl" or "dine_in" or "dine-in" => "table",
            _ => "pickup"
        };
    }

    private static string NormalizeClientOrderType(string? orderType)
    {
        return (orderType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "delivery" or "del" => "delivery",
            "table" or "tbl" or "dine_in" or "dine-in" => "table",
            "pickup" or "collection" or "col" or "takeaway" => "pickup",
            _ => "all"
        };
    }

    private static DateTime? ParseScheduledTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals("ASAP", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return DateTime.TryParse(value, out var parsed) ? parsed : null;
    }

    public async Task<IReadOnlyList<ClientReservationDto>> ListReservationsAsync(DateTime from, DateTime to)
    {
        var start = from.Date <= to.Date ? from.Date : to.Date;
        var end = to.Date >= from.Date ? to.Date : from.Date;
        var reservations = await _reservationSync.GetReservationsAsync(start, end);
        return reservations.Select(ToDto).ToList();
    }

    public async Task<ClientReservationMutationResult> CreateReservationAsync(ClientReservationCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            return ClientReservationMutationResult.Fail(400, "Customer name is required.");
        }

        if (request.Covers <= 0)
        {
            return ClientReservationMutationResult.Fail(400, "Covers must be at least 1.");
        }

        var result = await _reservationSync.CreatePosReservationAsync(new CreatePosReservationRequest
        {
            ReservationDate = request.ReservationDate.Date,
            ReservationTime = request.ReservationTime,
            Covers = request.Covers,
            CustomerName = request.CustomerName.Trim(),
            CustomerPhone = request.CustomerPhone?.Trim() ?? string.Empty,
            CustomerEmail = request.CustomerEmail?.Trim() ?? string.Empty,
            PromoCode = request.PromoCode?.Trim() ?? string.Empty,
            Notes = request.Notes?.Trim() ?? string.Empty,
            Allergies = request.Allergies?.Trim() ?? string.Empty,
            TableNumber = request.TableNumber?.Trim() ?? string.Empty,
            Channel = string.IsNullOrWhiteSpace(request.Channel) ? "pos" : request.Channel.Trim()
        });

        if (result.Reservation == null)
        {
            return ClientReservationMutationResult.Fail(403, result.Message);
        }

        return ClientReservationMutationResult.Ok(result.Message, ToDto(result.Reservation));
    }

    public async Task<ClientReservationMutationResult> UpdateReservationStatusAsync(string? cloudId, string? localId, string? status)
    {
        var normalized = NormalizeAttendanceStatus(status);
        if (normalized == null)
        {
            return ClientReservationMutationResult.Fail(400, "Status must be arrived, no_show, or cancelled.");
        }

        if (string.IsNullOrWhiteSpace(cloudId) && string.IsNullOrWhiteSpace(localId))
        {
            return ClientReservationMutationResult.Fail(400, "Reservation id missing.");
        }

        var result = await _reservationSync.UpdateReservationStatusAsync(cloudId ?? string.Empty, normalized, localId);
        return result.Success
            ? ClientReservationMutationResult.Ok(result.Message)
            : ClientReservationMutationResult.Fail(409, result.Message);
    }

    public async Task<ClientReservationSyncResult> SyncDateAsync(DateTime date)
    {
        var result = await _reservationSync.SyncDateAsync(date.Date, useSince: false, includeCancelled: true);
        var start = date.Date.AddDays(-7);
        var end = date.Date.AddMonths(1).AddDays(7);
        var reservations = await ListReservationsAsync(start, end);
        return new ClientReservationSyncResult(result.Success, result.Message, reservations);
    }

    private static string? NormalizeAttendanceStatus(string? status)
    {
        var value = (status ?? string.Empty).Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
        return value switch
        {
            "arrived" or "show" or "shown" or "seated" => "arrived",
            "no_show" or "noshow" => "no_show",
            "cancelled" or "canceled" or "cancel" => "cancelled",
            _ => null
        };
    }

    private static ClientReservationDto ToDto(CloudReservation reservation)
    {
        return new ClientReservationDto(
            string.IsNullOrWhiteSpace(reservation.CloudId) ? reservation.LocalId ?? reservation.Id.ToString() : reservation.CloudId,
            reservation.LocalId,
            reservation.Reference ?? string.Empty,
            reservation.ReservationDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            reservation.ReservationTime.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture),
            reservation.Covers,
            reservation.CustomerName ?? string.Empty,
            reservation.CustomerPhone ?? string.Empty,
            reservation.CustomerEmail ?? string.Empty,
            reservation.PromoCode ?? string.Empty,
            reservation.Notes ?? string.Empty,
            reservation.Allergies ?? string.Empty,
            string.IsNullOrWhiteSpace(reservation.Status) ? "confirmed" : reservation.Status,
            reservation.Source ?? string.Empty,
            reservation.TableNumber ?? string.Empty,
            reservation.IsPendingUpload,
            reservation.LastUpdatedAt == default
                ? DateTimeOffset.UtcNow.ToString("O")
                : new DateTimeOffset(DateTime.SpecifyKind(reservation.LastUpdatedAt, DateTimeKind.Utc)).ToString("O"));
    }
}

public sealed record ClientMenuSnapshot(
    string Version,
    IReadOnlyList<ClientMenuCategoryDto> Categories,
    IReadOnlyList<ClientMenuProductDto> Products,
    IReadOnlyList<ClientMenuPriceDto> Prices,
    IReadOnlyList<ClientMenuModifierGroupDto> ModifierGroups,
    IReadOnlyList<ClientMenuModifierDto> Modifiers,
    IReadOnlyList<ClientMenuProductModifierDto> ProductModifiers);

public sealed record ClientMenuCategoryDto(int Id, string MotherId, string Name, string Color, int SortOrder, bool IsActive);

public sealed record ClientMenuProductDto(int Id, string MotherId, int CategoryId, string Name, string Description, string? Sku, bool IsActive);

public sealed record ClientMenuPriceDto(int Id, int ProductId, string PriceType, decimal Amount, string Currency, int? TaxRateId);

public sealed record ClientMenuModifierGroupDto(int Id, string MotherId, string Name, int MinSelect, int MaxSelect, bool IsActive);

public sealed record ClientMenuModifierDto(int Id, string MotherId, int ModifierGroupId, string Name, decimal PriceDelta, bool IsActive);

public sealed record ClientMenuProductModifierDto(int ProductId, int ModifierGroupId, int SortOrder);

public sealed record ClientDeliveryQuote(string Postcode, bool IsDeliverable, string? DeliveryZoneName, decimal DeliveryFee);

public sealed record ClientAddressSuggestion(
    string DisplayText,
    string AddressLine1,
    string AddressLine2,
    string AddressLine3,
    string City,
    string County,
    string Postcode,
    string Country);

public sealed record ClientReservationDto(
    string CloudId,
    string? LocalId,
    string Reference,
    string Date,
    string Time,
    int Covers,
    string CustomerName,
    string CustomerPhone,
    string CustomerEmail,
    string PromoCode,
    string Notes,
    string Allergies,
    string Status,
    string Source,
    string TableNumber,
    bool PendingUpload,
    string UpdatedUtc);

public sealed record ClientReservationCreateRequest(
    DateTime ReservationDate,
    TimeSpan ReservationTime,
    int Covers,
    string? CustomerName,
    string? CustomerPhone,
    string? CustomerEmail,
    string? PromoCode,
    string? Notes,
    string? Allergies,
    string? TableNumber,
    string? Channel);

public sealed record ClientReservationMutationResult(bool Success, int StatusCode, string Message, ClientReservationDto? Reservation)
{
    public static ClientReservationMutationResult Ok(string message, ClientReservationDto? reservation = null) =>
        new(true, 200, message, reservation);

    public static ClientReservationMutationResult Fail(int statusCode, string message) =>
        new(false, statusCode, message, null);
}

public sealed record ClientReservationSyncResult(
    bool Success,
    string Message,
    IReadOnlyList<ClientReservationDto> Reservations);

public sealed record ClientOrderUpsertRequest(
    string? OrderId,
    string? OrderType,
    string? CustomerName,
    string? CustomerPhone,
    string? CustomerEmail,
    string? CustomerAddress,
    decimal DeliveryFee,
    string? Notes,
    string? ScheduledTime,
    int? TableId,
    string? TableNumber,
    int Guests,
    IReadOnlyList<ClientOrderLineRequest>? Lines);

public sealed record ClientOrderLineRequest(
    string? Id,
    string? ProductId,
    string Name,
    int Quantity,
    decimal UnitPrice,
    string? Notes,
    IReadOnlyList<string>? Modifiers);

public sealed record ClientOperationalOrder(
    string Id,
    string OrderNumber,
    string OrderType,
    string Status,
    int? TableId,
    string? TableNumber,
    int Guests,
    IReadOnlyList<ClientOperationalOrderLine> Lines,
    decimal Subtotal,
    decimal Tax,
    decimal Total,
    int Version,
    string UpdatedUtc,
    string? ConflictMessage);

public sealed record ClientOperationalOrderLine(
    string Id,
    string? ProductMotherId,
    string Name,
    int Quantity,
    decimal UnitPrice,
    string? Notes,
    IReadOnlyList<string> Modifiers);

public sealed record ClientOrderUpsertResult(bool Success, int StatusCode, string Message, ClientOperationalOrder? Order)
{
    public static ClientOrderUpsertResult Ok(ClientOperationalOrder order) =>
        new(true, 200, "Order saved on Mother POS.", order);

    public static ClientOrderUpsertResult Fail(int statusCode, string message) =>
        new(false, statusCode, message, null);
}
