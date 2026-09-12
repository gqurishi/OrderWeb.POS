using MyFirstMauiApp.Models.FoodMenu;
using MyFirstMauiApp.Services;
using MySqlConnector;
using OrderWeb.Contracts.Access;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Synchronization;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Mother-side operational APIs for Client POS: food menu, delivery zones,
/// and table/collection/delivery orders. Reuses the same services Mother's own till uses.
/// </summary>
public sealed partial class ClientPosOperationalService
{
    private const string LiveOrderSourceFilter = @"
        LOWER(COALESCE(NULLIF(o.source_channel, ''), 'local')) = 'local'";

    private const string ActiveLifecycleFilter = @"
        AND COALESCE(o.is_open, 1) = 1
        AND COALESCE(o.draft_abandoned_flag, 0) = 0
        AND (
              LOWER(COALESCE(NULLIF(o.local_lifecycle_state, ''), 'draft')) IN ('sent_partial', 'sent_full', 'payment_partial')
              OR o.first_sent_at IS NOT NULL
              OR COALESCE(o.send_attempt_count, 0) > 0
              OR LOWER(COALESCE(o.status, '')) IN ('kitchen', 'preparing', 'ready')
            )";

    private readonly DatabaseService _databaseService;
    private readonly ReservationSyncService _reservationSync;
    private readonly OrderService _orderService = new();
    private readonly MenuCategoryService _categoryService = new();
    private readonly MenuItemService _menuItemService = new();
    private readonly MealDealService _mealDealService = new();
    private readonly TastingMenuService _tastingMenuService = new();
    private readonly TableServiceChargeSettingsService _tableServiceChargeSettingsService;

    public ClientPosOperationalService(DatabaseService databaseService, ReservationSyncService? reservationSync = null)
    {
        _databaseService = databaseService;
        _reservationSync = reservationSync
            ?? ServiceHelper.GetService<ReservationSyncService>()
            ?? new ReservationSyncService(databaseService);
        _tableServiceChargeSettingsService = ServiceHelper.GetService<TableServiceChargeSettingsService>()
            ?? new TableServiceChargeSettingsService(
                databaseService,
                ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance);
    }

    public async Task<ClientMenuSnapshot> BuildMenuSnapshotAsync(string version)
    {
        var allCategories = await _categoryService.GetAllCategoriesAsync();
        var items = await _menuItemService.GetAllItemsAsync();

        // Client order place must match Mother Food Menu: export every category Mother has,
        // including Inactive. Filtering to Active-only left Client with an empty menu while
        // Mother UI still showed categories.
        var categories = allCategories
            .Where(category => !string.IsNullOrWhiteSpace(category.Id))
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.Name)
            .ToList();

        var usedIds = new HashSet<int>();
        var categoryIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var snapshotCategories = new List<ClientMenuCategoryDto>();
        foreach (var category in categories)
        {
            var categoryId = StableEntityId.FromKey($"cat:{category.Id}", usedIds);
            categoryIds[category.Id] = categoryId;
            snapshotCategories.Add(new ClientMenuCategoryDto(
                categoryId,
                category.Id,
                category.Name,
                string.IsNullOrWhiteSpace(category.Color) ? "#3B82F6" : category.Color,
                category.DisplayOrder,
                // Expose Inactive categories as active on Client so order place can sell linked items.
                IsActive: true,
                ParentMotherId: string.IsNullOrWhiteSpace(category.ParentId) ? null : category.ParentId.Trim()));
        }

        if (snapshotCategories.Count == 0)
        {
            AppDiagnostics.Log(
                $"Client menu snapshot EMPTY (source: {allCategories.Count} categories / {items.Count} items). " +
                "Open Mother → Food Menu and add categories/items.");
        }

        var products = new List<ClientMenuProductDto>();
        var prices = new List<ClientMenuPriceDto>();
        var modifierGroups = new List<ClientMenuModifierGroupDto>();
        var modifiers = new List<ClientMenuModifierDto>();
        var productModifiers = new List<ClientMenuProductModifierDto>();
        var variants = new List<ClientMenuVariantDto>();
        var quickNotes = new List<ClientMenuQuickNoteDto>();

        foreach (var item in items
                     .Where(item => !string.IsNullOrWhiteSpace(item.CategoryId) && categoryIds.ContainsKey(item.CategoryId))
                     .OrderBy(item => item.DisplayOrder)
                     .ThenBy(item => item.Name))
        {
            var productId = StableEntityId.FromKey($"prod:{item.Id}", usedIds);
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
            prices.Add(new ClientMenuPriceDto(
                StableEntityId.FromKey($"price:{item.Id}:takeaway", usedIds),
                productId,
                "takeaway",
                takeaway,
                "GBP",
                null));
            if (dineIn != takeaway)
            {
                prices.Add(new ClientMenuPriceDto(
                    StableEntityId.FromKey($"price:{item.Id}:dine_in", usedIds),
                    productId,
                    "dine_in",
                    dineIn,
                    "GBP",
                    null));
            }

            foreach (var variant in (item.Variants ?? new List<MenuItemVariant>())
                         .Where(variant => variant.Active && !string.IsNullOrWhiteSpace(variant.Name))
                         .OrderBy(variant => variant.DisplayOrder)
                         .ThenBy(variant => variant.Name))
            {
                var variantMotherId = string.IsNullOrWhiteSpace(variant.Id)
                    ? $"{item.Id}:{variant.Name}"
                    : variant.Id;
                // Mother stores one variant price today; expose both channels for Client parity.
                var variantPrice = variant.Price;
                variants.Add(new ClientMenuVariantDto(
                    StableEntityId.FromKey($"var:{variantMotherId}", usedIds),
                    variantMotherId,
                    productId,
                    variant.Name.Trim(),
                    variant.Description,
                    variantPrice,
                    variantPrice,
                    variant.DisplayOrder,
                    true));
            }

            var addons = item.Addons
                .Where(addon => !string.IsNullOrWhiteSpace(addon.Name))
                .ToList();
            if (addons.Count == 0)
            {
                continue;
            }

            var groupMotherId = $"{item.Id}:addons";
            var groupId = StableEntityId.FromKey($"mgroup:{groupMotherId}", usedIds);
            modifierGroups.Add(new ClientMenuModifierGroupDto(
                groupId,
                groupMotherId,
                "Add-ons",
                0,
                Math.Max(addons.Count, 1),
                true));
            productModifiers.Add(new ClientMenuProductModifierDto(productId, groupId, 0));
            foreach (var addon in addons)
            {
                var modifierMotherId = string.IsNullOrWhiteSpace(addon.Id) ? $"{item.Id}:{addon.Name}" : addon.Id;
                modifiers.Add(new ClientMenuModifierDto(
                    StableEntityId.FromKey($"mod:{modifierMotherId}", usedIds),
                    modifierMotherId,
                    groupId,
                    addon.Name,
                    addon.Price,
                    true));
            }
        }

        var mealDeals = new List<ClientMealDealDto>();
        var mealDealChoices = new List<ClientMealDealChoiceDto>();
        var mealDealCategoryRules = new List<ClientMealDealCategoryRuleDto>();
        try
        {
            foreach (var deal in (await _mealDealService.GetActiveDealsAsync())
                         .OrderBy(deal => deal.DisplayOrder)
                         .ThenBy(deal => deal.Name))
            {
                var dealId = StableEntityId.FromKey($"mealdeal:{deal.Id}", usedIds);
                mealDeals.Add(new ClientMealDealDto(
                    dealId,
                    deal.Id,
                    deal.Name,
                    deal.Description,
                    deal.Price,
                    string.IsNullOrWhiteSpace(deal.Color) ? "#F59E0B" : deal.Color,
                    Math.Max(1, deal.PickCount),
                    string.IsNullOrWhiteSpace(deal.VatCategory) ? "HotFood" : deal.VatCategory,
                    deal.DisplayOrder,
                    true));

                foreach (var choice in deal.Choices
                             .Where(choice => !string.IsNullOrWhiteSpace(choice.Name))
                             .OrderBy(choice => choice.SortOrder)
                             .ThenBy(choice => choice.Name))
                {
                    var choiceMotherId = string.IsNullOrWhiteSpace(choice.Id)
                        ? $"{deal.Id}:{choice.Name}"
                        : choice.Id;
                    mealDealChoices.Add(new ClientMealDealChoiceDto(
                        StableEntityId.FromKey($"mealdeal-choice:{choiceMotherId}", usedIds),
                        choiceMotherId,
                        dealId,
                        choice.Name.Trim(),
                        choice.SortOrder));
                }

                foreach (var rule in deal.Categories
                             .Where(rule => !string.IsNullOrWhiteSpace(rule.Name))
                             .OrderBy(rule => rule.Name))
                {
                    var ruleMotherId = string.IsNullOrWhiteSpace(rule.Id)
                        ? $"{deal.Id}:rule:{rule.Name}"
                        : rule.Id;
                    var linkedIds = (rule.MenuItemIds ?? new List<string>())
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Select(id => id.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    mealDealCategoryRules.Add(new ClientMealDealCategoryRuleDto(
                        StableEntityId.FromKey($"mealdeal-rule:{ruleMotherId}", usedIds),
                        ruleMotherId,
                        dealId,
                        rule.Name.Trim(),
                        rule.IsRequired,
                        Math.Max(0, rule.MinSelections),
                        Math.Max(1, rule.MaxSelections),
                        linkedIds));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClientMenu] Meal deals snapshot skipped: {ex.Message}");
        }

        var tastingMenus = new List<ClientTastingMenuDto>();
        var tastingOptions = new List<ClientTastingMenuOptionDto>();
        var tastingCourses = new List<ClientTastingMenuCourseDto>();
        var tastingChoices = new List<ClientTastingMenuChoiceDto>();
        try
        {
            foreach (var tasting in (await _tastingMenuService.GetActiveAsync())
                         .OrderBy(menu => menu.DisplayOrder)
                         .ThenBy(menu => menu.Name))
            {
                var tastingId = StableEntityId.FromKey($"tasting:{tasting.Id}", usedIds);
                tastingMenus.Add(new ClientTastingMenuDto(
                    tastingId,
                    tasting.Id,
                    tasting.Name,
                    tasting.Description,
                    string.IsNullOrWhiteSpace(tasting.Color) ? "#0EA5E9" : tasting.Color,
                    tasting.DisplayOrder,
                    true));

                foreach (var option in tasting.Options
                             .OrderBy(option => option.SortOrder)
                             .ThenBy(option => option.Name))
                {
                    var optionMotherId = string.IsNullOrWhiteSpace(option.Id)
                        ? $"{tasting.Id}:opt:{option.Name}:{option.SortOrder}"
                        : option.Id;
                    tastingOptions.Add(new ClientTastingMenuOptionDto(
                        StableEntityId.FromKey($"tasting-opt:{optionMotherId}", usedIds),
                        optionMotherId,
                        tastingId,
                        string.IsNullOrWhiteSpace(option.Name) ? option.DisplayName : option.Name.Trim(),
                        option.Price,
                        option.IncludesWine,
                        option.CourseCount,
                        option.SortOrder));
                }

                foreach (var course in tasting.Courses
                             .OrderBy(course => course.CourseNumber)
                             .ThenBy(course => course.Name))
                {
                    var courseMotherId = string.IsNullOrWhiteSpace(course.Id)
                        ? $"{tasting.Id}:course:{course.CourseNumber}:{course.Name}"
                        : course.Id;
                    var courseId = StableEntityId.FromKey($"tasting-course:{courseMotherId}", usedIds);
                    tastingCourses.Add(new ClientTastingMenuCourseDto(
                        courseId,
                        courseMotherId,
                        tastingId,
                        course.Name.Trim(),
                        course.WineName,
                        course.CourseNumber,
                        course.Required,
                        string.IsNullOrWhiteSpace(course.VatCategory) ? "HotFood" : course.VatCategory,
                        course.CourseNumber));

                    foreach (var choice in course.Choices
                                 .Where(choice => !string.IsNullOrWhiteSpace(choice.Name))
                                 .OrderBy(choice => choice.SortOrder)
                                 .ThenBy(choice => choice.Name))
                    {
                        var choiceMotherId = string.IsNullOrWhiteSpace(choice.Id)
                            ? $"{courseMotherId}:{choice.Name}"
                            : choice.Id;
                        tastingChoices.Add(new ClientTastingMenuChoiceDto(
                            StableEntityId.FromKey($"tasting-choice:{choiceMotherId}", usedIds),
                            choiceMotherId,
                            courseId,
                            choice.Name.Trim(),
                            choice.PrintGroupId,
                            choice.SortOrder));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClientMenu] Tasting menus snapshot skipped: {ex.Message}");
        }

        // Mother till synthesizes these category buttons; Client needs the same anchors.
        if (mealDeals.Count > 0 &&
            !snapshotCategories.Any(category =>
                string.Equals(category.MotherId, MealDeal.PosCategoryId, StringComparison.Ordinal)))
        {
            snapshotCategories.Insert(0, new ClientMenuCategoryDto(
                StableEntityId.FromKey($"cat:{MealDeal.PosCategoryId}", usedIds),
                MealDeal.PosCategoryId,
                "Meal Deals",
                "#F59E0B",
                -1,
                true));
        }

        if (tastingMenus.Count > 0 &&
            !snapshotCategories.Any(category =>
                string.Equals(category.MotherId, TastingMenu.PosCategoryId, StringComparison.Ordinal)))
        {
            snapshotCategories.Insert(0, new ClientMenuCategoryDto(
                StableEntityId.FromKey($"cat:{TastingMenu.PosCategoryId}", usedIds),
                TastingMenu.PosCategoryId,
                "Tasting Menus",
                "#0EA5E9",
                -2,
                true));
        }

        AppDiagnostics.Log(
            $"Client menu snapshot: {snapshotCategories.Count} categories, {products.Count} products, " +
            $"{mealDeals.Count} meal deals, {tastingMenus.Count} tasting menus " +
            $"(source: {allCategories.Count} categories / {items.Count} items).");

        var productMotherToId = products.ToDictionary(
            product => product.MotherId,
            product => product.Id,
            StringComparer.OrdinalIgnoreCase);
        try
        {
            var notesByProduct = new Dictionary<int, int>();
            foreach (var note in await _menuItemService.GetAllActiveQuickNotesAsync())
            {
                if (string.IsNullOrWhiteSpace(note.MenuItemId) ||
                    string.IsNullOrWhiteSpace(note.NoteText) ||
                    !productMotherToId.TryGetValue(note.MenuItemId.Trim(), out var productId))
                {
                    continue;
                }

                notesByProduct.TryGetValue(productId, out var count);
                if (count >= 6)
                {
                    continue;
                }

                notesByProduct[productId] = count + 1;
                var noteMotherId = string.IsNullOrWhiteSpace(note.Id)
                    ? $"{note.MenuItemId}:{note.DisplayOrder}:{note.NoteText}"
                    : note.Id.Trim();
                quickNotes.Add(new ClientMenuQuickNoteDto(
                    StableEntityId.FromKey($"qnote:{noteMotherId}", usedIds),
                    noteMotherId,
                    productId,
                    note.NoteText.Trim(),
                    note.DisplayOrder,
                    true));
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Client menu quick notes skipped: {ex.Message}");
        }

        return new ClientMenuSnapshot(
            version,
            snapshotCategories,
            products,
            prices,
            modifierGroups,
            modifiers,
            productModifiers,
            variants,
            mealDeals,
            mealDealChoices,
            mealDealCategoryRules,
            tastingMenus,
            tastingOptions,
            tastingCourses,
            tastingChoices,
            quickNotes);
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

        var existing = await _orderService.GetOrderByExternalIdAsync(order.OrderId);

        foreach (var line in incomingLines)
        {
            var built = BuildOrderItem(line, menuById, orderType);
            PreserveLineKitchenState(built, existing);
            order.Items.Add(built);
        }

        var foodSubtotal = order.Items.Sum(item => item.TotalPrice);
        order.SubtotalAmount = foodSubtotal;
        order.TaxAmount = 0m;

        if (existing != null)
        {
            order.DiscountAmount = request.Discount is >= 0 ? request.Discount.Value : existing.DiscountAmount;
            order.ServiceChargePercentage = existing.ServiceChargePercentage;
            order.ServiceChargeStatus = existing.ServiceChargeStatus;
            order.ServiceChargeClassification = existing.ServiceChargeClassification;
            order.ServiceChargeBasis = existing.ServiceChargeBasis;
            order.ServiceChargeAmount = existing.ServiceChargeAmount;
            if (string.IsNullOrWhiteSpace(request.Notes))
            {
                // Keep Mother order notes when Client omits Notes on a line-edit upsert.
                order.SpecialInstructions = existing.SpecialInstructions;
            }
        }
        else if (orderType == "table")
        {
            await ApplyDefaultTableServiceChargeAsync(order);
            if (request.Discount is > 0)
            {
                order.DiscountAmount = request.Discount.Value;
            }
        }
        else if (request.Discount is > 0)
        {
            order.DiscountAmount = request.Discount.Value;
        }

        ApplyClientOrderFinancials(order, orderType);

        // Match Mother till: open the table (session / Occupied) and let Client show the order
        // create screen with an empty basket. Persist the Mother ledger row only once items exist.
        if (existing == null && orderType == "table" && incomingLines.Count == 0)
        {
            var customerSummary = string.Join(" · ", new[] { customerName, order.CustomerPhone }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
            var draft = new ClientOperationalOrder(
                order.OrderId,
                string.Empty,
                "Table",
                customerName,
                request.TableId,
                request.TableNumber?.Trim(),
                Math.Max(1, request.Guests),
                Array.Empty<ClientOperationalOrderLine>(),
                0m,
                0m,
                0m,
                0,
                DateTime.UtcNow.ToString("O"),
                string.IsNullOrWhiteSpace(customerSummary) ? null : customerSummary);
            return ClientOrderUpsertResult.Ok(draft, "Table ready — add items to create the order.");
        }

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
            // Phase 3: concurrent edit — reject stale saves.
            if (HasOptimisticConcurrencyConflict(existing, request.ExpectedVersion, request.ExpectedUpdatedUtc))
            {
                return ClientOrderUpsertResult.Conflict(
                    ToClientOrder(existing),
                    "Order updated elsewhere — reload");
            }

            order.OrderNumber = existing.OrderNumber;
            order.CreatedAt = existing.CreatedAt;
            order.Status = existing.Status;
            order.LocalLifecycleState = existing.LocalLifecycleState;
            order.IsOpen = existing.IsOpen;

            // Resume / open-for-edit must never wipe Mother lines with an empty payload.
            if (incomingLines.Count == 0 && existing.Items.Count > 0)
            {
                if (tableSessionId.HasValue)
                {
                    var linkedOnly = await new TableSessionService().LinkOrderToSessionAsync(
                        tableSessionId.Value,
                        existing.OrderId,
                        TableSessionStatus.Ordering,
                        "Client POS");
                    if (!linkedOnly.success)
                    {
                        return ClientOrderUpsertResult.Fail(409, linkedOnly.message);
                    }
                }

                var resumed = ToClientOrder(existing);
                if (orderType == "table")
                {
                    resumed = resumed with
                    {
                        TableId = request.TableId,
                        TableNumber = request.TableNumber?.Trim(),
                        Guests = Math.Max(1, request.Guests)
                    };
                }

                return ClientOrderUpsertResult.Ok(resumed, "Order resumed from Mother POS.");
            }
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

    /// <summary>
    /// Voids a Client order on Mother (reason + manager PIN), or releases an uncommitted table session
    /// when no ledger row exists yet (same as Mother till discard).
    /// </summary>
    public async Task<ClientOrderUpsertResult> VoidOrderAsync(
        string? orderId,
        string? reason = null,
        string? approvingPin = null,
        int? sessionUserId = null,
        string? sessionUserName = null,
        int? tableId = null)
    {
        if (string.IsNullOrWhiteSpace(orderId) && tableId is null or <= 0)
        {
            return ClientOrderUpsertResult.Fail(400, "A Mother order id is required to void.");
        }

        var existing = string.IsNullOrWhiteSpace(orderId)
            ? null
            : await _orderService.GetOrderByExternalIdAsync(orderId.Trim());

        // Uncommitted table: session open, no ledger row yet — release without a void history row.
        if (existing == null)
        {
            var releaseTableId = tableId;
            if (releaseTableId is null or <= 0)
            {
                return ClientOrderUpsertResult.Fail(404, "Mother POS could not find this order.");
            }

            var sessions = new TableSessionService();
            var release = await sessions.ForceReleaseTableAsync(
                releaseTableId.Value,
                "unsent_basket_discarded",
                string.IsNullOrWhiteSpace(sessionUserName) ? "Client POS" : sessionUserName.Trim());
            if (!release.success)
            {
                return ClientOrderUpsertResult.Fail(422, release.message);
            }

            var released = new ClientOperationalOrder(
                orderId?.Trim() ?? Guid.NewGuid().ToString("N"),
                string.Empty,
                "Table",
                "Voided",
                releaseTableId,
                null,
                1,
                Array.Empty<ClientOperationalOrderLine>(),
                0m,
                0m,
                0m,
                0,
                DateTime.UtcNow.ToString("O"),
                null);
            return ClientOrderUpsertResult.Ok(released, "Table released — no order was created.");
        }

        if (existing.LocalLifecycleState is LocalLifecycleState.Paid or LocalLifecycleState.Voided)
        {
            return ClientOrderUpsertResult.Fail(409, "This order is already paid or voided on Mother POS.");
        }

        var payments = await _orderService.GetOrderPaymentsAsync(existing.Id);
        var approvedPaymentTotal = payments
            .Where(payment => string.Equals(payment.Status, "approved", StringComparison.OrdinalIgnoreCase))
            .Sum(payment => payment.Amount);
        if (approvedPaymentTotal > 0m)
        {
            return ClientOrderUpsertResult.Fail(
                409,
                $"£{approvedPaymentTotal:F2} has already been approved for this order. Refund or void the payment before voiding the order.");
        }

        var voidReason = string.IsNullOrWhiteSpace(reason) ? "Unspecified" : reason.Trim();
        var approval = await ResolveManagerApprovalAsync(
            sessionUserId,
            sessionUserName,
            approvingPin,
            requirePinWhenNotManager: true);
        if (!approval.Success || approval.User is null)
        {
            return ClientOrderUpsertResult.Fail(403, approval.Message);
        }

        var approverName = string.IsNullOrWhiteSpace(approval.User.Name)
            ? approval.User.Username
            : approval.User.Name;

        // Match Mother Order Place void: keep DB UpdatedAt as the concurrency token.
        // Stamping DateTime.Now here caused false "Order changed on another terminal" failures.
        var expectedUpdatedAt = existing.UpdatedAt == default ? (DateTime?)null : existing.UpdatedAt;

        existing.LocalLifecycleState = LocalLifecycleState.Voided;
        existing.Status = OrderStatus.Cancelled;
        existing.IsOpen = false;
        existing.VoidReason = voidReason;
        existing.VoidedAt = DateTime.Now;
        existing.VoidedBy = approverName;
        existing.ExpectedUpdatedAt = expectedUpdatedAt;

        var save = await _orderService.SaveOrderAsync(existing);
        if (!save.Success)
        {
            return ClientOrderUpsertResult.Fail(422, save.Message ?? "Mother POS could not void this order.");
        }

        await _orderService.LogOrderEventAsync(
            existing.OrderId,
            "voided",
            actorType: "user",
            actorId: approval.User.Id.ToString(),
            actorName: approverName,
            payload: new
            {
                reason = voidReason,
                amount = existing.TotalAmount,
                itemCount = existing.Items?.Count ?? 0,
                source = "client_pos"
            });

        // Match Mother EnsureTableReleasedAfterFinalizeAsync: close session, then force-release.
        var voidSessions = new TableSessionService();
        var voidTableId = tableId;
        if ((voidTableId is null or <= 0) && existing.TableSessionId is > 0)
        {
            var linkedSession = await voidSessions.GetSessionByIdAsync(existing.TableSessionId.Value);
            if (linkedSession?.TableId > 0)
            {
                voidTableId = linkedSession.TableId;
            }
        }

        var tableReleased = false;
        if (existing.TableSessionId is > 0)
        {
            var close = await voidSessions.CloseSessionForOrderAsync(
                existing.TableSessionId.Value,
                "voided",
                approverName);
            tableReleased = close.success;
        }

        if (!tableReleased && voidTableId is > 0)
        {
            await voidSessions.ForceReleaseTableAsync(voidTableId.Value, "voided", approverName);
        }

        AppDataRefreshService.RequestRefresh(AppDataRefreshType.Orders | AppDataRefreshType.Tables);

        var persisted = await _orderService.GetOrderByExternalIdAsync(existing.OrderId);
        if (persisted == null)
        {
            return ClientOrderUpsertResult.Fail(500, "Mother POS voided the order but could not reload it.");
        }

        return ClientOrderUpsertResult.Ok(ToClientOrder(persisted), $"Order voided on Mother POS. Reason: {voidReason}");
    }

    /// <summary>Fresh load of one open order for Client reopen/edit (Phase 2 Collection).</summary>
    public async Task<ClientOrderUpsertResult> GetOrderAsync(string? orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return ClientOrderUpsertResult.Fail(400, "A Mother order id is required.");
        }

        var existing = await _orderService.GetOrderByExternalIdAsync(orderId.Trim());
        if (existing == null)
        {
            return ClientOrderUpsertResult.Fail(404, "Mother POS could not find this order.");
        }

        if (existing.LocalLifecycleState is LocalLifecycleState.Paid or LocalLifecycleState.Voided ||
            existing.IsOpen == false)
        {
            return ClientOrderUpsertResult.Fail(409, "This order is closed on Mother POS and cannot be edited.");
        }

        var clientOrder = ToClientOrder(existing);
        if (string.Equals(NormalizeSavedOrderType(existing.OrderType), "table", StringComparison.OrdinalIgnoreCase) &&
            existing.TableSessionId is > 0)
        {
            var session = await new TableSessionService().GetSessionByIdAsync(existing.TableSessionId.Value);
            if (session != null)
            {
                var tableNumber = session.Table?.TableNumber;
                if (string.IsNullOrWhiteSpace(tableNumber) &&
                    !string.IsNullOrWhiteSpace(existing.CustomerName) &&
                    existing.CustomerName.StartsWith("Table ", StringComparison.OrdinalIgnoreCase))
                {
                    tableNumber = existing.CustomerName["Table ".Length..].Trim();
                }

                clientOrder = clientOrder with
                {
                    TableId = session.TableId,
                    TableNumber = tableNumber,
                    Guests = Math.Max(1, session.PartySize)
                };
            }
        }

        return ClientOrderUpsertResult.Ok(clientOrder, "Order loaded from Mother POS.");
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
        var mealDealId = ResolveMealDealId(line);
        if (!string.IsNullOrWhiteSpace(mealDealId))
        {
            return BuildMealDealOrderItem(line, mealDealId);
        }

        if (!string.IsNullOrWhiteSpace(line.TastingMenuId) ||
            (line.ProductId ?? string.Empty).StartsWith(TastingMenu.OrderMenuItemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return BuildTastingPackageOrderItem(line);
        }

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
        var variant = ResolveVariant(menuItem, line);
        var basePrice = variant != null
            ? variant.Price
            : menuItem?.GetEffectivePrice(orderType) ?? Math.Max(0m, line.UnitPrice - addonTotal);
        if (variant == null && line.VariantPrice is > 0)
        {
            basePrice = line.VariantPrice.Value;
        }

        var itemName = menuItem?.Name ?? line.Name.Trim();
        var variantName = variant?.Name
            ?? (string.IsNullOrWhiteSpace(line.VariantName) ? null : line.VariantName.Trim());
        var displayName = string.IsNullOrWhiteSpace(variantName)
            ? itemName
            : $"{itemName} ({variantName})";

        return new OrderItem
        {
            ClientItemId = string.IsNullOrWhiteSpace(line.Id) ? Guid.NewGuid().ToString("N") : line.Id.Trim(),
            MenuItemId = menuItem?.Id ?? (IsLikelyMotherMenuId(line.ProductId) ? line.ProductId : null),
            ItemName = itemName,
            DisplayName = displayName,
            VariantId = variant?.Id ?? (string.IsNullOrWhiteSpace(line.VariantId) ? null : line.VariantId.Trim()),
            VariantName = variantName,
            PrintGroupId = menuItem?.PrintGroupId,
            PrintInRed = menuItem?.PrintInRed ?? false,
            Quantity = line.Quantity,
            ItemPrice = basePrice,
            SpecialInstructions = string.IsNullOrWhiteSpace(line.Notes) ? null : line.Notes.Trim(),
            Addons = addons
        };
    }

    private static OrderItem BuildMealDealOrderItem(ClientOrderLineRequest line, string mealDealId)
    {
        var choices = (line.MealDealChoices ?? line.Modifiers ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var notes = string.IsNullOrWhiteSpace(line.Notes)
            ? MealDealNotesHelper.FormatSelections(choices)
            : line.Notes.Trim();
        if (string.IsNullOrWhiteSpace(notes) && choices.Count > 0)
        {
            notes = MealDealNotesHelper.FormatSelections(choices);
        }

        return new OrderItem
        {
            ClientItemId = string.IsNullOrWhiteSpace(line.Id) ? Guid.NewGuid().ToString("N") : line.Id.Trim(),
            MenuItemId = MealDealNotesHelper.BuildOrderMenuItemId(mealDealId),
            ItemName = string.IsNullOrWhiteSpace(line.Name) ? "Meal Deal" : line.Name.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(line.Name) ? "Meal Deal" : line.Name.Trim(),
            Quantity = Math.Max(1, line.Quantity),
            ItemPrice = Math.Max(0m, line.UnitPrice),
            SpecialInstructions = string.IsNullOrWhiteSpace(notes) ? null : notes,
            Addons = new List<OrderItemAddon>()
        };
    }

    private static OrderItem BuildTastingPackageOrderItem(ClientOrderLineRequest line)
    {
        var tastingId = string.IsNullOrWhiteSpace(line.TastingMenuId)
            ? (line.ProductId ?? string.Empty).Replace(TastingMenu.OrderMenuItemPrefix, string.Empty, StringComparison.OrdinalIgnoreCase)
            : line.TastingMenuId.Trim();
        var menuItemId = string.IsNullOrWhiteSpace(line.ProductId)
            ? $"{TastingMenu.OrderMenuItemPrefix}{tastingId}"
            : line.ProductId.Trim();

        return new OrderItem
        {
            ClientItemId = string.IsNullOrWhiteSpace(line.Id) ? Guid.NewGuid().ToString("N") : line.Id.Trim(),
            MenuItemId = menuItemId,
            ItemName = string.IsNullOrWhiteSpace(line.Name) ? "Tasting Menu" : line.Name.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(line.Name) ? "Tasting Menu" : line.Name.Trim(),
            VariantId = string.IsNullOrWhiteSpace(line.VariantId) ? null : line.VariantId.Trim(),
            VariantName = string.IsNullOrWhiteSpace(line.VariantName) ? null : line.VariantName.Trim(),
            Quantity = Math.Max(1, line.Quantity),
            ItemPrice = Math.Max(0m, line.UnitPrice),
            SpecialInstructions = string.IsNullOrWhiteSpace(line.Notes) ? null : line.Notes.Trim(),
            Addons = new List<OrderItemAddon>()
        };
    }

    private static string? ResolveMealDealId(ClientOrderLineRequest line)
    {
        if (!string.IsNullOrWhiteSpace(line.MealDealId))
        {
            return line.MealDealId.Trim();
        }

        var productId = line.ProductId ?? string.Empty;
        if (productId.StartsWith(MealDeal.OrderMenuItemPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return productId[MealDeal.OrderMenuItemPrefix.Length..].Trim();
        }

        return null;
    }

    private static MenuItemVariant? ResolveVariant(FoodMenuItem? menuItem, ClientOrderLineRequest line)
    {
        if (menuItem?.Variants == null || menuItem.Variants.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(line.VariantId))
        {
            var byId = menuItem.Variants.FirstOrDefault(variant =>
                variant.Active &&
                string.Equals(variant.Id, line.VariantId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byId != null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(line.VariantName))
        {
            return menuItem.Variants.FirstOrDefault(variant =>
                variant.Active &&
                string.Equals(variant.Name, line.VariantName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return null;
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
            .Select(item =>
            {
                var mealDealId = item.MenuItemId != null &&
                                 item.MenuItemId.StartsWith(MealDeal.OrderMenuItemPrefix, StringComparison.OrdinalIgnoreCase)
                    ? item.MenuItemId[MealDeal.OrderMenuItemPrefix.Length..]
                    : null;
                var tastingMenuId = item.MenuItemId != null &&
                                    item.MenuItemId.StartsWith(TastingMenu.OrderMenuItemPrefix, StringComparison.OrdinalIgnoreCase) &&
                                    !item.MenuItemId.StartsWith(TastingMenu.OrderCourseItemPrefix, StringComparison.OrdinalIgnoreCase)
                    ? ResolveTastingMenuId(item.MenuItemId)
                    : null;
                var mealChoices = string.IsNullOrWhiteSpace(mealDealId) || string.IsNullOrWhiteSpace(item.SpecialInstructions)
                    ? Array.Empty<string>()
                    : item.SpecialInstructions
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(part => part.TrimStart('•', '-', ' ').Trim())
                        .Where(part => !string.IsNullOrWhiteSpace(part))
                        .ToArray();
                var lineNotes = string.IsNullOrWhiteSpace(mealDealId)
                    ? item.SpecialInstructions
                    : null;

                return new ClientOperationalOrderLine(
                    string.IsNullOrWhiteSpace(item.ClientItemId) ? item.Id.ToString() : item.ClientItemId,
                    item.MenuItemId,
                    string.IsNullOrWhiteSpace(item.DisplayName) ? item.ItemName : item.DisplayName,
                    item.Quantity,
                    (item.ItemPrice ?? 0m) + item.Addons.Sum(addon => addon.AddonPrice ?? 0m),
                    lineNotes,
                    item.Addons.Select(addon => addon.AddonName).Where(name => !string.IsNullOrWhiteSpace(name)).ToList()!,
                    item.VariantId,
                    item.VariantName,
                    mealDealId,
                    mealDealId == null ? null : mealChoices,
                    tastingMenuId,
                    IsLineSent(item.SendStatus),
                    item.SendStatus);
            })
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
            Math.Max(1, ComputeClientOrderVersion(order)),
            (order.UpdatedAt == default ? DateTime.Now : order.UpdatedAt).ToUniversalTime().ToString("O"),
            customer,
            order.SpecialInstructions,
            order.DiscountAmount,
            order.ServiceChargeAmount,
            order.ServiceChargeStatus,
            order.ServiceChargePercentage,
            order.LoyaltyPointsEarned);
    }

    private static string? ResolveTastingMenuId(string menuItemId)
    {
        // tasting:{menuId}:{optionId}
        var rest = menuItemId[TastingMenu.OrderMenuItemPrefix.Length..];
        var parts = rest.Split(':', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[0] : null;
    }

    private static bool IsLineSent(string? sendStatus)
    {
        var status = (sendStatus ?? string.Empty).Trim().ToLowerInvariant();
        return status is "sent" or "printed" or "preparing" or "ready" or "served" or "partial";
    }

    private static void PreserveLineKitchenState(OrderItem built, Order? existing)
    {
        if (existing == null || string.IsNullOrWhiteSpace(built.ClientItemId))
        {
            return;
        }

        var prior = existing.Items.FirstOrDefault(item =>
            string.Equals(item.ClientItemId, built.ClientItemId, StringComparison.OrdinalIgnoreCase));
        if (prior == null)
        {
            return;
        }

        built.SendStatus = prior.SendStatus;
        built.SentAt = prior.SentAt;
        built.PrintedAt = prior.PrintedAt;
        built.SendBatchId = prior.SendBatchId;
        built.FailureReason = prior.FailureReason;
    }

    private async Task ApplyDefaultTableServiceChargeAsync(Order order)
    {
        try
        {
            var settings = await _tableServiceChargeSettingsService.GetAsync();
            if (settings.IsEnabled && settings.Percentage > 0m)
            {
                order.ServiceChargeStatus = "applied";
                order.ServiceChargePercentage = settings.Percentage;
                order.ServiceChargeClassification = settings.Classification.ToString();
            }
            else
            {
                order.ServiceChargeStatus = "not_configured";
                order.ServiceChargePercentage = 0m;
            }
        }
        catch
        {
            order.ServiceChargeStatus = "not_configured";
            order.ServiceChargePercentage = 0m;
        }
    }

    private static void ApplyClientOrderFinancials(Order order, string orderType)
    {
        var foodSubtotal = order.Items.Sum(item => item.TotalPrice);
        order.SubtotalAmount = foodSubtotal;
        order.TaxAmount = 0m;
        order.DiscountAmount = Math.Max(0m, order.DiscountAmount);

        var isTable = string.Equals(orderType, "table", StringComparison.OrdinalIgnoreCase);
        var status = (order.ServiceChargeStatus ?? "not_configured").Trim().ToLowerInvariant();
        var isApplied = isTable && status == "applied";
        var percent = isTable ? Math.Max(0m, order.ServiceChargePercentage) : 0m;
        var calc = TableServiceChargeCalculator.Calculate(
            isTable ? TableServiceChargeCalculator.TableOrderMode : orderType,
            foodSubtotal,
            order.DiscountAmount,
            percent,
            isApplied);
        order.ServiceChargeBasis = calc.ChargeBasis;
        order.ServiceChargeAmount = calc.ServiceCharge;
        order.TotalAmount = calc.OrderTotal + Math.Max(0m, order.DeliveryFee);
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

    /// <summary>
    /// Same version token Clients receive on order payloads. Used for optimistic concurrency.
    /// </summary>
    public static int ComputeClientOrderVersion(Order order) =>
        CollectionOrderHubRules.ComputeOrderVersion(order.UpdatedAt, order.CreatedAt);

    private static bool HasOptimisticConcurrencyConflict(
        Order existing,
        int? expectedVersion,
        string? expectedUpdatedUtc)
    {
        if (expectedVersion is > 0)
        {
            return expectedVersion.Value != ComputeClientOrderVersion(existing);
        }

        if (string.IsNullOrWhiteSpace(expectedUpdatedUtc) ||
            !DateTimeOffset.TryParse(expectedUpdatedUtc, out var expected) ||
            existing.UpdatedAt == default)
        {
            // Legacy callers without version: allow (create path / older Clients).
            return false;
        }

        var current = new DateTimeOffset(
            DateTime.SpecifyKind(existing.UpdatedAt, DateTimeKind.Local)).ToUniversalTime();
        return Math.Abs((current - expected.ToUniversalTime()).TotalSeconds) > 1;
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
    IReadOnlyList<ClientMenuProductModifierDto> ProductModifiers,
    IReadOnlyList<ClientMenuVariantDto> Variants,
    IReadOnlyList<ClientMealDealDto> MealDeals,
    IReadOnlyList<ClientMealDealChoiceDto> MealDealChoices,
    IReadOnlyList<ClientMealDealCategoryRuleDto> MealDealCategoryRules,
    IReadOnlyList<ClientTastingMenuDto> TastingMenus,
    IReadOnlyList<ClientTastingMenuOptionDto> TastingMenuOptions,
    IReadOnlyList<ClientTastingMenuCourseDto> TastingMenuCourses,
    IReadOnlyList<ClientTastingMenuChoiceDto> TastingMenuChoices,
    IReadOnlyList<ClientMenuQuickNoteDto> QuickNotes);

public sealed record ClientMenuCategoryDto(int Id, string MotherId, string Name, string Color, int SortOrder, bool IsActive, string? ParentMotherId = null);

public sealed record ClientMenuProductDto(int Id, string MotherId, int CategoryId, string Name, string Description, string? Sku, bool IsActive);

public sealed record ClientMenuPriceDto(int Id, int ProductId, string PriceType, decimal Amount, string Currency, int? TaxRateId);

public sealed record ClientMenuModifierGroupDto(int Id, string MotherId, string Name, int MinSelect, int MaxSelect, bool IsActive);

public sealed record ClientMenuModifierDto(int Id, string MotherId, int ModifierGroupId, string Name, decimal PriceDelta, bool IsActive);

public sealed record ClientMenuProductModifierDto(int ProductId, int ModifierGroupId, int SortOrder);

public sealed record ClientMenuVariantDto(
    int Id,
    string MotherId,
    int ProductId,
    string Name,
    string? Description,
    decimal TakeawayPrice,
    decimal DineInPrice,
    int SortOrder,
    bool IsActive);

public sealed record ClientMenuQuickNoteDto(
    int Id,
    string MotherId,
    int ProductId,
    string NoteText,
    int SortOrder,
    bool IsActive);

public sealed record ClientMealDealDto(
    int Id,
    string MotherId,
    string Name,
    string? Description,
    decimal Price,
    string Color,
    int PickCount,
    string VatCategory,
    int SortOrder,
    bool IsActive);

public sealed record ClientMealDealChoiceDto(
    int Id,
    string MotherId,
    int MealDealId,
    string Name,
    int SortOrder);

public sealed record ClientMealDealCategoryRuleDto(
    int Id,
    string MotherId,
    int MealDealId,
    string Name,
    bool IsRequired,
    int MinSelections,
    int MaxSelections,
    IReadOnlyList<string> MenuItemMotherIds);

public sealed record ClientTastingMenuDto(
    int Id,
    string MotherId,
    string Name,
    string? Description,
    string Color,
    int SortOrder,
    bool IsActive);

public sealed record ClientTastingMenuOptionDto(
    int Id,
    string MotherId,
    int TastingMenuId,
    string Name,
    decimal Price,
    bool IncludesWine,
    int CourseCount,
    int SortOrder);

public sealed record ClientTastingMenuCourseDto(
    int Id,
    string MotherId,
    int TastingMenuId,
    string Name,
    string? WineName,
    int CourseNumber,
    bool Required,
    string VatCategory,
    int SortOrder);

public sealed record ClientTastingMenuChoiceDto(
    int Id,
    string MotherId,
    int CourseId,
    string Name,
    string? PrintGroupId,
    int SortOrder);

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
    IReadOnlyList<ClientOrderLineRequest>? Lines,
    int? ExpectedVersion = null,
    string? ExpectedUpdatedUtc = null,
    decimal? Discount = null);

public sealed record ClientOrderLineRequest(
    string? Id,
    string? ProductId,
    string Name,
    int Quantity,
    decimal UnitPrice,
    string? Notes,
    IReadOnlyList<string>? Modifiers,
    string? VariantId = null,
    string? VariantName = null,
    decimal? VariantPrice = null,
    string? MealDealId = null,
    IReadOnlyList<string>? MealDealChoices = null,
    string? TastingMenuId = null);

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
    string? ConflictMessage,
    string? Notes = null,
    decimal Discount = 0m,
    decimal ServiceCharge = 0m,
    string? ServiceChargeStatus = null,
    decimal ServiceChargePercent = 0m,
    int LoyaltyPointsEarned = 0);

public sealed record ClientOperationalOrderLine(
    string Id,
    string? ProductMotherId,
    string Name,
    int Quantity,
    decimal UnitPrice,
    string? Notes,
    IReadOnlyList<string> Modifiers,
    string? VariantId = null,
    string? VariantName = null,
    string? MealDealId = null,
    IReadOnlyList<string>? MealDealChoices = null,
    string? TastingMenuId = null,
    bool IsSent = false,
    string? SendStatus = null);

public sealed record ClientOrderUpsertResult(bool Success, int StatusCode, string Message, ClientOperationalOrder? Order)
{
    public static ClientOrderUpsertResult Ok(ClientOperationalOrder order) =>
        new(true, 200, "Order saved on Mother POS.", order);

    public static ClientOrderUpsertResult Ok(ClientOperationalOrder order, string message) =>
        new(true, 200, message, order);

    public static ClientOrderUpsertResult Conflict(ClientOperationalOrder order, string message) =>
        new(false, 409, message, order);

    public static ClientOrderUpsertResult Fail(int statusCode, string message) =>
        new(false, statusCode, message, null);
}

/// <summary>Order Place loyalty earn (add points for bill) result for Client API.</summary>
public sealed record ClientOrderLoyaltyAddResult(
    bool Success,
    int StatusCode,
    string Message,
    string? ErrorCode,
    ClientOperationalOrder? Order,
    decimal? BillTotal,
    int PointsAdded,
    int? PointsBalance,
    ClientLoyaltyCustomerDto? Customer)
{
    public static ClientOrderLoyaltyAddResult Ok(
        ClientOperationalOrder order,
        string message,
        decimal billTotal,
        int pointsAdded,
        int pointsBalance,
        ClientLoyaltyCustomerDto customer) =>
        new(true, 200, message, null, order, billTotal, pointsAdded, pointsBalance, customer);

    public static ClientOrderLoyaltyAddResult Fail(int statusCode, string message, string? errorCode) =>
        new(false, statusCode, message, errorCode, null, null, 0, null, null);
}
