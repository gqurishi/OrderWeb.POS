using MyFirstMauiApp.Models.FoodMenu;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>Client Order Place MORE / VOID action APIs (discount, SC, transfer, merge, fire, loyalty, previous).</summary>
public sealed partial class ClientPosOperationalService
{
    private const decimal DiscountManagerPinThreshold = 20m;

    public async Task<ClientOrderUpsertResult> ApplyDiscountAsync(
        string? orderId,
        decimal amount,
        decimal percent,
        string? reason,
        string? discountType,
        string? approvingPin,
        int? sessionUserId,
        string? sessionUserName)
    {
        var existing = await LoadMutableOrderAsync(orderId);
        if (!existing.Success || existing.Order is null)
        {
            return ClientOrderUpsertResult.Fail(existing.StatusCode, existing.Message);
        }

        var order = existing.Order;
        var previous = order.DiscountAmount;
        var subtotal = Math.Max(0m, order.SubtotalAmount);
        var discountAmount = amount;
        var discountPercent = Math.Max(0m, percent);
        var type = string.IsNullOrWhiteSpace(discountType) ? "fixed" : discountType.Trim().ToLowerInvariant();

        if (type is "percent" or "percentage")
        {
            discountPercent = Math.Clamp(discountPercent, 0m, 100m);
            discountAmount = Math.Round(subtotal * (discountPercent / 100m), 2, MidpointRounding.AwayFromZero);
            type = "percent";
        }
        else
        {
            type = "fixed";
            discountPercent = 0m;
        }

        discountAmount = Math.Clamp(discountAmount, 0m, subtotal);
        var approvalRequired = discountAmount > DiscountManagerPinThreshold;
        User? approver = null;
        if (approvalRequired)
        {
            var approval = await ResolveManagerApprovalAsync(
                sessionUserId,
                sessionUserName,
                approvingPin,
                requirePinWhenNotManager: true);
            if (!approval.Success || approval.User is null)
            {
                return ClientOrderUpsertResult.Fail(403, approval.Message);
            }

            approver = approval.User;
        }

        order.DiscountAmount = discountAmount;
        ApplyClientOrderFinancials(order, NormalizeSavedOrderType(order.OrderType));
        order.UpdatedAt = DateTime.Now;

        var save = await _orderService.SaveOrderAsync(order);
        if (!save.Success)
        {
            return ClientOrderUpsertResult.Fail(422, save.Message ?? "Mother POS could not apply the discount.");
        }

        try
        {
            var audit = new DiscountAuditService(_databaseService, AuthenticationService.Instance);
            var action = discountAmount <= 0m
                ? "removed"
                : previous > 0m
                    ? "updated"
                    : "applied";
            await audit.LogAsync(new DiscountAuditRequest
            {
                OrderId = order.OrderId,
                OrderNumber = order.OrderNumber,
                TableSessionId = order.TableSessionId,
                TableNumber = ExtractTableNumber(order),
                Action = action,
                DiscountType = type,
                SubtotalAmount = order.SubtotalAmount,
                PreviousDiscountAmount = previous,
                DiscountAmount = discountAmount,
                DiscountPercent = discountPercent,
                TotalAfterDiscount = order.TotalAmount,
                Reason = action == "removed"
                    ? "Discount removed"
                    : (string.IsNullOrWhiteSpace(reason) ? "Client discount" : reason.Trim()),
                SourceArea = string.IsNullOrWhiteSpace(sessionUserName)
                    ? "client_order_more_options"
                    : $"client_order_more_options:{sessionUserName.Trim()}",
                ApprovalRequired = approvalRequired,
                ApprovedBy = approver == null
                    ? null
                    : new DiscountApprovalInfo
                    {
                        UserId = approver.Id,
                        Name = string.IsNullOrWhiteSpace(approver.Name) ? approver.Username : approver.Name,
                        Role = approver.Role.ToString()
                    }
            });
        }
        catch
        {
            // Discount still applied; audit is best-effort.
        }

        var persisted = await _orderService.GetOrderByExternalIdAsync(order.OrderId);
        return ClientOrderUpsertResult.Ok(
            ToClientOrder(persisted ?? order),
            discountAmount <= 0m
                ? "Discount removed."
                : $"£{discountAmount:F2} discount applied.");
    }

    public async Task<ClientOrderUpsertResult> SetServiceChargeAsync(
        string? orderId,
        string? action,
        string? reason,
        string? approvingPin,
        int? sessionUserId,
        string? sessionUserName)
    {
        var existing = await LoadMutableOrderAsync(orderId);
        if (!existing.Success || existing.Order is null)
        {
            return ClientOrderUpsertResult.Fail(existing.StatusCode, existing.Message);
        }

        var order = existing.Order;
        if (!string.Equals(NormalizeSavedOrderType(order.OrderType), "table", StringComparison.OrdinalIgnoreCase))
        {
            return ClientOrderUpsertResult.Fail(400, "Service charge can only be changed on table orders.");
        }

        var status = (order.ServiceChargeStatus ?? "not_configured").Trim().ToLowerInvariant();
        if (status is "not_configured")
        {
            return ClientOrderUpsertResult.Fail(409, "This order has no service charge configured.");
        }

        var remove = string.Equals(action?.Trim(), "remove", StringComparison.OrdinalIgnoreCase);
        if (remove && status != "applied")
        {
            return ClientOrderUpsertResult.Fail(409, "Service charge is not currently applied.");
        }

        if (!remove && status != "removed")
        {
            return ClientOrderUpsertResult.Fail(409, "Service charge is already applied.");
        }

        var approval = await ResolveManagerApprovalAsync(
            sessionUserId,
            sessionUserName,
            approvingPin,
            requirePinWhenNotManager: true);
        if (!approval.Success || approval.User is null)
        {
            return ClientOrderUpsertResult.Fail(403, approval.Message);
        }

        var performedBy = approval.User;
        var performedName = string.IsNullOrWhiteSpace(performedBy.Name) ? performedBy.Username : performedBy.Name;
        var removalReason = remove
            ? (string.IsNullOrWhiteSpace(reason) ? "Manager discretion" : reason.Trim())
            : "Service charge restored";

        if (remove)
        {
            order.ServiceChargeRemovalReason = removalReason;
            order.ServiceChargeRemovedByUserId = performedBy.Id;
            order.ServiceChargeRemovedByName = performedName;
            order.ServiceChargeApprovedByUserId = performedBy.Id;
            order.ServiceChargeApprovedByName = performedName;
            order.ServiceChargeRemovedAt = DateTime.Now;
            order.ServiceChargeStatus = "removed";
        }
        else
        {
            order.ServiceChargeRemovalReason = null;
            order.ServiceChargeRemovedByUserId = null;
            order.ServiceChargeRemovedByName = null;
            order.ServiceChargeApprovedByUserId = null;
            order.ServiceChargeApprovedByName = null;
            order.ServiceChargeRemovedAt = null;
            order.ServiceChargeStatus = "applied";
        }

        ApplyClientOrderFinancials(order, "table");
        order.UpdatedAt = DateTime.Now;
        var amount = order.ServiceChargeAmount;

        var save = await _orderService.SaveOrderAsync(order);
        if (!save.Success)
        {
            return ClientOrderUpsertResult.Fail(422, save.Message ?? "Mother POS could not update service charge.");
        }

        try
        {
            var audit = new TableServiceChargeOrderAuditService(_databaseService);
            await audit.RecordAsync(
                order.OrderId,
                remove ? "removed" : "restored",
                order.ServiceChargePercentage,
                order.ServiceChargeBasis,
                amount,
                ParseServiceChargeClassification(order.ServiceChargeClassification),
                removalReason,
                performedBy,
                performedBy);
        }
        catch
        {
            // Best-effort audit.
        }

        var persisted = await _orderService.GetOrderByExternalIdAsync(order.OrderId);
        return ClientOrderUpsertResult.Ok(
            ToClientOrder(persisted ?? order),
            remove ? $"Service charge removed (£{amount:F2})." : $"Service charge restored (£{amount:F2}).");
    }

    public async Task<ClientOrderUpsertResult> TransferTableAsync(
        string? orderId,
        int? targetTableId,
        string? actorName)
    {
        var existing = await LoadMutableOrderAsync(orderId);
        if (!existing.Success || existing.Order is null)
        {
            return ClientOrderUpsertResult.Fail(existing.StatusCode, existing.Message);
        }

        var order = existing.Order;
        if (!string.Equals(NormalizeSavedOrderType(order.OrderType), "table", StringComparison.OrdinalIgnoreCase))
        {
            return ClientOrderUpsertResult.Fail(400, "Only table orders can be transferred.");
        }

        if (targetTableId is null or <= 0)
        {
            return ClientOrderUpsertResult.Fail(400, "A target table id is required.");
        }

        if (order.TableSessionId is null or <= 0)
        {
            return ClientOrderUpsertResult.Fail(409, "No active table session to transfer.");
        }

        var sessions = new TableSessionService();
        var tables = await new RestaurantTableService().GetAllTablesAsync();
        var target = tables.FirstOrDefault(table => table.Id == targetTableId.Value);
        if (target == null)
        {
            return ClientOrderUpsertResult.Fail(404, "Target table was not found.");
        }

        var transfer = await sessions.TransferSessionAsync(
            order.TableSessionId.Value,
            targetTableId.Value,
            string.IsNullOrWhiteSpace(actorName) ? "Client POS" : actorName.Trim(),
            $"Transferred to Table {target.TableNumber}");
        if (!transfer.success)
        {
            return ClientOrderUpsertResult.Fail(422, transfer.message);
        }

        order.CustomerName = $"Table {target.TableNumber}";
        order.UpdatedAt = DateTime.Now;
        await _orderService.SaveOrderAsync(order);

        var persisted = await _orderService.GetOrderByExternalIdAsync(order.OrderId);
        var client = ToClientOrder(persisted ?? order);
        client = client with
        {
            TableId = target.Id,
            TableNumber = target.TableNumber?.Trim()
        };
        return ClientOrderUpsertResult.Ok(client, $"Order transferred to Table {target.TableNumber}.");
    }

    public async Task<ClientOrderUpsertResult> MergeTablesAsync(
        string? orderId,
        string? childTableNumber,
        string? actorName)
    {
        var existing = await LoadMutableOrderAsync(orderId);
        if (!existing.Success || existing.Order is null)
        {
            return ClientOrderUpsertResult.Fail(existing.StatusCode, existing.Message);
        }

        var order = existing.Order;
        if (!string.Equals(NormalizeSavedOrderType(order.OrderType), "table", StringComparison.OrdinalIgnoreCase))
        {
            return ClientOrderUpsertResult.Fail(400, "Only table orders can be merged.");
        }

        if (order.TableSessionId is null or <= 0)
        {
            return ClientOrderUpsertResult.Fail(409, "No active session is available for merging.");
        }

        if (string.IsNullOrWhiteSpace(childTableNumber))
        {
            return ClientOrderUpsertResult.Fail(400, "Enter the child table number to merge.");
        }

        var sessions = new TableSessionService();
        var tables = await sessions.GetTablesWithSessionsAsync();
        var child = tables.FirstOrDefault(table =>
            string.Equals(table.TableNumber?.Trim(), childTableNumber.Trim(), StringComparison.OrdinalIgnoreCase));
        if (child?.CurrentSession == null)
        {
            return ClientOrderUpsertResult.Fail(404, "The selected table does not have an active session.");
        }

        if (child.CurrentSession.Id == order.TableSessionId.Value)
        {
            return ClientOrderUpsertResult.Fail(400, "Cannot merge a table into itself.");
        }

        var merge = await sessions.MergeSessionsAsync(
            order.TableSessionId.Value,
            child.CurrentSession.Id,
            string.IsNullOrWhiteSpace(actorName) ? "Client POS" : actorName.Trim(),
            $"Merged Table {child.TableNumber} into parent session");
        if (!merge.success)
        {
            return ClientOrderUpsertResult.Fail(422, merge.message);
        }

        var persisted = await _orderService.GetOrderByExternalIdAsync(order.OrderId);
        return ClientOrderUpsertResult.Ok(
            ToClientOrder(persisted ?? order),
            $"Table {child.TableNumber} merged into this order.");
    }

    public async Task<ClientOrderUpsertResult> FireCourseAsync(
        string? orderId,
        string? course,
        string? actorName)
    {
        var existing = await LoadMutableOrderAsync(orderId);
        if (!existing.Success || existing.Order is null)
        {
            return ClientOrderUpsertResult.Fail(existing.StatusCode, existing.Message);
        }

        var order = existing.Order;
        if (!string.Equals(NormalizeSavedOrderType(order.OrderType), "table", StringComparison.OrdinalIgnoreCase))
        {
            return ClientOrderUpsertResult.Fail(400, "Fire Course is only available on table orders.");
        }

        if (order.Items.Count == 0)
        {
            return ClientOrderUpsertResult.Fail(400, "There are no items to fire.");
        }

        var fireAll = string.Equals(course?.Trim(), "All", StringComparison.OrdinalIgnoreCase);
        var normalizedCourse = fireAll ? null : NormalizeCourseType(course);
        if (!fireAll && normalizedCourse == null)
        {
            return ClientOrderUpsertResult.Fail(400, "Choose Starters, Mains, Desserts, Drinks, or All.");
        }

        var menuItems = await _menuItemService.GetAllItemsAsync() ?? new List<FoodMenuItem>();
        var categories = await _categoryService.GetAllCategoriesAsync() ?? new List<MenuCategory>();
        foreach (var item in order.Items.Where(item => string.IsNullOrWhiteSpace(item.CourseType)))
        {
            item.CourseType = ResolveCourseTypeFromMenu(item.MenuItemId, menuItems, categories) ?? "Mains";
        }

        var matching = order.Items
            .Where(item => fireAll || string.Equals(item.CourseType, normalizedCourse, StringComparison.OrdinalIgnoreCase))
            .Where(item => item.MenuItemId == null ||
                           (!item.MenuItemId.StartsWith(TastingMenu.OrderMenuItemPrefix, StringComparison.OrdinalIgnoreCase) &&
                            !item.MenuItemId.StartsWith(TastingMenu.OrderCourseItemPrefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var toFire = matching.Where(item => !item.FiredAt.HasValue).ToList();
        if (toFire.Count == 0)
        {
            return ClientOrderUpsertResult.Fail(
                409,
                matching.Count > 0
                    ? $"{(fireAll ? "All matching courses have" : $"{normalizedCourse} has")} already been fired."
                    : $"There are no {(fireAll ? "courses" : normalizedCourse)} on this order.");
        }

        var fireOrder = ToKitchenPrintOrder(order);
        var fireIds = toFire
            .Select(item => string.IsNullOrWhiteSpace(item.ClientItemId) ? item.Id.ToString() : item.ClientItemId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fireItems = fireOrder.Items.Where(item => fireIds.Contains(item.Id)).ToList();
        fireOrder.Items.Clear();
        foreach (var item in fireItems)
        {
            fireOrder.Items.Add(item);
        }

        var fireLabel = fireAll ? "ALL COURSES" : normalizedCourse!.ToUpperInvariant();
        fireOrder.KitchenTicketType = $"FIRE {fireLabel}";

        var routing = ServiceHelper.GetService<OrderRoutingPrintService>() ?? new OrderRoutingPrintService();
        var printResult = await routing.PrintOrderAsync(fireOrder);
        if (!printResult.AnyPrinted)
        {
            var reason = printResult.FailedRoutes.FirstOrDefault()
                         ?? "No configured kitchen route accepted the course ticket.";
            return ClientOrderUpsertResult.Fail(422, $"Nothing was printed. {reason}");
        }

        var firedAt = DateTime.Now;
        var firedBy = string.IsNullOrWhiteSpace(actorName) ? "Client POS" : actorName.Trim();
        var printedIds = printResult.PrintedItemIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in toFire)
        {
            var lineId = string.IsNullOrWhiteSpace(item.ClientItemId) ? item.Id.ToString() : item.ClientItemId!;
            if (printedIds.Count == 0 || printedIds.Contains(lineId))
            {
                item.FiredAt = firedAt;
                item.FiredBy = firedBy;
            }
        }

        order.UpdatedAt = firedAt;
        var save = await _orderService.SaveOrderAsync(order);
        if (!save.Success)
        {
            return ClientOrderUpsertResult.Fail(422, save.Message ?? "Course printed but Mother could not save fire state.");
        }

        var persisted = await _orderService.GetOrderByExternalIdAsync(order.OrderId);
        return ClientOrderUpsertResult.Ok(
            ToClientOrder(persisted ?? order),
            $"Fired {fireLabel} ({toFire.Count} item(s)).");
    }

    public async Task<ClientOrderUpsertResult> ApplyLoyaltyRedeemAsync(
        string? orderId,
        string? lookup,
        int points,
        string? idempotencyKey,
        int? sessionUserId,
        string? sessionUserName)
    {
        var existing = await LoadMutableOrderAsync(orderId);
        if (!existing.Success || existing.Order is null)
        {
            return ClientOrderUpsertResult.Fail(existing.StatusCode, existing.Message);
        }

        var order = existing.Order;
        if (order.Items.Count == 0)
        {
            return ClientOrderUpsertResult.Fail(400, "Add items before redeeming loyalty points.");
        }

        if (order.TotalAmount <= 0m)
        {
            return ClientOrderUpsertResult.Fail(400, "There is no bill amount to offset with loyalty points.");
        }

        if (string.IsNullOrWhiteSpace(lookup) || points <= 0)
        {
            return ClientOrderUpsertResult.Fail(400, "Enter a customer lookup and points to redeem.");
        }

        var maxBillPoints = (int)Math.Floor(order.TotalAmount * 100m);
        if (points > maxBillPoints)
        {
            return ClientOrderUpsertResult.Fail(400, $"You can only redeem up to {maxBillPoints:N0} points on this bill.");
        }

        var loyalty = ClientPosLoyaltyService.TryResolve();
        if (loyalty is null)
        {
            return ClientOrderUpsertResult.Fail(503, "Mother loyalty service is not available. Check OrderWeb cloud settings.");
        }

        var txnId = string.IsNullOrWhiteSpace(idempotencyKey)
            ? $"{order.OrderId}:loyalty-redeem:{lookup.Trim()}:{points}"
            : idempotencyKey.Trim();
        var reason = $"Loyalty redemption - Client order {order.OrderNumber ?? order.OrderId}";
        var redeem = await loyalty.RedeemPointsAsync(lookup.Trim(), points, reason, txnId);
        if (!redeem.Success || redeem.Customer is null)
        {
            return ClientOrderUpsertResult.Fail(422, redeem.Error ?? "Unable to redeem loyalty points.");
        }

        var discountAmount = Math.Round(points / 100m, 2, MidpointRounding.AwayFromZero);
        var previous = order.DiscountAmount;
        order.CustomerName = string.IsNullOrWhiteSpace(redeem.Customer.CustomerName)
            ? order.CustomerName
            : redeem.Customer.CustomerName;
        order.CustomerPhone = string.IsNullOrWhiteSpace(redeem.Customer.Phone)
            ? order.CustomerPhone
            : redeem.Customer.Phone;
        order.LoyaltyPointsRedeemed += points;
        order.LoyaltyPointsDiscount += discountAmount;
        order.DiscountAmount = previous + discountAmount;
        ApplyClientOrderFinancials(order, NormalizeSavedOrderType(order.OrderType));
        order.UpdatedAt = DateTime.Now;

        var save = await _orderService.SaveOrderAsync(order);
        if (!save.Success)
        {
            return ClientOrderUpsertResult.Fail(
                422,
                "Points were redeemed in cloud but Mother could not update the order discount. Contact a manager.");
        }

        try
        {
            var audit = new DiscountAuditService(_databaseService, AuthenticationService.Instance);
            await audit.LogAsync(new DiscountAuditRequest
            {
                OrderId = order.OrderId,
                OrderNumber = order.OrderNumber,
                TableSessionId = order.TableSessionId,
                TableNumber = ExtractTableNumber(order),
                Action = previous > 0m ? "updated" : "applied",
                DiscountType = "loyalty",
                SubtotalAmount = order.SubtotalAmount,
                PreviousDiscountAmount = previous,
                DiscountAmount = order.DiscountAmount,
                DiscountPercent = 0m,
                TotalAfterDiscount = order.TotalAmount,
                Reason = $"{reason} ({sessionUserName ?? "Client"})",
                SourceArea = "client_order_loyalty",
                ApprovalRequired = false
            });
        }
        catch
        {
            // Best-effort.
        }

        var persisted = await _orderService.GetOrderByExternalIdAsync(order.OrderId);
        return ClientOrderUpsertResult.Ok(
            ToClientOrder(persisted ?? order),
            $"Redeemed {points:N0} points for £{discountAmount:F2}. Remaining balance: {redeem.Customer.PointsBalance:N0} pts.");
    }

    public async Task<ClientPreviousOrdersResult> GetPreviousCustomerOrdersAsync(string? customerPhone)
    {
        if (string.IsNullOrWhiteSpace(customerPhone))
        {
            return ClientPreviousOrdersResult.Fail(400, "Select a saved customer before viewing previous orders.");
        }

        try
        {
            var previous = await _orderService.GetPreviousCustomerOrdersAsync(
                customerPhone.Trim(),
                maximumOrders: 3,
                monthsBack: 12);
            var items = previous.Select(order => new ClientPreviousOrderDto(
                order.OrderDatabaseId,
                order.OrderNumber,
                order.CreatedAt.ToString("O"),
                order.OrderTypeDisplay,
                order.TotalAmount,
                order.StatusDisplay,
                order.ItemsText,
                order.OrderNotes,
                order.IsMostRecent)).ToList();
            return ClientPreviousOrdersResult.Ok(items);
        }
        catch (Exception ex)
        {
            return ClientPreviousOrdersResult.Fail(500, $"Previous orders unavailable: {ex.Message}");
        }
    }

    public async Task<(bool Success, int StatusCode, string Message, string? PrinterName)> OpenOrderPlaceCashDrawerAsync(
        string? reason,
        string? orderId,
        string? orderNumber,
        int? sessionUserId,
        string? sessionUserName,
        string? sessionUserRole)
    {
        var drawer = ServiceHelper.GetService<CashDrawerService>();
        if (drawer is null)
        {
            return (false, 503, "Mother cash drawer service is unavailable.", null);
        }

        var result = await drawer.OpenAsync(new CashDrawerOpenRequest
        {
            Reason = string.IsNullOrWhiteSpace(reason) ? "Order place MORE" : reason.Trim(),
            SourceArea = "client_order_more_options",
            OrderId = orderId,
            OrderNumber = orderNumber,
            RequestedByUserId = sessionUserId,
            RequestedByName = string.IsNullOrWhiteSpace(sessionUserName) ? "Client POS" : sessionUserName.Trim(),
            RequestedByRole = string.IsNullOrWhiteSpace(sessionUserRole) ? "User" : sessionUserRole.Trim()
        });
        return (result.Success, result.Success ? 200 : 503, result.Message, result.PrinterName);
    }

    private async Task<(bool Success, int StatusCode, string Message, Order? Order)> LoadMutableOrderAsync(string? orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return (false, 400, "A Mother order id is required.", null);
        }

        var existing = await _orderService.GetOrderByExternalIdAsync(orderId.Trim());
        if (existing == null)
        {
            return (false, 404, "Mother POS could not find this order.", null);
        }

        if (existing.LocalLifecycleState is LocalLifecycleState.Paid or LocalLifecycleState.Voided || existing.IsOpen == false)
        {
            return (false, 409, "This order is closed on Mother POS and cannot be changed.", null);
        }

        return (true, 200, string.Empty, existing);
    }

    private async Task<(bool Success, string Message, User? User)> ResolveManagerApprovalAsync(
        int? sessionUserId,
        string? sessionUserName,
        string? approvingPin,
        bool requirePinWhenNotManager)
    {
        var auth = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        User? sessionUser = null;
        if (sessionUserId is > 0)
        {
            sessionUser = await TryLoadUserAsync(sessionUserId.Value);
        }

        if (sessionUser is { Role: UserRole.Manager or UserRole.Admin })
        {
            return (true, string.Empty, sessionUser);
        }

        if (!requirePinWhenNotManager)
        {
            return (true, string.Empty, sessionUser);
        }

        if (string.IsNullOrWhiteSpace(approvingPin))
        {
            return (false, "Manager PIN is required for this action.", null);
        }

        var approval = await auth.ValidatePinAsync(approvingPin.Trim());
        if (!approval.Success || approval.User is not { Role: UserRole.Manager or UserRole.Admin })
        {
            return (false,
                approval.Success
                    ? "This PIN does not belong to a Manager or Administrator."
                    : approval.Message,
                null);
        }

        return (true, string.Empty, approval.User);
    }

    private async Task<User?> TryLoadUserAsync(int userId)
    {
        try
        {
            await using var connection = new MySqlConnector.MySqlConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();
            await using var command = new MySqlConnector.MySqlCommand(
                "SELECT id, name, username, role, is_active FROM users WHERE id = @id AND COALESCE(is_archived, FALSE) = FALSE LIMIT 1",
                connection);
            command.Parameters.AddWithValue("@id", userId);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            var roleText = reader["role"]?.ToString() ?? "Staff";
            Enum.TryParse<UserRole>(roleText, true, out var role);
            return new User
            {
                Id = Convert.ToInt32(reader["id"]),
                Name = reader["name"]?.ToString() ?? string.Empty,
                Username = reader["username"]?.ToString() ?? string.Empty,
                Role = role,
                IsActive = Convert.ToBoolean(reader["is_active"])
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractTableNumber(Order order)
    {
        var name = order.CustomerName?.Trim();
        if (name?.StartsWith("Table ", StringComparison.OrdinalIgnoreCase) == true)
        {
            return name[6..].Trim();
        }

        return null;
    }

    private static string? NormalizeCourseType(string? course)
    {
        if (string.IsNullOrWhiteSpace(course))
        {
            return null;
        }

        var value = course.Trim().ToLowerInvariant();
        if (value.Contains("starter") || value.Contains("appetiser") || value.Contains("appetizer"))
        {
            return "Starters";
        }

        if (value.Contains("dessert") || value.Contains("pudding") || value.Contains("sweet"))
        {
            return "Desserts";
        }

        if (value.Contains("drink") || value.Contains("beverage") || value.Contains("cocktail")
            || value.Contains("wine") || value.Contains("beer") || value.Contains("bar"))
        {
            return "Drinks";
        }

        if (value.Contains("main") || value.Contains("entree") || value.Contains("entrée"))
        {
            return "Mains";
        }

        return value switch
        {
            "starters" => "Starters",
            "mains" => "Mains",
            "desserts" => "Desserts",
            "drinks" => "Drinks",
            "all" => null,
            _ => null
        };
    }

    /// <summary>Mother Order Place parity: blank CourseType from category name / Drink item type.</summary>
    private static string? ResolveCourseTypeFromMenu(
        string? menuItemId,
        IReadOnlyList<FoodMenuItem> menuItems,
        IReadOnlyList<MenuCategory> categories)
    {
        if (string.IsNullOrWhiteSpace(menuItemId))
        {
            return null;
        }

        var menuItem = menuItems.FirstOrDefault(item =>
            string.Equals(item.Id, menuItemId, StringComparison.OrdinalIgnoreCase));
        if (menuItem is null)
        {
            return null;
        }

        var categoryId = menuItem.CategoryId;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!string.IsNullOrWhiteSpace(categoryId) && visited.Add(categoryId))
        {
            var category = categories.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, categoryId, StringComparison.OrdinalIgnoreCase));
            if (category is null)
            {
                break;
            }

            var course = NormalizeCourseType(category.Name);
            if (course is not null)
            {
                return course;
            }

            categoryId = category.ParentId;
        }

        return string.Equals(menuItem.ItemType, "Drink", StringComparison.OrdinalIgnoreCase)
            ? "Drinks"
            : "Mains";
    }

    private static ServiceChargeClassification? ParseServiceChargeClassification(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "optional" => ServiceChargeClassification.Optional,
            "compulsory" => ServiceChargeClassification.Compulsory,
            _ => null
        };

    private static TableOrder ToKitchenPrintOrder(Order order)
    {
        var printOrder = new TableOrder
        {
            Id = order.OrderId,
            OrderNumber = order.OrderNumber,
            TableNumber = int.TryParse(ExtractTableNumber(order), out var tableNumber) ? tableNumber : 0,
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            Notes = order.SpecialInstructions,
            OrderMode = string.Equals(order.OrderType, "table", StringComparison.OrdinalIgnoreCase) ? "dine_in" : "takeaway",
            StartTime = order.CreatedAt,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt
        };
        foreach (var item in order.Items)
        {
            printOrder.Items.Add(new TableOrderItem
            {
                Id = string.IsNullOrWhiteSpace(item.ClientItemId) ? item.Id.ToString() : item.ClientItemId,
                OrderId = order.OrderId,
                MenuItemId = item.MenuItemId ?? string.Empty,
                VariantId = item.VariantId,
                VariantName = item.VariantName,
                DisplayName = item.DisplayName,
                PrintGroupId = item.PrintGroupId,
                PrintInRed = item.PrintInRed,
                Name = item.ItemName,
                UnitPrice = item.ItemPrice ?? 0m,
                Quantity = Math.Max(1, item.Quantity),
                Notes = item.SpecialInstructions,
                CourseType = item.CourseType,
                SendStatus = ItemSendStatus.NotSent
            });
        }

        return printOrder;
    }
}

public sealed record ClientPreviousOrderDto(
    int OrderDatabaseId,
    string? OrderNumber,
    string CreatedAtUtc,
    string OrderType,
    decimal TotalAmount,
    string Status,
    string ItemsText,
    string? OrderNotes,
    bool IsMostRecent);

public sealed record ClientPreviousOrdersResult(
    bool Success,
    int StatusCode,
    string Message,
    IReadOnlyList<ClientPreviousOrderDto> Orders)
{
    public static ClientPreviousOrdersResult Ok(IReadOnlyList<ClientPreviousOrderDto> orders) =>
        new(true, 200, string.Empty, orders);

    public static ClientPreviousOrdersResult Fail(int statusCode, string message) =>
        new(false, statusCode, message, Array.Empty<ClientPreviousOrderDto>());
}
