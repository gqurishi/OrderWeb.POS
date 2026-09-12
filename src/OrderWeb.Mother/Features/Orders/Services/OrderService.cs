using MySqlConnector;
using POS_in_NET.Models;
using System.Text;
using System.Text.Json;

namespace POS_in_NET.Services;

public class OrderService
{
    private readonly string _connectionString;
    private readonly OnlineOrderApiService _apiService;
    private bool _orderTypeSchemaChecked;
    private bool _sourceChannelSchemaChecked;
    private bool _lifecycleSchemaChecked;
    private bool _financialSchemaChecked;

    public OrderService()
        : this(ServiceHelper.GetService<OnlineOrderApiService>() ?? new OnlineOrderApiService())
    {
    }

    public OrderService(OnlineOrderApiService apiService)
    {
        _connectionString = TerminalConfigurationService.GetPosConnectionString();
        _apiService = apiService;
    }

    public async Task<bool> TryClaimTastingCourseFireAsync(
        string orderId,
        string clientItemId,
        DateTime firedAt,
        string firedBy)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        const string sql = @"
            UPDATE order_items oi
            INNER JOIN orders o ON o.id = oi.order_id
            SET oi.fired_at = @firedAt,
                oi.fired_by = @firedBy
            WHERE o.order_id = @orderId
              AND oi.client_item_id = @clientItemId
              AND oi.menu_item_id LIKE 'tasting-course:%'
              AND oi.fired_at IS NULL";
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@firedAt", firedAt);
        command.Parameters.AddWithValue("@firedBy", firedBy);
        command.Parameters.AddWithValue("@orderId", orderId);
        command.Parameters.AddWithValue("@clientItemId", clientItemId);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    public async Task ReleaseTastingCourseFireClaimAsync(
        string orderId,
        string clientItemId,
        string firedBy)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        const string sql = @"
            UPDATE order_items oi
            INNER JOIN orders o ON o.id = oi.order_id
            SET oi.fired_at = NULL,
                oi.fired_by = NULL
            WHERE o.order_id = @orderId
              AND oi.client_item_id = @clientItemId
              AND oi.menu_item_id LIKE 'tasting-course:%'
              AND oi.fired_by = @firedBy";
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@orderId", orderId);
        command.Parameters.AddWithValue("@clientItemId", clientItemId);
        command.Parameters.AddWithValue("@firedBy", firedBy);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<(bool Success, string Message)> SaveOrderAsync(Order order)
    {
        try
        {
            if (!OrderPersistencePolicy.TryValidate(order, out var validationMessage))
            {
                return (false, validationMessage);
            }

            if (string.IsNullOrWhiteSpace(order.OrderId))
            {
                order.OrderId = Guid.NewGuid().ToString("N");
            }

            if (order.CreatedAt == default)
            {
                order.CreatedAt = DateTime.Now;
            }

            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureOrderTypeSchemaAsync(connection);
            await EnsureSourceChannelSchemaAsync(connection);
            await EnsureLifecycleSchemaAsync(connection);
            await EnsureFinancialSchemaAsync(connection);

            var normalizedOrderType = NormalizeOrderType(order.OrderType);
            order.OrderType = normalizedOrderType;
            var normalizedSourceChannel = NormalizeSourceChannel(order.SourceChannel);
            order.SourceChannel = normalizedSourceChannel;
            order.LocalLifecycleState = NormalizeLocalLifecycleState(order.LocalLifecycleState, order.Status);

            if (order.LocalLifecycleState == LocalLifecycleState.Paid || order.LocalLifecycleState == LocalLifecycleState.Voided)
            {
                order.IsOpen = false;
            }

            if (order.LocalLifecycleState == LocalLifecycleState.Paid || order.PaidAt.HasValue)
            {
                order.PaymentStatusRaw = "paid";
                order.AmountPaid ??= order.TotalAmount;
            }

            var existingOrder = await GetOrderByExternalIdAsync(order.OrderId);
            if (!string.IsNullOrWhiteSpace(existingOrder?.OrderNumber) && string.IsNullOrWhiteSpace(order.OrderNumber))
            {
                order.OrderNumber = existingOrder.OrderNumber;
            }

            // Check if order already exists
            if (existingOrder != null)
            {
                return await UpdateOrderAsync(order);
            }

            await using var transaction = await connection.BeginTransactionAsync();

            // Insert new order with all OrderWeb.net fields
            var orderQuery = @"
                INSERT INTO orders (order_id, order_number, cloud_order_id, 
                                  customer_name, customer_phone, customer_email, customer_address, 
                                  total_amount, subtotal_amount, discount_amount, delivery_fee,
                                  service_charge_percentage, service_charge_basis, service_charge_amount,
                                  service_charge_status, service_charge_classification,
                                  service_charge_removal_reason, service_charge_removed_by_user_id,
                                  service_charge_removed_by_name, service_charge_approved_by_user_id,
                                  service_charge_approved_by_name, service_charge_removed_at, tax_amount,
                                  cash_tip_amount, card_tip_amount,
                          order_type, source_channel, table_session_id, payment_method, payment_status, amount_paid,
                          payment_provider, payment_reference, payment_currency, voucher_code, promo_code,
                          gift_card_number_masked, gift_card_amount_paid, gift_card_remaining_balance,
                          loyalty_points_earned, loyalty_points_redeemed, loyalty_points_discount, loyalty_balance_after,
                          special_instructions, scheduled_time,
                          local_lifecycle_state, is_open, void_reason, voided_at, voided_by, paid_at,
                                  status, order_data, sync_status, 
                                  kitchen_time, preparing_time, ready_time, delivering_time, completed_time,
                                  updated_by_terminal_name, updated_by_terminal_at,
                                  created_at, updated_at) 
                VALUES (@orderId, @orderNumber, @cloudOrderId,
                        @customerName, @customerPhone, @customerEmail, @customerAddress, 
                        @totalAmount, @subtotalAmount, @discountAmount, @deliveryFee,
                        @serviceChargePercentage, @serviceChargeBasis, @serviceChargeAmount,
                        @serviceChargeStatus, @serviceChargeClassification,
                        @serviceChargeRemovalReason, @serviceChargeRemovedByUserId,
                        @serviceChargeRemovedByName, @serviceChargeApprovedByUserId,
                        @serviceChargeApprovedByName, @serviceChargeRemovedAt, @taxAmount,
                        @cashTipAmount, @cardTipAmount,
                    @orderType, @sourceChannel, @tableSessionId, @paymentMethod, @paymentStatus, @amountPaid,
                    @paymentProvider, @paymentReference, @paymentCurrency, @voucherCode, @promoCode,
                    @giftCardNumberMasked, @giftCardAmountPaid, @giftCardRemainingBalance,
                    @loyaltyPointsEarned, @loyaltyPointsRedeemed, @loyaltyPointsDiscount, @loyaltyBalanceAfter,
                    @specialInstructions, @scheduledTime,
                    @localLifecycleState, @isOpen, @voidReason, @voidedAt, @voidedBy, @paidAt,
                        @status, @orderData, @syncStatus,
                        @kitchenTime, @preparingTime, @readyTime, @deliveringTime, @completedTime,
                        @updatedByTerminalName, @updatedByTerminalAt,
                        @createdAt, @updatedAt)";

            using var orderCommand = new MySqlCommand(orderQuery, connection, transaction);
            var now = NormalizeTimestampForDb(DateTime.Now);
            orderCommand.Parameters.AddWithValue("@orderId", order.OrderId);
            orderCommand.Parameters.AddWithValue("@orderNumber", order.OrderNumber ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@cloudOrderId", order.CloudOrderId ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@customerName", order.CustomerName);
            orderCommand.Parameters.AddWithValue("@customerPhone", order.CustomerPhone ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@customerEmail", order.CustomerEmail ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@customerAddress", order.CustomerAddress ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@totalAmount", order.TotalAmount);
            orderCommand.Parameters.AddWithValue("@subtotalAmount", order.SubtotalAmount);
            orderCommand.Parameters.AddWithValue("@discountAmount", order.DiscountAmount);
            orderCommand.Parameters.AddWithValue("@deliveryFee", order.DeliveryFee);
            AddServiceChargeParameters(orderCommand, order);
            orderCommand.Parameters.AddWithValue("@taxAmount", order.TaxAmount);
            orderCommand.Parameters.AddWithValue("@cashTipAmount", order.CashTipAmount);
            orderCommand.Parameters.AddWithValue("@cardTipAmount", order.CardTipAmount);
            orderCommand.Parameters.AddWithValue("@orderType", normalizedOrderType);
            orderCommand.Parameters.AddWithValue("@sourceChannel", normalizedSourceChannel);
            orderCommand.Parameters.AddWithValue("@tableSessionId", order.TableSessionId ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@paymentMethod", order.PaymentMethod ?? (object)DBNull.Value);
            AddCloudPaymentParameters(orderCommand, order);
            orderCommand.Parameters.AddWithValue("@specialInstructions", order.SpecialInstructions ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@scheduledTime", order.ScheduledTime ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@localLifecycleState", ToDbLifecycleState(order.LocalLifecycleState));
            orderCommand.Parameters.AddWithValue("@isOpen", order.IsOpen);
            orderCommand.Parameters.AddWithValue("@voidReason", order.VoidReason ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@voidedAt", order.VoidedAt ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@voidedBy", order.VoidedBy ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@paidAt", order.PaidAt ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@status", order.Status.ToString().ToLower());
            orderCommand.Parameters.AddWithValue("@orderData", order.OrderData ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@syncStatus", order.SyncStatus.ToString().ToLower());
            orderCommand.Parameters.AddWithValue("@kitchenTime", order.KitchenTime ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@preparingTime", order.PreparingTime ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@readyTime", order.ReadyTime ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@deliveringTime", order.DeliveringTime ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@completedTime", order.CompletedTime ?? (object)DBNull.Value);
            orderCommand.Parameters.AddWithValue("@updatedByTerminalName", GetCurrentTerminalName());
            orderCommand.Parameters.AddWithValue("@updatedByTerminalAt", now);
            orderCommand.Parameters.AddWithValue("@createdAt", order.CreatedAt);
            orderCommand.Parameters.AddWithValue("@updatedAt", now);

            await orderCommand.ExecuteNonQueryAsync();

            // Get the inserted order ID
            var getIdQuery = "SELECT LAST_INSERT_ID()";
            using var idCommand = new MySqlCommand(getIdQuery, connection, transaction);
            var insertedId = Convert.ToInt32(await idCommand.ExecuteScalarAsync());
            order.Id = insertedId;
            order.UpdatedAt = now;

            await InsertOrderEventAsync(
                connection,
                insertedId,
                "created",
                "system",
                null,
                null,
                order.CreatedAt == default ? DateTime.Now : order.CreatedAt,
                JsonSerializer.Serialize(new
                {
                    lifecycle = ToDbLifecycleState(order.LocalLifecycleState),
                    sourceChannel = order.SourceChannel,
                    orderType = order.OrderType
                }),
                transaction);

            // Insert order items
            foreach (var item in order.Items)
            {
                await SaveOrderItemAsync(connection, insertedId, item, transaction);
            }

            await transaction.CommitAsync();
            try
            {
                await UpdateOrderOperationalSummaryAsync(connection, insertedId);
                UpdateActiveTableCache(order);
                await PublishOrderTerminalEventAsync(connection, order, "created");
            }
            catch (Exception postCommitException)
            {
                // The permanent order and all lines are already committed. A
                // notification/metrics failure must not report the save as failed
                // and encourage a duplicate retry.
                AppDiagnostics.Log($"Order {order.OrderId} saved; post-commit notification warning: {postCommitException.Message}");
            }

            return (true, "Order saved successfully");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to save order: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> UpdateOrderAsync(Order order)
    {
        try
        {
            if (!OrderPersistencePolicy.TryValidate(order, out var validationMessage))
            {
                return (false, validationMessage);
            }

            if (string.IsNullOrWhiteSpace(order.OrderId))
            {
                return (false, "Order ID missing");
            }

            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureOrderTypeSchemaAsync(connection);
            await EnsureSourceChannelSchemaAsync(connection);
            await EnsureLifecycleSchemaAsync(connection);

            var normalizedOrderType = NormalizeOrderType(order.OrderType);
            order.OrderType = normalizedOrderType;
            var normalizedSourceChannel = NormalizeSourceChannel(order.SourceChannel);
            order.SourceChannel = normalizedSourceChannel;
            order.LocalLifecycleState = NormalizeLocalLifecycleState(order.LocalLifecycleState, order.Status);

            if (order.LocalLifecycleState == LocalLifecycleState.Paid || order.PaidAt.HasValue)
            {
                order.PaymentStatusRaw = "paid";
                order.AmountPaid ??= order.TotalAmount;
            }

            if (order.LocalLifecycleState == LocalLifecycleState.Paid || order.LocalLifecycleState == LocalLifecycleState.Voided)
            {
                order.IsOpen = false;
            }

            var existingOrder = await GetOrderByExternalIdAsync(order.OrderId);
            if (existingOrder != null && IsTerminalLifecycleState(existingOrder.LocalLifecycleState))
            {
                return (false, "Terminal orders are immutable");
            }

            if (!string.IsNullOrWhiteSpace(existingOrder?.OrderNumber) && string.IsNullOrWhiteSpace(order.OrderNumber))
            {
                order.OrderNumber = existingOrder.OrderNumber;
            }

            var expectedUpdatedAt = order.ExpectedUpdatedAt;
            if (!expectedUpdatedAt.HasValue && existingOrder != null && order.UpdatedAt != default)
            {
                expectedUpdatedAt = order.UpdatedAt;
            }
            if (expectedUpdatedAt.HasValue)
            {
                expectedUpdatedAt = NormalizeTimestampForDb(expectedUpdatedAt.Value);
            }

            var updateQuery = @"
                UPDATE orders SET 
                    order_number = @orderNumber,
                    cloud_order_id = COALESCE(NULLIF(@cloudOrderId, ''), cloud_order_id),
                    customer_name = @customerName,
                    customer_phone = @customerPhone,
                    customer_email = COALESCE(NULLIF(@customerEmail, ''), customer_email),
                    customer_address = @customerAddress,
                    total_amount = @totalAmount,
                    subtotal_amount = @subtotalAmount,
                    discount_amount = @discountAmount,
                    delivery_fee = @deliveryFee,
                    service_charge_percentage = @serviceChargePercentage,
                    service_charge_basis = @serviceChargeBasis,
                    service_charge_amount = @serviceChargeAmount,
                    service_charge_status = @serviceChargeStatus,
                    service_charge_classification = @serviceChargeClassification,
                    service_charge_removal_reason = @serviceChargeRemovalReason,
                    service_charge_removed_by_user_id = @serviceChargeRemovedByUserId,
                    service_charge_removed_by_name = @serviceChargeRemovedByName,
                    service_charge_approved_by_user_id = @serviceChargeApprovedByUserId,
                    service_charge_approved_by_name = @serviceChargeApprovedByName,
                    service_charge_removed_at = @serviceChargeRemovedAt,
                    tax_amount = @taxAmount,
                    cash_tip_amount = @cashTipAmount,
                    card_tip_amount = @cardTipAmount,
                    order_type = @orderType,
                    source_channel = @sourceChannel,
                    table_session_id = @tableSessionId,
                    payment_method = @paymentMethod,
                    payment_status = @paymentStatus,
                    amount_paid = @amountPaid,
                    payment_provider = @paymentProvider,
                    payment_reference = @paymentReference,
                    payment_currency = @paymentCurrency,
                    voucher_code = @voucherCode,
                    promo_code = @promoCode,
                    gift_card_number_masked = @giftCardNumberMasked,
                    gift_card_amount_paid = @giftCardAmountPaid,
                    gift_card_remaining_balance = @giftCardRemainingBalance,
                    loyalty_points_earned = @loyaltyPointsEarned,
                    loyalty_points_redeemed = @loyaltyPointsRedeemed,
                    loyalty_points_discount = @loyaltyPointsDiscount,
                    loyalty_balance_after = @loyaltyBalanceAfter,
                    special_instructions = @specialInstructions,
                    scheduled_time = @scheduledTime,
                    local_lifecycle_state = @localLifecycleState,
                    is_open = @isOpen,
                    void_reason = @voidReason,
                    voided_at = @voidedAt,
                    voided_by = @voidedBy,
                    paid_at = @paidAt,
                    status = @status,
                    order_data = @orderData,
                    sync_status = @syncStatus,
                    kitchen_time = @kitchenTime,
                    preparing_time = @preparingTime,
                    ready_time = @readyTime,
                    delivering_time = @deliveringTime,
                    completed_time = @completedTime,
                    updated_by_terminal_name = @updatedByTerminalName,
                    updated_by_terminal_at = @updatedByTerminalAt,
                    updated_at = @updatedAt
                WHERE order_id = @orderId";
            if (expectedUpdatedAt.HasValue)
            {
                updateQuery += " AND updated_at = @expectedUpdatedAt";
            }

            using var command = new MySqlCommand(updateQuery, connection);
            var now = NormalizeTimestampForDb(DateTime.Now);
            command.Parameters.AddWithValue("@orderNumber", order.OrderNumber ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@cloudOrderId", order.CloudOrderId ?? string.Empty);
            command.Parameters.AddWithValue("@customerName", order.CustomerName);
            command.Parameters.AddWithValue("@customerPhone", order.CustomerPhone ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@customerEmail", order.CustomerEmail ?? string.Empty);
            command.Parameters.AddWithValue("@customerAddress", order.CustomerAddress ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@totalAmount", order.TotalAmount);
            command.Parameters.AddWithValue("@subtotalAmount", order.SubtotalAmount);
            command.Parameters.AddWithValue("@discountAmount", order.DiscountAmount);
            command.Parameters.AddWithValue("@deliveryFee", order.DeliveryFee);
            AddServiceChargeParameters(command, order);
            command.Parameters.AddWithValue("@taxAmount", order.TaxAmount);
            command.Parameters.AddWithValue("@cashTipAmount", order.CashTipAmount);
            command.Parameters.AddWithValue("@cardTipAmount", order.CardTipAmount);
            command.Parameters.AddWithValue("@orderType", normalizedOrderType);
            command.Parameters.AddWithValue("@sourceChannel", normalizedSourceChannel);
            command.Parameters.AddWithValue("@tableSessionId", order.TableSessionId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@paymentMethod", order.PaymentMethod ?? (object)DBNull.Value);
            AddCloudPaymentParameters(command, order);
            command.Parameters.AddWithValue("@specialInstructions", order.SpecialInstructions ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@scheduledTime", order.ScheduledTime ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@localLifecycleState", ToDbLifecycleState(order.LocalLifecycleState));
            command.Parameters.AddWithValue("@isOpen", order.IsOpen);
            command.Parameters.AddWithValue("@voidReason", order.VoidReason ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@voidedAt", order.VoidedAt ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@voidedBy", order.VoidedBy ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@paidAt", order.PaidAt ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@status", order.Status.ToString().ToLower());
            command.Parameters.AddWithValue("@orderData", order.OrderData ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@syncStatus", order.SyncStatus.ToString().ToLower());
            command.Parameters.AddWithValue("@kitchenTime", order.KitchenTime ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@preparingTime", order.PreparingTime ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@readyTime", order.ReadyTime ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@deliveringTime", order.DeliveringTime ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@completedTime", order.CompletedTime ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@updatedByTerminalName", GetCurrentTerminalName());
            command.Parameters.AddWithValue("@updatedByTerminalAt", now);
            command.Parameters.AddWithValue("@updatedAt", now);
            command.Parameters.AddWithValue("@orderId", order.OrderId);
            if (expectedUpdatedAt.HasValue)
            {
                command.Parameters.AddWithValue("@expectedUpdatedAt", expectedUpdatedAt.Value);
            }

            var rowsAffected = await command.ExecuteNonQueryAsync();
            if (rowsAffected == 0)
            {
                var latestOrder = await GetOrderByExternalIdAsync(order.OrderId);
                if (latestOrder == null)
                {
                    return (false, "Order not found for update");
                }

                if (expectedUpdatedAt.HasValue && latestOrder.UpdatedAt != expectedUpdatedAt.Value)
                {
                    return (false, "Order changed on another terminal, reload.");
                }

                return (false, "Order update conflict, please reload.");
            }

            if (existingOrder != null)
            {
                await SyncOrderItemsAsync(connection, existingOrder.Id, order.Items);
                await UpdateOrderOperationalSummaryAsync(connection, existingOrder.Id);
            }

            order.UpdatedAt = now;
            order.ExpectedUpdatedAt = now;
            UpdateActiveTableCache(order);
            await PublishOrderTerminalEventAsync(connection, order, "updated");

            return (true, "Order updated successfully");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to update order: {ex.Message}");
        }
    }

    /// <summary>
    /// After cloud loyalty-add succeeds, stamp the order as earned without full-order
    /// optimistic concurrency (which breaks if UpdatedAt was rewritten to "now").
    /// Idempotent when <c>loyalty_points_earned</c> is already &gt; 0.
    /// </summary>
    public async Task<(bool Success, string Message)> MarkLoyaltyPointsEarnedAsync(
        string orderId,
        int pointsEarned,
        string? customerName = null,
        string? customerPhone = null,
        int? loyaltyBalanceAfter = null)
    {
        if (string.IsNullOrWhiteSpace(orderId) || pointsEarned <= 0)
        {
            return (false, "Order id and points earned are required.");
        }

        try
        {
            await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureFinancialSchemaAsync(connection);

            var now = NormalizeTimestampForDb(DateTime.Now);
            const string sql = @"
                UPDATE orders SET
                    loyalty_points_earned = CASE
                        WHEN COALESCE(loyalty_points_earned, 0) > 0 THEN loyalty_points_earned
                        ELSE @pointsEarned
                    END,
                    customer_name = CASE
                        WHEN NULLIF(@customerName, '') IS NULL THEN customer_name
                        ELSE @customerName
                    END,
                    customer_phone = CASE
                        WHEN NULLIF(@customerPhone, '') IS NULL THEN customer_phone
                        ELSE @customerPhone
                    END,
                    loyalty_balance_after = COALESCE(@loyaltyBalanceAfter, loyalty_balance_after),
                    updated_at = @updatedAt
                WHERE order_id = @orderId";

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@pointsEarned", pointsEarned);
            command.Parameters.AddWithValue("@customerName", customerName?.Trim() ?? string.Empty);
            command.Parameters.AddWithValue("@customerPhone", customerPhone?.Trim() ?? string.Empty);
            command.Parameters.AddWithValue("@loyaltyBalanceAfter", loyaltyBalanceAfter ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@updatedAt", now);
            command.Parameters.AddWithValue("@orderId", orderId.Trim());

            var rows = await command.ExecuteNonQueryAsync();
            var latest = await GetOrderByExternalIdAsync(orderId.Trim());
            if (latest != null && latest.LoyaltyPointsEarned > 0)
            {
                return (true, rows > 0 ? "Loyalty earn marked on order." : "Loyalty earn already marked on order.");
            }

            return (false, rows == 0 ? "Order not found for loyalty earn mark." : "Loyalty earn mark did not persist.");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to mark loyalty earn: {ex.Message}");
        }
    }

    public async Task<Order?> GetOrderByExternalIdAsync(string externalOrderId)
    {
        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureSourceChannelSchemaAsync(connection);
            await EnsureLifecycleSchemaAsync(connection);

            var query = @"SELECT * FROM orders WHERE order_id = @orderId";
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@orderId", externalOrderId);

            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var order = MapOrderFromReader((MySqlDataReader)reader);
                await reader.CloseAsync();
                
                // Load order items
                order.Items = await GetOrderItemsAsync(connection, order.Id);
                
                return order;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting order: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Enrich an existing OrderWeb order without changing its local lifecycle,
    /// kitchen state, payment attempts, or operator edits.
    /// </summary>
    public async Task<(bool Success, string Message)> EnrichCloudOrderAsync(Order incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming.OrderId))
        {
            return (false, "Cloud order ID missing");
        }

        try
        {
            await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            const string sql = @"
                UPDATE orders
                SET order_number = COALESCE(NULLIF(@orderNumber, ''), order_number),
                    cloud_order_id = COALESCE(NULLIF(@cloudOrderId, ''), cloud_order_id),
                    customer_name = COALESCE(NULLIF(@customerName, ''), customer_name),
                    customer_phone = COALESCE(NULLIF(@customerPhone, ''), customer_phone),
                    customer_email = COALESCE(NULLIF(@customerEmail, ''), customer_email),
                    customer_address = COALESCE(NULLIF(@customerAddress, ''), customer_address),
                    total_amount = CASE WHEN @totalAmount > 0 THEN @totalAmount ELSE total_amount END,
                    subtotal_amount = CASE WHEN @subtotalAmount > 0 THEN @subtotalAmount ELSE subtotal_amount END,
                    discount_amount = CASE WHEN @discountAmount > 0 OR discount_amount = 0 THEN @discountAmount ELSE discount_amount END,
                    delivery_fee = CASE WHEN @deliveryFee > 0 OR delivery_fee = 0 THEN @deliveryFee ELSE delivery_fee END,
                    service_charge_percentage = CASE WHEN @serviceChargePercentage > 0 THEN @serviceChargePercentage ELSE service_charge_percentage END,
                    service_charge_basis = CASE WHEN @serviceChargeBasis > 0 THEN @serviceChargeBasis ELSE service_charge_basis END,
                    service_charge_amount = CASE WHEN @serviceChargeAmount > 0 OR service_charge_amount = 0 THEN @serviceChargeAmount ELSE service_charge_amount END,
                    service_charge_status = CASE WHEN @serviceChargeStatus <> 'not_configured' THEN @serviceChargeStatus ELSE service_charge_status END,
                    tax_amount = CASE WHEN @taxAmount > 0 OR tax_amount = 0 THEN @taxAmount ELSE tax_amount END,
                    cash_tip_amount = CASE WHEN @cashTipAmount > 0 OR cash_tip_amount = 0 THEN @cashTipAmount ELSE cash_tip_amount END,
                    card_tip_amount = CASE WHEN @cardTipAmount > 0 OR card_tip_amount = 0 THEN @cardTipAmount ELSE card_tip_amount END,
                    order_type = COALESCE(NULLIF(@orderType, ''), order_type),
                    payment_method = COALESCE(NULLIF(@paymentMethod, ''), payment_method),
                    payment_status = CASE
                        WHEN NULLIF(@paymentStatus, '') IS NULL THEN payment_status
                        WHEN LOWER(COALESCE(payment_status, '')) IN ('paid', 'complete', 'completed', 'captured', 'settled', 'success', 'succeeded')
                             AND LOWER(@paymentStatus) IN ('pending', 'awaiting', 'processing', 'unpaid')
                            THEN payment_status
                        ELSE @paymentStatus
                    END,
                    amount_paid = COALESCE(@amountPaid, amount_paid),
                    payment_provider = COALESCE(NULLIF(@paymentProvider, ''), payment_provider),
                    payment_reference = COALESCE(NULLIF(@paymentReference, ''), payment_reference),
                    payment_currency = COALESCE(NULLIF(@paymentCurrency, ''), payment_currency),
                    voucher_code = COALESCE(NULLIF(@voucherCode, ''), voucher_code),
                    promo_code = COALESCE(NULLIF(@promoCode, ''), promo_code),
                    gift_card_number_masked = COALESCE(NULLIF(@giftCardNumberMasked, ''), gift_card_number_masked),
                    gift_card_amount_paid = COALESCE(@giftCardAmountPaid, gift_card_amount_paid),
                    gift_card_remaining_balance = COALESCE(@giftCardRemainingBalance, gift_card_remaining_balance),
                    loyalty_points_earned = CASE WHEN @loyaltyPointsEarned > 0 THEN @loyaltyPointsEarned ELSE loyalty_points_earned END,
                    loyalty_points_redeemed = CASE WHEN @loyaltyPointsRedeemed > 0 THEN @loyaltyPointsRedeemed ELSE loyalty_points_redeemed END,
                    loyalty_points_discount = CASE WHEN @loyaltyPointsDiscount > 0 THEN @loyaltyPointsDiscount ELSE loyalty_points_discount END,
                    loyalty_balance_after = COALESCE(@loyaltyBalanceAfter, loyalty_balance_after),
                    special_instructions = COALESCE(NULLIF(@specialInstructions, ''), special_instructions),
                    scheduled_time = COALESCE(@scheduledTime, scheduled_time),
                    order_data = CASE
                        WHEN CHAR_LENGTH(COALESCE(@orderData, '')) > CHAR_LENGTH(COALESCE(order_data, ''))
                            THEN @orderData
                        ELSE order_data
                    END,
                    updated_at = CURRENT_TIMESTAMP
                WHERE (order_id = @orderId OR cloud_order_id = @orderId)
                  AND LOWER(COALESCE(source_channel, 'web')) = 'web'";

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@orderId", incoming.OrderId);
            command.Parameters.AddWithValue("@orderNumber", incoming.OrderNumber ?? string.Empty);
            command.Parameters.AddWithValue("@cloudOrderId", incoming.CloudOrderId ?? string.Empty);
            command.Parameters.AddWithValue("@customerName", incoming.CustomerName ?? string.Empty);
            command.Parameters.AddWithValue("@customerPhone", incoming.CustomerPhone ?? string.Empty);
            command.Parameters.AddWithValue("@customerEmail", incoming.CustomerEmail ?? string.Empty);
            command.Parameters.AddWithValue("@customerAddress", incoming.CustomerAddress ?? string.Empty);
            command.Parameters.AddWithValue("@totalAmount", incoming.TotalAmount);
            command.Parameters.AddWithValue("@subtotalAmount", incoming.SubtotalAmount);
            command.Parameters.AddWithValue("@discountAmount", incoming.DiscountAmount);
            command.Parameters.AddWithValue("@deliveryFee", incoming.DeliveryFee);
            command.Parameters.AddWithValue("@serviceChargePercentage", incoming.ServiceChargePercentage);
            command.Parameters.AddWithValue("@serviceChargeBasis", incoming.ServiceChargeBasis);
            command.Parameters.AddWithValue("@serviceChargeAmount", incoming.ServiceChargeAmount);
            command.Parameters.AddWithValue("@serviceChargeStatus", incoming.ServiceChargeStatus ?? "not_configured");
            command.Parameters.AddWithValue("@taxAmount", incoming.TaxAmount);
            command.Parameters.AddWithValue("@cashTipAmount", incoming.CashTipAmount);
            command.Parameters.AddWithValue("@cardTipAmount", incoming.CardTipAmount);
            command.Parameters.AddWithValue("@orderType", incoming.OrderType ?? string.Empty);
            command.Parameters.AddWithValue("@paymentMethod", incoming.PaymentMethod ?? string.Empty);
            AddCloudPaymentParameters(command, incoming);
            command.Parameters.AddWithValue("@specialInstructions", incoming.SpecialInstructions ?? string.Empty);
            command.Parameters.AddWithValue("@scheduledTime", incoming.ScheduledTime ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@orderData", incoming.OrderData ?? string.Empty);

            var rows = await command.ExecuteNonQueryAsync();
            if (rows > 0 && incoming.Items.Count > 0)
            {
                const string itemCountSql = @"
                    SELECT o.id, COUNT(oi.id)
                    FROM orders o
                    LEFT JOIN order_items oi ON oi.order_id = o.id
                    WHERE (o.order_id = @orderId OR o.cloud_order_id = @orderId)
                    GROUP BY o.id
                    LIMIT 1";
                await using var itemCountCommand = new MySqlCommand(itemCountSql, connection);
                itemCountCommand.Parameters.AddWithValue("@orderId", incoming.OrderId);
                await using var reader = await itemCountCommand.ExecuteReaderAsync();
                var databaseOrderId = 0;
                var itemCount = 0;
                if (await reader.ReadAsync())
                {
                    databaseOrderId = reader.GetInt32(0);
                    itemCount = reader.GetInt32(1);
                }
                await reader.CloseAsync();

                if (databaseOrderId > 0 && itemCount == 0)
                {
                    foreach (var item in incoming.Items)
                    {
                        await SaveOrderItemAsync(connection, databaseOrderId, item);
                    }
                }
            }

            return rows > 0
                ? (true, "Cloud order details enriched")
                : (false, "Cloud order not found for enrichment");
        }
        catch (Exception ex)
        {
            return (false, $"Cloud order enrichment failed: {ex.Message}");
        }
    }

    public async Task<Order?> GetOrderByDatabaseIdAsync(int orderDbId)
    {
        if (orderDbId <= 0)
        {
            return null;
        }

        await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        return await GetOrderByIdAsync(connection, orderDbId);
    }

    public async Task<List<CustomerPreviousOrder>> GetPreviousCustomerOrdersAsync(
        string customerPhone,
        int maximumOrders = 3,
        int monthsBack = 12)
    {
        var normalizedPhone = OrderWebCustomerCloudService.NormalizePhone(customerPhone);
        if (string.IsNullOrWhiteSpace(normalizedPhone) || maximumOrders <= 0 || monthsBack <= 0)
        {
            return new List<CustomerPreviousOrder>();
        }

        var suffixLength = Math.Min(9, normalizedPhone.Length);
        var phoneSuffix = normalizedPhone[^suffixLength..];
        var cutoff = DateTime.Today.AddMonths(-monthsBack);
        var candidates = new List<(Order Order, decimal RefundAmount)>();

        await using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        await EnsureOrderTypeSchemaAsync(connection);
        await EnsureSourceChannelSchemaAsync(connection);
        await EnsureLifecycleSchemaAsync(connection);
        await EnsureFinancialSchemaAsync(connection);

        const string normalizedPhoneSql = """
            REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                COALESCE(o.customer_phone, ''), ' ', ''), '-', ''), '(', ''), ')', ''), '+', ''), '.', '')
            """;
        var sql = $"""
            SELECT o.*,
                   COALESCE((
                       SELECT SUM(r.refund_amount)
                       FROM order_refunds r
                       WHERE r.order_id = o.id
                   ), 0) AS refund_total
            FROM orders o
            WHERE o.created_at >= @cutoff
              AND LOWER(COALESCE(o.order_type, '')) IN ('pickup', 'collection', 'col', 'takeaway', 'delivery', 'del')
              AND LOWER(COALESCE(o.status, 'new')) <> 'cancelled'
              AND LOWER(COALESCE(o.local_lifecycle_state, 'draft')) <> 'voided'
              AND COALESCE(o.draft_abandoned_flag, 0) = 0
              AND (
                    LOWER(COALESCE(o.local_lifecycle_state, '')) IN ('sent_partial', 'sent_full', 'payment_partial', 'paid')
                    OR LOWER(COALESCE(o.status, '')) IN ('kitchen', 'preparing', 'ready', 'delivering', 'completed')
                  )
              AND RIGHT({normalizedPhoneSql}, @suffixLength) = @phoneSuffix
              AND EXISTS (SELECT 1 FROM order_items oi WHERE oi.order_id = o.id)
            ORDER BY o.created_at DESC, o.id DESC
            LIMIT @candidateLimit
            """;

        await using (var command = new MySqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@cutoff", cutoff);
            command.Parameters.AddWithValue("@suffixLength", suffixLength);
            command.Parameters.AddWithValue("@phoneSuffix", phoneSuffix);
            command.Parameters.AddWithValue("@candidateLimit", Math.Max(maximumOrders * 10, 30));

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var order = MapOrderFromReader(reader);
                var storedPhone = OrderWebCustomerCloudService.NormalizePhone(order.CustomerPhone);
                if (!string.Equals(storedPhone, normalizedPhone, StringComparison.Ordinal))
                {
                    continue;
                }

                var refundAmount = reader["refund_total"] == DBNull.Value
                    ? 0m
                    : Convert.ToDecimal(reader["refund_total"]);
                candidates.Add((order, refundAmount));

                if (candidates.Count >= maximumOrders)
                {
                    break;
                }
            }
        }

        var result = new List<CustomerPreviousOrder>(candidates.Count);
        foreach (var candidate in candidates)
        {
            candidate.Order.Items = await GetOrderItemsAsync(connection, candidate.Order.Id);
            if (candidate.Order.Items.Count == 0)
            {
                continue;
            }

            result.Add(new CustomerPreviousOrder
            {
                OrderDatabaseId = candidate.Order.Id,
                OrderNumber = candidate.Order.OrderNumber,
                CreatedAt = candidate.Order.CreatedAt,
                OrderType = candidate.Order.OrderType ?? string.Empty,
                TotalAmount = candidate.Order.TotalAmount,
                Status = candidate.Order.Status.ToString(),
                LocalLifecycleState = candidate.Order.LocalLifecycleState.ToString(),
                RefundAmount = candidate.RefundAmount,
                OrderNotes = candidate.Order.SpecialInstructions,
                ItemsText = BuildPreviousOrderItemsText(candidate.Order.Items)
            });
        }

        if (result.Count > 0)
        {
            result[0].IsMostRecent = true;
        }

        return result;
    }

    private static string BuildPreviousOrderItemsText(IReadOnlyList<OrderItem> items)
    {
        var builder = new StringBuilder();
        foreach (var item in items)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            var itemName = !string.IsNullOrWhiteSpace(item.DisplayName) ? item.DisplayName : item.ItemName;
            builder.Append(item.Quantity).Append(" × ").Append(itemName);

            if (!string.IsNullOrWhiteSpace(item.VariantName)
                && !itemName.Contains(item.VariantName, StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(" — ").Append(item.VariantName);
            }

            if (item.Addons.Count > 0)
            {
                builder.AppendLine();
                builder.Append("   Extras: ");
                builder.Append(string.Join(", ", item.Addons.Select(addon =>
                    addon.Quantity > 1 ? $"{addon.Quantity} × {addon.AddonName}" : addon.AddonName)));
            }

            if (!string.IsNullOrWhiteSpace(item.SpecialInstructions))
            {
                builder.AppendLine();
                builder.Append("   Note: ").Append(item.SpecialInstructions.Trim());
            }
        }

        return builder.ToString();
    }

    public async Task<Order?> GetOpenOrderByTableSessionIdAsync(int tableSessionId)
    {
        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureSourceChannelSchemaAsync(connection);
            await EnsureLifecycleSchemaAsync(connection);

            const string query = @"
                SELECT * FROM orders
                WHERE table_session_id = @tableSessionId
                  AND COALESCE(is_open, 1) = 1
                  AND COALESCE(local_lifecycle_state, 'active') NOT IN ('paid', 'voided')
                ORDER BY updated_at DESC, id DESC
                LIMIT 1";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@tableSessionId", tableSessionId);

            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var order = MapOrderFromReader(reader);
                await reader.CloseAsync();
                order.Items = await GetOrderItemsAsync(connection, order.Id);
                return order;
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting order by table session: {ex.Message}");
            return null;
        }
    }

    public async Task<Order?> GetLatestTableOrderBySessionIdAsync(int tableSessionId)
    {
        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureSourceChannelSchemaAsync(connection);
            await EnsureLifecycleSchemaAsync(connection);

            const string query = @"
                SELECT * FROM orders
                WHERE table_session_id = @tableSessionId
                  AND COALESCE(source_channel, 'local') = 'local'
                  AND COALESCE(order_type, 'table') = 'table'
                  AND COALESCE(is_open, 1) = 1
                  AND COALESCE(local_lifecycle_state, 'active') NOT IN ('paid', 'voided')
                ORDER BY updated_at DESC, id DESC
                LIMIT 1";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@tableSessionId", tableSessionId);

            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var order = MapOrderFromReader(reader);
                await reader.CloseAsync();
                order.Items = await GetOrderItemsAsync(connection, order.Id);
                return order;
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting latest table order by session: {ex.Message}");
            return null;
        }
    }

    public async Task<Order?> GetLatestOpenTableOrderByTableNumberAsync(string tableNumber)
    {
        if (string.IsNullOrWhiteSpace(tableNumber))
        {
            return null;
        }

        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureSourceChannelSchemaAsync(connection);
            await EnsureLifecycleSchemaAsync(connection);

            const string query = @"
                SELECT * FROM orders
                WHERE COALESCE(source_channel, 'local') = 'local'
                  AND COALESCE(order_type, 'table') = 'table'
                  AND customer_name = @customerName
                  AND COALESCE(is_open, 1) = 1
                  AND COALESCE(local_lifecycle_state, 'active') NOT IN ('paid', 'voided')
                ORDER BY updated_at DESC, id DESC
                LIMIT 1";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@customerName", $"Table {tableNumber.Trim()}");

            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var order = MapOrderFromReader(reader);
                await reader.CloseAsync();
                order.Items = await GetOrderItemsAsync(connection, order.Id);
                return order;
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting open table order by table number: {ex.Message}");
            return null;
        }
    }

    public async Task<Order?> GetLatestTableOrderByTableNumberAsync(string tableNumber)
    {
        if (string.IsNullOrWhiteSpace(tableNumber))
        {
            return null;
        }

        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureSourceChannelSchemaAsync(connection);
            await EnsureLifecycleSchemaAsync(connection);

            const string query = @"
                SELECT * FROM orders
                WHERE COALESCE(source_channel, 'local') = 'local'
                  AND COALESCE(order_type, 'table') = 'table'
                  AND customer_name = @customerName
                  AND COALESCE(is_open, 1) = 1
                  AND COALESCE(local_lifecycle_state, 'active') NOT IN ('paid', 'voided')
                ORDER BY updated_at DESC, id DESC
                LIMIT 1";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@customerName", $"Table {tableNumber.Trim()}");

            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var order = MapOrderFromReader(reader);
                await reader.CloseAsync();
                order.Items = await GetOrderItemsAsync(connection, order.Id);
                return order;
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting latest table order by table number: {ex.Message}");
            return null;
        }
    }

    public async Task<List<Order>> GetOrdersByStatusAsync(OrderStatus status)
    {
        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureSourceChannelSchemaAsync(connection);
            await EnsureLifecycleSchemaAsync(connection);

            var query = @"SELECT * FROM orders WHERE status = @status ORDER BY created_at ASC";
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@status", status.ToString().ToLower());

            var orders = new List<Order>();
            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                orders.Add(MapOrderFromReader((MySqlDataReader)reader));
            }
            await reader.CloseAsync();

            // Load items for each order
            foreach (var order in orders)
            {
                order.Items = await GetOrderItemsAsync(connection, order.Id);
            }

            return orders;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting orders by status: {ex.Message}");
            return new List<Order>();
        }
    }

    /// <summary>
    /// Open website orders still in progress (not paid/voided/cancelled).
    /// Used by Admin Dashboard header badge.
    /// </summary>
    public async Task<int> GetIncomingWebOrderCountAsync()
    {
        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureSourceChannelSchemaAsync(connection);
            await EnsureLifecycleSchemaAsync(connection);

            await using var command = new MySqlCommand(@"
                SELECT COUNT(*)
                FROM orders
                WHERE LOWER(COALESCE(source_channel, '')) = 'web'
                  AND LOWER(COALESCE(status, '')) NOT IN ('cancelled', 'voided', 'completed')
                  AND LOWER(COALESCE(local_lifecycle_state, 'active')) NOT IN ('paid', 'voided')", connection);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result ?? 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetIncomingWebOrderCountAsync error: {ex.Message}");
            return 0;
        }
    }

    public async Task<List<Order>> GetOrdersAsync()
    {
        using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        await EnsureSourceChannelSchemaAsync(connection);
        await EnsureLifecycleSchemaAsync(connection);
        
        var orders = new List<Order>();

        const string sql = @"
                 SELECT id, order_id, order_number, cloud_order_id,
                   customer_name, customer_phone, customer_email, customer_address, 
                   total_amount, subtotal_amount, discount_amount, delivery_fee,
                   service_charge_percentage, service_charge_basis, service_charge_amount,
                   service_charge_status, service_charge_classification,
                   service_charge_removal_reason, service_charge_removed_by_user_id,
                   service_charge_removed_by_name, service_charge_approved_by_user_id,
                   service_charge_approved_by_name, service_charge_removed_at,
                   tax_amount, cash_tip_amount, card_tip_amount,
                     order_type, source_channel, table_session_id, payment_method, payment_status, amount_paid,
                     payment_provider, payment_reference, payment_currency, voucher_code, promo_code,
                     gift_card_number_masked, gift_card_amount_paid, gift_card_remaining_balance,
                     loyalty_points_earned, loyalty_points_redeemed, loyalty_points_discount, loyalty_balance_after,
                     special_instructions, scheduled_time,
                     local_lifecycle_state, is_open, void_reason, voided_at, voided_by, paid_at,
                   status, order_data, sync_status, 
                   kitchen_time, preparing_time, ready_time, delivering_time, completed_time,
                   created_at, updated_at
            FROM orders 
            ORDER BY created_at DESC";

        using var command = new MySqlCommand(sql, connection);
        using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            orders.Add(MapOrderFromReader((MySqlDataReader)reader));
        }

        await reader.CloseAsync();

        // Load order items for each order
        foreach (var order in orders)
        {
            order.Items = await GetOrderItemsAsync(connection, order.Id);
        }

        return orders;
    }

    private async Task<Order?> GetOrderByIdAsync(MySqlConnection connection, int orderId)
    {
        const string sql = @"
                 SELECT id, order_id, order_number, cloud_order_id,
                   customer_name, customer_phone, customer_email, customer_address, 
                   total_amount, subtotal_amount, discount_amount, delivery_fee,
                   service_charge_percentage, service_charge_basis, service_charge_amount,
                   service_charge_status, service_charge_classification,
                   service_charge_removal_reason, service_charge_removed_by_user_id,
                   service_charge_removed_by_name, service_charge_approved_by_user_id,
                   service_charge_approved_by_name, service_charge_removed_at,
                   tax_amount, cash_tip_amount, card_tip_amount,
                     order_type, source_channel, table_session_id, payment_method, payment_status, amount_paid,
                     payment_provider, payment_reference, payment_currency, voucher_code, promo_code,
                     gift_card_number_masked, gift_card_amount_paid, gift_card_remaining_balance,
                     loyalty_points_earned, loyalty_points_redeemed, loyalty_points_discount, loyalty_balance_after,
                     special_instructions, scheduled_time,
                     local_lifecycle_state, is_open, void_reason, voided_at, voided_by, paid_at,
                   status, order_data, sync_status, 
                   kitchen_time, preparing_time, ready_time, delivering_time, completed_time,
                   created_at, updated_at
            FROM orders 
            WHERE id = @id";

        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", orderId);
        
        using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var order = MapOrderFromReader(reader);
            await reader.CloseAsync();
            
            // Load order items
            order.Items = await GetOrderItemsAsync(connection, order.Id);
            
            return order;
        }
        
        return null;
    }

    public async Task<bool> UpdateOrderStatusAsync(int orderId, OrderStatus newStatus)
    {
        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureLifecycleSchemaAsync(connection);
            
            // Get the current order to update status timestamps
            const string selectSql = "SELECT status, payment_method, local_lifecycle_state FROM orders WHERE id = @id";
            using var selectCommand = new MySqlCommand(selectSql, connection);
            selectCommand.Parameters.AddWithValue("@id", orderId);
            
            string? currentPaymentMethod = null;
            string? currentLifecycleText = null;
            using (var reader = (MySqlDataReader)await selectCommand.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                {
                    return false;
                }

                currentPaymentMethod = reader["payment_method"]?.ToString();
                currentLifecycleText = reader["local_lifecycle_state"]?.ToString();
            }
            
            // Determine which timestamp column to update based on new status
            var timestampColumn = newStatus switch
            {
                OrderStatus.Kitchen => "kitchen_time",
                OrderStatus.Preparing => "preparing_time",
                OrderStatus.Ready => "ready_time", 
                OrderStatus.Delivering => "delivering_time",
                OrderStatus.Completed => "completed_time",
                _ => null
            };
            // Update the status and appropriate timestamp
            if (!string.IsNullOrWhiteSpace(currentLifecycleText) && IsTerminalLifecycleState(ParseLocalLifecycleState(currentLifecycleText)))
            {
                return false;
            }

            if (newStatus == OrderStatus.Completed && OnlineOrderPaymentHelper.IsDeferredPaymentMethod(currentPaymentMethod))
            {
                const string approvedPaymentSql = @"
                    SELECT COUNT(*)
                    FROM order_payments
                    WHERE order_id = @id AND status = 'approved' AND amount > 0";
                using var approvedPaymentCommand = new MySqlCommand(approvedPaymentSql, connection);
                approvedPaymentCommand.Parameters.AddWithValue("@id", orderId);
                var approvedPaymentCount = Convert.ToInt32(await approvedPaymentCommand.ExecuteScalarAsync());
                if (approvedPaymentCount == 0)
                {
                    return false;
                }
            }

            var updateSql = timestampColumn != null 
                ? $"UPDATE orders SET status = @status, local_lifecycle_state = @lifecycleState, is_open = @isOpen, paid_at = @paidAt, voided_at = @voidedAt, {timestampColumn} = @timestamp, updated_by_terminal_name = @updatedByTerminalName, updated_by_terminal_at = @updated, updated_at = @updated WHERE id = @id"
                : "UPDATE orders SET status = @status, local_lifecycle_state = @lifecycleState, is_open = @isOpen, paid_at = @paidAt, voided_at = @voidedAt, updated_by_terminal_name = @updatedByTerminalName, updated_by_terminal_at = @updated, updated_at = @updated WHERE id = @id";

            var nextLifecycleState = newStatus switch
            {
                OrderStatus.Completed => LocalLifecycleState.Paid,
                OrderStatus.Cancelled => LocalLifecycleState.Voided,
                _ => LocalLifecycleState.Active
            };

            var isOpen = nextLifecycleState != LocalLifecycleState.Paid && nextLifecycleState != LocalLifecycleState.Voided;

            using var updateCommand = new MySqlCommand(updateSql, connection);
            updateCommand.Parameters.AddWithValue("@id", orderId);
            updateCommand.Parameters.AddWithValue("@status", newStatus.ToString());
            updateCommand.Parameters.AddWithValue("@lifecycleState", ToDbLifecycleState(nextLifecycleState));
            updateCommand.Parameters.AddWithValue("@isOpen", isOpen);
            updateCommand.Parameters.AddWithValue("@paidAt", nextLifecycleState == LocalLifecycleState.Paid ? DateTime.Now : DBNull.Value);
            updateCommand.Parameters.AddWithValue("@voidedAt", nextLifecycleState == LocalLifecycleState.Voided ? DateTime.Now : DBNull.Value);
            updateCommand.Parameters.AddWithValue("@updated", DateTime.Now);
            updateCommand.Parameters.AddWithValue("@updatedByTerminalName", GetCurrentTerminalName());
            
            if (timestampColumn != null)
            {
                updateCommand.Parameters.AddWithValue("@timestamp", DateTime.Now);
            }

            var rowsAffected = await updateCommand.ExecuteNonQueryAsync();
            if (rowsAffected > 0)
            {
                await PublishOrderTerminalEventAsync(
                    connection,
                    orderId.ToString(),
                    null,
                    "status_changed",
                    new { status = newStatus.ToString(), lifecycle = ToDbLifecycleState(nextLifecycleState) });
            }

            return rowsAffected > 0;
        }
        catch (Exception)
        {
            // Log error if needed
            return false;
        }
    }

    public async Task<Dictionary<OrderStatus, int>> GetOrderCountsByStatusAsync()
    {
        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureLifecycleSchemaAsync(connection);

            var query = @"SELECT status, COUNT(*) as count FROM orders 
                         WHERE COALESCE(is_open, 1) = 1 AND COALESCE(local_lifecycle_state, 'active') NOT IN ('paid', 'voided')
                         GROUP BY status";
            
            using var command = new MySqlCommand(query, connection);
            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

            var counts = new Dictionary<OrderStatus, int>();
            
            while (await reader.ReadAsync())
            {
                var statusStr = reader["status"].ToString() ?? "";
                var count = Convert.ToInt32(reader["count"]);
                
                if (Enum.TryParse<OrderStatus>(statusStr, true, out var status))
                {
                    counts[status] = count;
                }
            }

            return counts;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting order counts: {ex.Message}");
            return new Dictionary<OrderStatus, int>();
        }
    }

    public async Task<int> GetTodayCompletedOrdersCountAsync()
    {
        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            var query = @"SELECT COUNT(*) FROM orders 
                         WHERE COALESCE(local_lifecycle_state, CASE WHEN LOWER(status) = 'completed' THEN 'paid' ELSE 'active' END) = 'paid' 
                         AND DATE(completed_time) = CURDATE()";
            
            using var command = new MySqlCommand(query, connection);
            var result = await command.ExecuteScalarAsync();
            
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting today's completed orders: {ex.Message}");
            return 0;
        }
    }

    public async Task<(bool Success, string Message)> ProcessAutomaticStatusTransitionsAsync()
    {
        try
        {
            var now = DateTime.Now;
            var updatedOrders = 0;

            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            // Kitchen -> Preparing (after 2 minutes)
            var kitchenToPreparingQuery = @"
                UPDATE orders SET 
                    status = 'preparing',
                    preparing_time = @now,
                    updated_by_terminal_name = @updatedByTerminalName,
                    updated_by_terminal_at = @now,
                    updated_at = @now
                WHERE status = 'kitchen' 
                AND kitchen_time IS NOT NULL 
                AND COALESCE(local_lifecycle_state, 'active') NOT IN ('paid', 'voided')
                AND TIMESTAMPDIFF(MINUTE, kitchen_time, @now) >= 2";

            using var cmd1 = new MySqlCommand(kitchenToPreparingQuery, connection);
            cmd1.Parameters.AddWithValue("@now", now);
            cmd1.Parameters.AddWithValue("@updatedByTerminalName", GetCurrentTerminalName());
            var count1 = await cmd1.ExecuteNonQueryAsync();
            updatedOrders += count1;

            // Preparing -> Ready (after 10 minutes total from kitchen time)
            var preparingToReadyQuery = @"
                UPDATE orders SET 
                    status = 'ready',
                    ready_time = @now,
                    updated_by_terminal_name = @updatedByTerminalName,
                    updated_by_terminal_at = @now,
                    updated_at = @now
                WHERE status = 'preparing' 
                AND kitchen_time IS NOT NULL 
                AND COALESCE(local_lifecycle_state, 'active') NOT IN ('paid', 'voided')
                AND TIMESTAMPDIFF(MINUTE, kitchen_time, @now) >= 10";

            using var cmd2 = new MySqlCommand(preparingToReadyQuery, connection);
            cmd2.Parameters.AddWithValue("@now", now);
            cmd2.Parameters.AddWithValue("@updatedByTerminalName", GetCurrentTerminalName());
            var count2 = await cmd2.ExecuteNonQueryAsync();
            updatedOrders += count2;

            if (updatedOrders > 0)
            {
                await TerminalEventSyncService.PublishAsync(
                    connection,
                    AppDataChangeKind.Orders,
                    "orders",
                    null,
                    null,
                    new { action = "automatic_status_transitions", count = updatedOrders });
            }

            return (true, $"Processed {updatedOrders} automatic status transitions");
        }
        catch (Exception ex)
        {
            return (false, $"Error processing status transitions: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> MarkOrderForDeliveryAsync(string orderId, string deliveryPersonName)
    {
        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            var query = @"
                UPDATE orders SET 
                    status = 'delivering',
                    delivering_time = @deliveringTime,
                    updated_by_terminal_name = @updatedByTerminalName,
                    updated_by_terminal_at = @updatedAt,
                    updated_at = @updatedAt
                WHERE order_id = @orderId AND status = 'ready' AND COALESCE(local_lifecycle_state, 'active') NOT IN ('paid', 'voided')";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@deliveringTime", DateTime.Now);
            command.Parameters.AddWithValue("@updatedAt", DateTime.Now);
            command.Parameters.AddWithValue("@updatedByTerminalName", GetCurrentTerminalName());
            command.Parameters.AddWithValue("@orderId", orderId);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            
            if (rowsAffected > 0)
            {
                await PublishOrderTerminalEventAsync(
                    connection,
                    orderId,
                    null,
                    "delivery_started",
                    new { deliveryPersonName });

                // Try to sync status with online system
                await _apiService.UpdateOrderStatusAsync(orderId, OrderStatus.Delivering, $"Taken by: {deliveryPersonName}");
                
                return (true, "Order marked for delivery successfully");
            }
            else
            {
                return (false, "Order not found or not in ready status");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Error marking order for delivery: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> CompleteOrderAsync(string orderId)
    {
        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            var query = @"
                UPDATE orders SET 
                    status = CASE
                        WHEN LOWER(COALESCE(local_lifecycle_state, '')) = 'paid' THEN 'completed'
                        WHEN LOWER(COALESCE(local_lifecycle_state, '')) IN ('sent_partial', 'sent_full', 'payment_partial') THEN 'kitchen'
                        ELSE 'ready'
                    END,
                    completed_time = @completedTime,
                    is_open = 1,
                    updated_by_terminal_name = @updatedByTerminalName,
                    updated_by_terminal_at = @updatedAt,
                    updated_at = @updatedAt
                WHERE order_id = @orderId AND status = 'delivering' AND COALESCE(local_lifecycle_state, 'active') NOT IN ('paid', 'voided')";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@completedTime", DateTime.Now);
            command.Parameters.AddWithValue("@updatedAt", DateTime.Now);
            command.Parameters.AddWithValue("@updatedByTerminalName", GetCurrentTerminalName());
            command.Parameters.AddWithValue("@orderId", orderId);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            
            if (rowsAffected > 0)
            {
                await PublishOrderTerminalEventAsync(
                    connection,
                    orderId,
                    null,
                    "delivered",
                    new { status = "ready", delivered = true });

                // Sync delivery milestone without marking the local order paid.
                await _apiService.UpdateOrderStatusAsync(orderId, OrderStatus.Ready, "Delivered successfully");
                
                return (true, "Order delivered successfully");
            }
            else
            {
                return (false, "Order not found or not in delivering status");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Error completing order: {ex.Message}");
        }
    }

    private async Task EnsureOrderTypeSchemaAsync(MySqlConnection connection)
    {
        if (RuntimeSchemaPolicy.IsMigrationManaged) return;
        if (_orderTypeSchemaChecked)
        {
            return;
        }

        try
        {
            // Keep historical aliases compatible before enforcing enum values.
            var normalizeLegacyTypesQuery = @"
                UPDATE orders
                SET order_type = CASE
                    WHEN LOWER(order_type) IN ('col', 'collection') THEN 'pickup'
                    WHEN LOWER(order_type) = 'del' THEN 'delivery'
                    ELSE order_type
                END";

            using var normalizeCommand = new MySqlCommand(normalizeLegacyTypesQuery, connection);
            await normalizeCommand.ExecuteNonQueryAsync();

            var ensureOrderTypeColumnQuery = @"
                ALTER TABLE orders
                ADD COLUMN IF NOT EXISTS order_type VARCHAR(50) DEFAULT 'pickup'";
            using var ensureColumnCommand = new MySqlCommand(ensureOrderTypeColumnQuery, connection);
            await ensureColumnCommand.ExecuteNonQueryAsync();

            var alterOrderTypeEnumQuery = @"
                ALTER TABLE orders
                MODIFY COLUMN order_type ENUM('pickup', 'delivery', 'table') DEFAULT 'pickup'";
            using var alterCommand = new MySqlCommand(alterOrderTypeEnumQuery, connection);
            await alterCommand.ExecuteNonQueryAsync();

            var normalizeTableTypesQuery = @"
                UPDATE orders
                SET order_type = 'table'
                WHERE LOWER(order_type) IN ('tbl', 'table', 'dine_in', 'dine-in')";
            using var normalizeTableCommand = new MySqlCommand(normalizeTableTypesQuery, connection);
            await normalizeTableCommand.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Order type schema check warning: {ex.Message}");
        }
        finally
        {
            _orderTypeSchemaChecked = true;
        }
    }

    private async Task EnsureSourceChannelSchemaAsync(MySqlConnection connection)
    {
        if (RuntimeSchemaPolicy.IsMigrationManaged) return;
        if (_sourceChannelSchemaChecked)
        {
            return;
        }

        try
        {
            var ensureSourceColumnQuery = @"
                ALTER TABLE orders
                ADD COLUMN IF NOT EXISTS source_channel ENUM('local', 'web') DEFAULT 'local'";
            using var ensureColumnCommand = new MySqlCommand(ensureSourceColumnQuery, connection);
            await ensureColumnCommand.ExecuteNonQueryAsync();

            var setDefaultSourceQuery = @"
                UPDATE orders
                SET source_channel = 'local'
                WHERE source_channel IS NULL OR TRIM(source_channel) = ''";
            using var setDefaultSourceCommand = new MySqlCommand(setDefaultSourceQuery, connection);
            await setDefaultSourceCommand.ExecuteNonQueryAsync();

            // Do not infer web/local from cloud_order_id. Local POS orders may keep a
            // cloud id after sync, but they must remain visible in Live Order.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Source channel schema check warning: {ex.Message}");
        }
        finally
        {
            _sourceChannelSchemaChecked = true;
        }
    }

    private async Task EnsureLifecycleSchemaAsync(MySqlConnection connection)
    {
        if (RuntimeSchemaPolicy.IsMigrationManaged) return;
        if (_lifecycleSchemaChecked)
        {
            return;
        }

        try
        {
                var ensureColumnsQuery = @"
                ALTER TABLE orders
                ADD COLUMN IF NOT EXISTS local_lifecycle_state ENUM('draft', 'active', 'sent_partial', 'sent_full', 'payment_partial', 'paid', 'voided') DEFAULT 'draft',
                ADD COLUMN IF NOT EXISTS is_open BOOLEAN DEFAULT TRUE,
                ADD COLUMN IF NOT EXISTS void_reason VARCHAR(255) NULL,
                ADD COLUMN IF NOT EXISTS voided_at DATETIME NULL,
                ADD COLUMN IF NOT EXISTS voided_by VARCHAR(100) NULL,
                ADD COLUMN IF NOT EXISTS paid_at DATETIME NULL,
                ADD COLUMN IF NOT EXISTS table_session_id INT NULL,
                ADD COLUMN IF NOT EXISTS first_sent_at DATETIME NULL,
                ADD COLUMN IF NOT EXISTS last_sent_at DATETIME NULL,
                ADD COLUMN IF NOT EXISTS send_attempt_count INT NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS send_failure_count INT NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS send_latency_ms INT NULL,
                ADD COLUMN IF NOT EXISTS first_payment_attempt_at DATETIME NULL,
                ADD COLUMN IF NOT EXISTS payment_attempt_count INT NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS payment_completion_seconds INT NULL,
                ADD COLUMN IF NOT EXISTS operational_last_event_at DATETIME NULL,
                ADD COLUMN IF NOT EXISTS draft_abandoned_flag BOOLEAN NOT NULL DEFAULT FALSE,
                ADD COLUMN IF NOT EXISTS draft_abandoned_at DATETIME NULL";

            using var ensureColumnsCommand = new MySqlCommand(ensureColumnsQuery, connection);
                await ensureColumnsCommand.ExecuteNonQueryAsync();

                using var ensureItemColumnsCommand = new MySqlCommand(@"
                    ALTER TABLE order_items
                    ADD COLUMN IF NOT EXISTS variant_id VARCHAR(100) NULL,
                    ADD COLUMN IF NOT EXISTS variant_name VARCHAR(100) NULL,
                    ADD COLUMN IF NOT EXISTS display_name VARCHAR(180) NULL,
                    ADD COLUMN IF NOT EXISTS print_group_id VARCHAR(36) NULL,
                    ADD COLUMN IF NOT EXISTS print_in_red BOOLEAN NOT NULL DEFAULT FALSE,
                    ADD COLUMN IF NOT EXISTS course_type VARCHAR(30) NULL,
                    ADD COLUMN IF NOT EXISTS fired_at DATETIME NULL,
                    ADD COLUMN IF NOT EXISTS fired_by VARCHAR(150) NULL,
                    ADD COLUMN IF NOT EXISTS client_item_id VARCHAR(100) NULL", connection);
                await ensureItemColumnsCommand.ExecuteNonQueryAsync();

            try
            {
                using var addSessionIndexCommand = new MySqlCommand(
                    "CREATE INDEX IF NOT EXISTS idx_orders_table_session_open ON orders (table_session_id, is_open)",
                    connection);
                await addSessionIndexCommand.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Order table session index warning: {ex.Message}");
            }

            var backfillQuery = @"
                UPDATE orders
                SET local_lifecycle_state = CASE
                        WHEN LOWER(status) = 'completed' THEN 'paid'
                        WHEN LOWER(status) = 'cancelled' THEN 'voided'
                        ELSE COALESCE(NULLIF(local_lifecycle_state, ''), 'active')
                    END,
                    is_open = CASE
                        WHEN LOWER(status) IN ('completed', 'cancelled') THEN 0
                        ELSE COALESCE(is_open, 1)
                    END,
                    paid_at = CASE
                        WHEN LOWER(status) = 'completed' THEN COALESCE(paid_at, completed_time)
                        ELSE paid_at
                    END,
                    voided_at = CASE
                        WHEN LOWER(status) = 'cancelled' THEN COALESCE(voided_at, updated_at)
                        ELSE voided_at
                    END";

            using var backfillCommand = new MySqlCommand(backfillQuery, connection);
            await backfillCommand.ExecuteNonQueryAsync();

            const string createOrderPaymentsTable = @"
                CREATE TABLE IF NOT EXISTS order_payments (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    order_id INT NOT NULL,
                    attempt_no INT NOT NULL DEFAULT 1,
                    payment_method ENUM('cash', 'card', 'gift_card', 'refund', 'tip_adjust') NOT NULL,
                    amount DECIMAL(10,2) NOT NULL,
                    currency_code CHAR(3) NOT NULL DEFAULT 'GBP',
                    status ENUM('attempted', 'approved', 'failed', 'voided') NOT NULL DEFAULT 'attempted',
                    reference VARCHAR(100) NULL,
                    tip_amount DECIMAL(10,2) NOT NULL DEFAULT 0,
                    metadata_json JSON NULL,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    created_by VARCHAR(100) NULL,
                    FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE CASCADE,
                    INDEX idx_order_payments_order_created (order_id, created_at)
                ) ENGINE=InnoDB";

            using var createPaymentsCommand = new MySqlCommand(createOrderPaymentsTable, connection);
            await createPaymentsCommand.ExecuteNonQueryAsync();

            const string createOrderEventsTable = @"
                CREATE TABLE IF NOT EXISTS order_events (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    order_id INT NOT NULL,
                    event_type ENUM('created', 'line_added', 'line_removed', 'line_updated', 'sent', 'resend', 'send_failed', 'split', 'payment_attempt', 'payment_approved', 'payment_failed', 'void_requested', 'voided', 'reopened', 'table_transferred', 'table_merged', 'state_changed', 'draft_abandoned') NOT NULL,
                    actor_type ENUM('user', 'system', 'manager') NOT NULL DEFAULT 'system',
                    actor_id VARCHAR(100) NULL,
                    actor_name VARCHAR(150) NULL,
                    event_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    payload_json JSON NULL,
                    FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE CASCADE,
                    INDEX idx_order_events_order_time (order_id, event_at)
                ) ENGINE=InnoDB";

            using var createEventsCommand = new MySqlCommand(createOrderEventsTable, connection);
            await createEventsCommand.ExecuteNonQueryAsync();

            try
            {
                using var alterOrderEventsCommand = new MySqlCommand(@"
                    ALTER TABLE order_events
                    MODIFY COLUMN event_type ENUM('created', 'line_added', 'line_removed', 'line_updated', 'sent', 'resend', 'send_failed', 'split', 'payment_attempt', 'payment_approved', 'payment_failed', 'void_requested', 'voided', 'reopened', 'table_transferred', 'table_merged', 'state_changed', 'draft_abandoned') NOT NULL", connection);
                await alterOrderEventsCommand.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Order events enum update warning: {ex.Message}");
            }

            const string createOrderItemSendTrackingTable = @"
                CREATE TABLE IF NOT EXISTS order_item_send_tracking (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    order_id INT NOT NULL,
                    order_item_id INT NOT NULL,
                    send_batch_id VARCHAR(36) NOT NULL,
                    station_type ENUM('kitchen', 'bar', 'receipt', 'other') NOT NULL DEFAULT 'kitchen',
                    print_group_id VARCHAR(36) NULL,
                    route_target VARCHAR(120) NULL,
                    send_status ENUM('queued', 'sent', 'printed', 'failed', 'retrying') NOT NULL DEFAULT 'queued',
                    sent_at DATETIME NULL,
                    printed_at DATETIME NULL,
                    failure_reason VARCHAR(255) NULL,
                    attempt_count INT NOT NULL DEFAULT 0,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    FOREIGN KEY (order_id) REFERENCES orders(id) ON DELETE CASCADE,
                    FOREIGN KEY (order_item_id) REFERENCES order_items(id) ON DELETE CASCADE,
                    INDEX idx_order_send_item_created (order_item_id, created_at)
                ) ENGINE=InnoDB";

            using var createSendTrackingCommand = new MySqlCommand(createOrderItemSendTrackingTable, connection);
            await createSendTrackingCommand.ExecuteNonQueryAsync();

            try
            {
                using var alterOrderItemsCommand = new MySqlCommand(@"
                    ALTER TABLE order_items
                    ADD COLUMN IF NOT EXISTS client_item_id VARCHAR(100) NULL,
                    ADD COLUMN IF NOT EXISTS print_group_id VARCHAR(36) NULL,
                    ADD COLUMN IF NOT EXISTS variant_id VARCHAR(100) NULL,
                    ADD COLUMN IF NOT EXISTS variant_name VARCHAR(100) NULL,
                    ADD COLUMN IF NOT EXISTS display_name VARCHAR(180) NULL", connection);
                await alterOrderItemsCommand.ExecuteNonQueryAsync();

                using var alterOrdersLiveUpdateCommand = new MySqlCommand(@"
                    ALTER TABLE orders
                    ADD COLUMN IF NOT EXISTS updated_by_terminal_name VARCHAR(120) NULL,
                    ADD COLUMN IF NOT EXISTS updated_by_terminal_at DATETIME NULL", connection);
                await alterOrdersLiveUpdateCommand.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Order items schema update warning: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lifecycle schema check warning: {ex.Message}");
        }
        finally
        {
            _lifecycleSchemaChecked = true;
        }
    }

    private async Task EnsureFinancialSchemaAsync(MySqlConnection connection)
    {
        if (RuntimeSchemaPolicy.IsMigrationManaged) return;
        if (_financialSchemaChecked)
        {
            return;
        }

        try
        {
            const string ensureColumnsQuery = @"
                ALTER TABLE orders
                ADD COLUMN IF NOT EXISTS discount_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00";

            using var ensureColumnsCommand = new MySqlCommand(ensureColumnsQuery, connection);
            await ensureColumnsCommand.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Order financial schema check warning: {ex.Message}");
        }
        finally
        {
            _financialSchemaChecked = true;
        }
    }

    private static string NormalizeOrderType(string? orderType)
    {
        var value = orderType?.Trim().ToLowerInvariant();
        return value switch
        {
            "pickup" => "pickup",
            "collection" => "pickup",
            "col" => "pickup",
            "delivery" => "delivery",
            "del" => "delivery",
            "table" => "table",
            "tbl" => "table",
            "dine_in" => "table",
            "dine-in" => "table",
            _ => "pickup"
        };
    }

    private static string NormalizeSourceChannel(string? sourceChannel)
    {
        var value = sourceChannel?.Trim().ToLowerInvariant();
        return value switch
        {
            "web" => "web",
            _ => "local"
        };
    }

    private static LocalLifecycleState NormalizeLocalLifecycleState(LocalLifecycleState state, OrderStatus status)
    {
        if (state == LocalLifecycleState.Draft && status == OrderStatus.Completed)
        {
            return LocalLifecycleState.Paid;
        }

        if (state == LocalLifecycleState.Draft && status == OrderStatus.Cancelled)
        {
            return LocalLifecycleState.Voided;
        }

        return state;
    }

    private static string ToDbLifecycleState(LocalLifecycleState state)
    {
        return state switch
        {
            LocalLifecycleState.Draft => "draft",
            LocalLifecycleState.Active => "active",
            LocalLifecycleState.SentPartial => "sent_partial",
            LocalLifecycleState.SentFull => "sent_full",
            LocalLifecycleState.PaymentPartial => "payment_partial",
            LocalLifecycleState.Paid => "paid",
            LocalLifecycleState.Voided => "voided",
            _ => "draft"
        };
    }

    private static LocalLifecycleState ParseLocalLifecycleState(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "draft" => LocalLifecycleState.Draft,
            "active" => LocalLifecycleState.Active,
            "sent_partial" => LocalLifecycleState.SentPartial,
            "sentfull" => LocalLifecycleState.SentFull,
            "sent_full" => LocalLifecycleState.SentFull,
            "payment_partial" => LocalLifecycleState.PaymentPartial,
            "paid" => LocalLifecycleState.Paid,
            "cancelled" => LocalLifecycleState.Voided,
            "void" => LocalLifecycleState.Voided,
            "voided" => LocalLifecycleState.Voided,
            _ => LocalLifecycleState.Draft
        };
    }

    private static bool IsTerminalLifecycleState(LocalLifecycleState state)
    {
        return state is LocalLifecycleState.Paid or LocalLifecycleState.Voided;
    }

    private async Task SaveOrderItemAsync(
        MySqlConnection connection,
        int orderId,
        OrderItem item,
        MySqlTransaction? transaction = null)
    {
        // Insert order item with new schema
        var itemQuery = @"
            INSERT INTO order_items (order_id, client_item_id, cloud_item_id, cloud_item_external_id, menu_item_id, variant_id, variant_name, display_name, print_group_id, print_in_red, course_type, fired_at, fired_by, item_name, quantity, item_price, special_instructions)
            VALUES (@orderId, @clientItemId, @cloudItemId, @cloudItemExternalId, @menuItemId, @variantId, @variantName, @displayName, @printGroupId, @printInRed, @courseType, @firedAt, @firedBy, @itemName, @quantity, @itemPrice, @specialInstructions)";

        using var itemCommand = new MySqlCommand(itemQuery, connection, transaction);
    itemCommand.Parameters.AddWithValue("@orderId", orderId);
        itemCommand.Parameters.AddWithValue("@clientItemId", item.ClientItemId ?? (object)DBNull.Value);
        itemCommand.Parameters.AddWithValue("@cloudItemId", item.CloudItemId ?? (object)DBNull.Value);
        itemCommand.Parameters.AddWithValue("@cloudItemExternalId", item.CloudItemExternalId ?? (object)DBNull.Value);
        itemCommand.Parameters.AddWithValue("@menuItemId", item.MenuItemId ?? (object)DBNull.Value);
        itemCommand.Parameters.AddWithValue("@variantId", item.VariantId ?? (object)DBNull.Value);
        itemCommand.Parameters.AddWithValue("@variantName", item.VariantName ?? (object)DBNull.Value);
        itemCommand.Parameters.AddWithValue("@displayName", item.DisplayName ?? (object)DBNull.Value);
        itemCommand.Parameters.AddWithValue("@printGroupId", item.PrintGroupId ?? (object)DBNull.Value);
        itemCommand.Parameters.AddWithValue("@printInRed", item.PrintInRed);
        itemCommand.Parameters.AddWithValue("@courseType", item.CourseType ?? (object)DBNull.Value);
        itemCommand.Parameters.AddWithValue("@firedAt", item.FiredAt ?? (object)DBNull.Value);
        itemCommand.Parameters.AddWithValue("@firedBy", item.FiredBy ?? (object)DBNull.Value);
        itemCommand.Parameters.AddWithValue("@itemName", item.ItemName);
        itemCommand.Parameters.AddWithValue("@quantity", item.Quantity);
        itemCommand.Parameters.AddWithValue("@itemPrice", item.ItemPrice ?? (object)DBNull.Value);
        itemCommand.Parameters.AddWithValue("@specialInstructions", item.SpecialInstructions ?? (object)DBNull.Value);

        await itemCommand.ExecuteNonQueryAsync();

        // Get the inserted item ID
        var getItemIdQuery = "SELECT LAST_INSERT_ID()";
        using var idCommand = new MySqlCommand(getItemIdQuery, connection, transaction);
        var itemId = Convert.ToInt32(await idCommand.ExecuteScalarAsync());

        // Insert addons for this item
        foreach (var addon in item.Addons)
        {
            await SaveOrderItemAddonAsync(connection, itemId, addon, transaction);
        }
    }

    private async Task SaveOrderItemAddonAsync(
        MySqlConnection connection,
        int orderItemId,
        OrderItemAddon addon,
        MySqlTransaction? transaction = null)
    {
        var addonQuery = @"
            INSERT INTO order_item_addons (order_item_id, addon_id, addon_name, addon_price, quantity) 
            VALUES (@orderItemId, @addonId, @addonName, @addonPrice, @quantity)";

        using var addonCommand = new MySqlCommand(addonQuery, connection, transaction);
        addonCommand.Parameters.AddWithValue("@orderItemId", orderItemId);
        addonCommand.Parameters.AddWithValue("@addonId", addon.AddonId ?? (object)DBNull.Value);
        addonCommand.Parameters.AddWithValue("@addonName", addon.AddonName);
        addonCommand.Parameters.AddWithValue("@addonPrice", addon.AddonPrice ?? (object)DBNull.Value);
        addonCommand.Parameters.AddWithValue("@quantity", addon.Quantity);

        await addonCommand.ExecuteNonQueryAsync();
    }

    private async Task SyncOrderItemsAsync(MySqlConnection connection, int orderDbId, List<OrderItem>? incomingItems)
    {
        var incoming = (incomingItems ?? new List<OrderItem>())
            .Where(item => !string.IsNullOrWhiteSpace(item.ItemName) && item.Quantity > 0)
            .ToList();

        var existingRows = new List<ExistingOrderItemRow>();
        const string existingQuery = @"
            SELECT
                oi.id,
                oi.client_item_id,
                oi.menu_item_id,
                oi.variant_id,
                oi.variant_name,
                oi.display_name,
                oi.print_group_id,
                oi.print_in_red,
                oi.course_type,
                oi.fired_at,
                oi.fired_by,
                oi.item_name,
                oi.quantity,
                oi.item_price,
                oi.special_instructions,
                CASE WHEN EXISTS (
                    SELECT 1 FROM order_item_send_tracking ost WHERE ost.order_item_id = oi.id LIMIT 1
                ) THEN 1 ELSE 0 END AS has_tracking
            FROM order_items oi
            WHERE oi.order_id = @orderId
            ORDER BY oi.id";

        using (var existingCommand = new MySqlCommand(existingQuery, connection))
        {
            existingCommand.Parameters.AddWithValue("@orderId", orderDbId);
            using var reader = (MySqlDataReader)await existingCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                existingRows.Add(new ExistingOrderItemRow
                {
                    Id = Convert.ToInt32(reader["id"]),
                    ClientItemId = reader["client_item_id"]?.ToString(),
                    MenuItemId = reader["menu_item_id"]?.ToString(),
                    VariantId = reader["variant_id"]?.ToString(),
                    VariantName = reader["variant_name"]?.ToString(),
                    DisplayName = reader["display_name"]?.ToString(),
                    PrintGroupId = reader["print_group_id"]?.ToString(),
                    ItemName = reader["item_name"]?.ToString() ?? string.Empty,
                    Quantity = Convert.ToInt32(reader["quantity"]),
                    ItemPrice = reader["item_price"] == DBNull.Value ? null : Convert.ToDecimal(reader["item_price"]),
                    SpecialInstructions = reader["special_instructions"]?.ToString(),
                    HasSendTracking = Convert.ToBoolean(reader["has_tracking"])
                });
            }
        }

        var usedExistingIds = new HashSet<int>();

        foreach (var incomingItem in incoming)
        {
            incomingItem.ClientItemId = string.IsNullOrWhiteSpace(incomingItem.ClientItemId)
                ? Guid.NewGuid().ToString()
                : incomingItem.ClientItemId.Trim();

            var matched = existingRows.FirstOrDefault(row =>
                !usedExistingIds.Contains(row.Id)
                && !string.IsNullOrWhiteSpace(row.ClientItemId)
                && string.Equals(row.ClientItemId, incomingItem.ClientItemId, StringComparison.OrdinalIgnoreCase));

            if (matched == null)
            {
                matched = existingRows.FirstOrDefault(row =>
                    !usedExistingIds.Contains(row.Id)
                    && string.IsNullOrWhiteSpace(row.ClientItemId)
                    && string.Equals(row.ItemName, incomingItem.ItemName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(NormalizeText(row.SpecialInstructions), NormalizeText(incomingItem.SpecialInstructions), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(NormalizeText(row.MenuItemId), NormalizeText(incomingItem.MenuItemId), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(NormalizeText(row.VariantId), NormalizeText(incomingItem.VariantId), StringComparison.OrdinalIgnoreCase)
                    && Nullable.Equals(row.ItemPrice, incomingItem.ItemPrice));
            }

            if (matched != null)
            {
                usedExistingIds.Add(matched.Id);
                const string updateItemSql = @"
                    UPDATE order_items
                    SET client_item_id = @clientItemId,
                        menu_item_id = @menuItemId,
                        variant_id = @variantId,
                        variant_name = @variantName,
                        display_name = @displayName,
                        print_group_id = @printGroupId,
                        print_in_red = @printInRed,
                        course_type = @courseType,
                        fired_at = @firedAt,
                        fired_by = @firedBy,
                        item_name = @itemName,
                        quantity = @quantity,
                        item_price = @itemPrice,
                        special_instructions = @specialInstructions
                    WHERE id = @id";

                using (var updateCommand = new MySqlCommand(updateItemSql, connection))
                {
                    updateCommand.Parameters.AddWithValue("@clientItemId", incomingItem.ClientItemId);
                    updateCommand.Parameters.AddWithValue("@menuItemId", incomingItem.MenuItemId ?? (object)DBNull.Value);
                    updateCommand.Parameters.AddWithValue("@variantId", incomingItem.VariantId ?? (object)DBNull.Value);
                    updateCommand.Parameters.AddWithValue("@variantName", incomingItem.VariantName ?? (object)DBNull.Value);
                    updateCommand.Parameters.AddWithValue("@displayName", incomingItem.DisplayName ?? (object)DBNull.Value);
                    updateCommand.Parameters.AddWithValue("@printGroupId", incomingItem.PrintGroupId ?? (object)DBNull.Value);
                    updateCommand.Parameters.AddWithValue("@printInRed", incomingItem.PrintInRed);
                    updateCommand.Parameters.AddWithValue("@courseType", incomingItem.CourseType ?? (object)DBNull.Value);
                    updateCommand.Parameters.AddWithValue("@firedAt", incomingItem.FiredAt ?? (object)DBNull.Value);
                    updateCommand.Parameters.AddWithValue("@firedBy", incomingItem.FiredBy ?? (object)DBNull.Value);
                    updateCommand.Parameters.AddWithValue("@itemName", incomingItem.ItemName);
                    updateCommand.Parameters.AddWithValue("@quantity", incomingItem.Quantity);
                    updateCommand.Parameters.AddWithValue("@itemPrice", incomingItem.ItemPrice ?? (object)DBNull.Value);
                    updateCommand.Parameters.AddWithValue("@specialInstructions", incomingItem.SpecialInstructions ?? (object)DBNull.Value);
                    updateCommand.Parameters.AddWithValue("@id", matched.Id);
                    await updateCommand.ExecuteNonQueryAsync();
                }

                await ReplaceOrderItemAddonsAsync(connection, matched.Id, incomingItem.Addons);
            }
            else
            {
                await SaveOrderItemAsync(connection, orderDbId, incomingItem);
            }
        }

        var removableIds = existingRows
            .Where(row => !usedExistingIds.Contains(row.Id) && !row.HasSendTracking)
            .Select(row => row.Id)
            .ToList();

        if (removableIds.Count == 0)
        {
            return;
        }

        foreach (var removableId in removableIds)
        {
            using (var deleteAddonCommand = new MySqlCommand("DELETE FROM order_item_addons WHERE order_item_id = @orderItemId", connection))
            {
                deleteAddonCommand.Parameters.AddWithValue("@orderItemId", removableId);
                await deleteAddonCommand.ExecuteNonQueryAsync();
            }

            using var deleteItemCommand = new MySqlCommand("DELETE FROM order_items WHERE id = @id", connection);
            deleteItemCommand.Parameters.AddWithValue("@id", removableId);
            await deleteItemCommand.ExecuteNonQueryAsync();
        }
    }

    private async Task ReplaceOrderItemAddonsAsync(MySqlConnection connection, int orderItemId, List<OrderItemAddon>? addons)
    {
        using (var deleteExisting = new MySqlCommand("DELETE FROM order_item_addons WHERE order_item_id = @orderItemId", connection))
        {
            deleteExisting.Parameters.AddWithValue("@orderItemId", orderItemId);
            await deleteExisting.ExecuteNonQueryAsync();
        }

        if (addons == null || addons.Count == 0)
        {
            return;
        }

        foreach (var addon in addons)
        {
            await SaveOrderItemAddonAsync(connection, orderItemId, addon);
        }
    }

    private static string NormalizeText(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private sealed class ExistingOrderItemRow
    {
        public int Id { get; set; }
        public string? ClientItemId { get; set; }
        public string? MenuItemId { get; set; }
        public string? VariantId { get; set; }
        public string? VariantName { get; set; }
        public string? DisplayName { get; set; }
        public string? PrintGroupId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal? ItemPrice { get; set; }
        public string? SpecialInstructions { get; set; }
        public bool HasSendTracking { get; set; }
    }

    private async Task<List<OrderItem>> GetOrderItemsAsync(MySqlConnection connection, int orderId)
    {
        var items = new List<OrderItem>();
        
        var query = "SELECT * FROM order_items WHERE order_id = @orderId";
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@orderId", orderId);

        using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var item = new OrderItem
            {
                Id = Convert.ToInt32(reader["id"]),
                OrderId = reader["order_id"].ToString() ?? "",
                ClientItemId = reader["client_item_id"]?.ToString(),
                CloudItemId = reader["cloud_item_id"] == DBNull.Value ? null : Convert.ToInt32(reader["cloud_item_id"]),
                CloudItemExternalId = HasColumn(reader, "cloud_item_external_id") ? reader["cloud_item_external_id"]?.ToString() : null,
                MenuItemId = reader["menu_item_id"]?.ToString(),
                VariantId = reader["variant_id"]?.ToString(),
                VariantName = reader["variant_name"]?.ToString(),
                DisplayName = reader["display_name"]?.ToString(),
                PrintGroupId = reader["print_group_id"]?.ToString(),
                PrintInRed = Convert.ToBoolean(reader["print_in_red"]),
                CourseType = reader["course_type"]?.ToString(),
                FiredAt = reader["fired_at"] == DBNull.Value ? null : Convert.ToDateTime(reader["fired_at"]),
                FiredBy = reader["fired_by"]?.ToString(),
                ItemName = reader["item_name"].ToString() ?? "",
                Quantity = Convert.ToInt32(reader["quantity"]),
                ItemPrice = reader["item_price"] == DBNull.Value ? null : Convert.ToDecimal(reader["item_price"]),
                SpecialInstructions = reader["special_instructions"]?.ToString(),
                Addons = new List<OrderItemAddon>()
            };
            items.Add(item);
        }

        await reader.CloseAsync();
        foreach (var item in items)
        {
            item.Addons = await GetOrderItemAddonsAsync(connection, item.Id);
        }

        return items;
    }

    private async Task<List<OrderItemAddon>> GetOrderItemAddonsAsync(MySqlConnection connection, int orderItemId)
    {
        var addons = new List<OrderItemAddon>();

        const string query = @"
            SELECT id, order_item_id, addon_id, addon_name, addon_price, quantity
            FROM order_item_addons
            WHERE order_item_id = @orderItemId
            ORDER BY id";

        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@orderItemId", orderItemId);
        using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            addons.Add(new OrderItemAddon
            {
                Id = Convert.ToInt32(reader["id"]),
                OrderItemId = Convert.ToInt32(reader["order_item_id"]),
                AddonId = reader["addon_id"]?.ToString(),
                AddonName = reader["addon_name"]?.ToString() ?? string.Empty,
                AddonPrice = reader["addon_price"] == DBNull.Value ? null : Convert.ToDecimal(reader["addon_price"]),
                Quantity = reader["quantity"] == DBNull.Value ? 1 : Convert.ToInt32(reader["quantity"])
            });
        }

        return addons;
    }

    public async Task<bool> LogOrderEventAsync(
        string externalOrderId,
        string eventType,
        string actorType = "system",
        string? actorId = null,
        string? actorName = null,
        object? payload = null,
        DateTime? eventAt = null)
    {
        if (string.IsNullOrWhiteSpace(externalOrderId) || string.IsNullOrWhiteSpace(eventType))
        {
            return false;
        }

        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureLifecycleSchemaAsync(connection);

            const string findOrderSql = "SELECT id FROM orders WHERE order_id = @orderId LIMIT 1";
            using var findOrderCommand = new MySqlCommand(findOrderSql, connection);
            findOrderCommand.Parameters.AddWithValue("@orderId", externalOrderId);
            var orderIdObj = await findOrderCommand.ExecuteScalarAsync();
            if (orderIdObj == null)
            {
                return false;
            }

            var orderDbId = Convert.ToInt32(orderIdObj);
            var payloadJson = payload == null ? null : JsonSerializer.Serialize(payload);

            await InsertOrderEventAsync(
                connection,
                orderDbId,
                eventType,
                string.IsNullOrWhiteSpace(actorType) ? "system" : actorType,
                actorId,
                actorName,
                eventAt,
                payloadJson);

            await UpdateOrderOperationalSummaryAsync(connection, orderDbId);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error logging order event: {ex.Message}");
            return false;
        }
    }

    public async Task RefreshDraftAbandonmentFlagsAsync(int staleAfterMinutes = 120)
    {
        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureLifecycleSchemaAsync(connection);

            const string markDraftsQuery = @"
                UPDATE orders o
                SET o.draft_abandoned_flag = 1,
                    o.draft_abandoned_at = COALESCE(o.draft_abandoned_at, o.updated_at)
                WHERE COALESCE(o.is_open, 1) = 1
                  AND COALESCE(o.local_lifecycle_state, 'draft') IN ('draft', 'active')
                  AND o.updated_at < DATE_SUB(NOW(), INTERVAL @staleMinutes MINUTE)
                  AND NOT EXISTS (
                      SELECT 1
                      FROM order_events oe
                      WHERE oe.order_id = o.id
                        AND oe.event_type IN ('sent', 'resend', 'payment_attempt', 'payment_approved', 'voided', 'state_changed')
                  )";

            using (var markCommand = new MySqlCommand(markDraftsQuery, connection))
            {
                markCommand.Parameters.AddWithValue("@staleMinutes", staleAfterMinutes);
                await markCommand.ExecuteNonQueryAsync();
            }

            const string clearDraftsQuery = @"
                UPDATE orders o
                SET o.draft_abandoned_flag = 0,
                    o.draft_abandoned_at = NULL
                WHERE COALESCE(o.local_lifecycle_state, 'draft') NOT IN ('draft', 'active')
                   OR COALESCE(o.is_open, 1) = 0
                   OR EXISTS (
                      SELECT 1
                      FROM order_events oe
                      WHERE oe.order_id = o.id
                        AND oe.event_type IN ('sent', 'resend', 'payment_attempt', 'payment_approved', 'voided')
                  )";

            using (var clearCommand = new MySqlCommand(clearDraftsQuery, connection))
            {
                await clearCommand.ExecuteNonQueryAsync();
            }

            const string draftEventSeedQuery = @"
                SELECT o.id, o.order_id, o.draft_abandoned_at
                FROM orders o
                WHERE o.draft_abandoned_flag = 1
                  AND NOT EXISTS (
                      SELECT 1
                      FROM order_events oe
                      WHERE oe.order_id = o.id
                        AND oe.event_type = 'draft_abandoned'
                  )";

            var staleRows = new List<(int OrderDbId, string ExternalOrderId, DateTime? DraftAbandonedAt)>();
            using (var seedCommand = new MySqlCommand(draftEventSeedQuery, connection))
            using (var reader = (MySqlDataReader)await seedCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    staleRows.Add((
                        Convert.ToInt32(reader["id"]),
                        reader["order_id"]?.ToString() ?? string.Empty,
                        reader["draft_abandoned_at"] as DateTime?));
                }
            }

            foreach (var stale in staleRows)
            {
                await InsertOrderEventAsync(
                    connection,
                    stale.OrderDbId,
                    "draft_abandoned",
                    "system",
                    null,
                    "system",
                    stale.DraftAbandonedAt,
                    JsonSerializer.Serialize(new { policyMinutes = staleAfterMinutes, orderId = stale.ExternalOrderId }));

                await UpdateOrderOperationalSummaryAsync(connection, stale.OrderDbId);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error refreshing draft abandonment flags: {ex.Message}");
        }
    }

    public async Task<OperationalAnalyticsSnapshot> GetOperationalAnalyticsAsync(DateTime startDate, DateTime endDate)
    {
        var snapshot = new OperationalAnalyticsSnapshot
        {
            StartDate = startDate,
            EndDate = endDate
        };

        try
        {
            await RefreshDraftAbandonmentFlagsAsync();

            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureLifecycleSchemaAsync(connection);

            const string sendLatencyQuery = @"
                SELECT
                    COUNT(*) AS sample_count,
                    COALESCE(AVG(latency_ms), 0) AS avg_value,
                    COALESCE(MIN(latency_ms), 0) AS min_value,
                    COALESCE(MAX(latency_ms), 0) AS max_value
                FROM (
                    SELECT
                        o.id,
                        ROUND(TIMESTAMPDIFF(MICROSECOND, o.created_at, MIN(oe.event_at)) / 1000) AS latency_ms
                    FROM orders o
                    INNER JOIN order_events oe ON oe.order_id = o.id
                    WHERE oe.event_type IN ('sent', 'resend')
                      AND oe.event_at >= @startDate AND oe.event_at < @endDate
                    GROUP BY o.id, o.created_at
                ) send_samples";

            using (var command = new MySqlCommand(sendLatencyQuery, connection))
            {
                command.Parameters.AddWithValue("@startDate", startDate);
                command.Parameters.AddWithValue("@endDate", endDate);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    snapshot.SendLatency = new OperationalMetricSummary
                    {
                        SampleCount = Convert.ToInt32(reader["sample_count"]),
                        Average = Convert.ToDouble(reader["avg_value"]),
                        Min = Convert.ToDouble(reader["min_value"]),
                        Max = Convert.ToDouble(reader["max_value"])
                    };
                }
            }

            const string paymentCompletionQuery = @"
                SELECT
                    COUNT(*) AS sample_count,
                    COALESCE(AVG(seconds_to_complete), 0) AS avg_value,
                    COALESCE(MIN(seconds_to_complete), 0) AS min_value,
                    COALESCE(MAX(seconds_to_complete), 0) AS max_value
                FROM (
                    SELECT
                        o.id,
                        TIMESTAMPDIFF(SECOND,
                            COALESCE(MIN(CASE WHEN oe.event_type = 'payment_attempt' THEN oe.event_at END), o.created_at),
                            MIN(CASE WHEN oe.event_type = 'payment_approved' THEN oe.event_at END)
                        ) AS seconds_to_complete
                    FROM orders o
                    INNER JOIN order_events oe ON oe.order_id = o.id
                    WHERE oe.event_type IN ('payment_attempt', 'payment_approved')
                      AND oe.event_at >= @startDate AND oe.event_at < @endDate
                    GROUP BY o.id, o.created_at
                    HAVING MIN(CASE WHEN oe.event_type = 'payment_approved' THEN oe.event_at END) IS NOT NULL
                ) payment_samples";

            using (var command = new MySqlCommand(paymentCompletionQuery, connection))
            {
                command.Parameters.AddWithValue("@startDate", startDate);
                command.Parameters.AddWithValue("@endDate", endDate);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    snapshot.PaymentCompletionTime = new OperationalMetricSummary
                    {
                        SampleCount = Convert.ToInt32(reader["sample_count"]),
                        Average = Convert.ToDouble(reader["avg_value"]),
                        Min = Convert.ToDouble(reader["min_value"]),
                        Max = Convert.ToDouble(reader["max_value"])
                    };
                }
            }

            if (snapshot.PaymentCompletionTime.SampleCount == 0)
            {
                const string paymentFallbackQuery = @"
                    SELECT
                        COUNT(*) AS sample_count,
                        COALESCE(AVG(TIMESTAMPDIFF(SECOND, created_at, paid_at)), 0) AS avg_value,
                        COALESCE(MIN(TIMESTAMPDIFF(SECOND, created_at, paid_at)), 0) AS min_value,
                        COALESCE(MAX(TIMESTAMPDIFF(SECOND, created_at, paid_at)), 0) AS max_value
                    FROM orders
                    WHERE paid_at IS NOT NULL
                      AND paid_at >= @startDate AND paid_at < @endDate
                      AND LOWER(COALESCE(status, '')) NOT IN ('cancelled', 'voided')";

                using var command = new MySqlCommand(paymentFallbackQuery, connection);
                command.Parameters.AddWithValue("@startDate", startDate);
                command.Parameters.AddWithValue("@endDate", endDate);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var sampleCount = Convert.ToInt32(reader["sample_count"]);
                    if (sampleCount > 0)
                    {
                        snapshot.PaymentCompletionTime = new OperationalMetricSummary
                        {
                            SampleCount = sampleCount,
                            Average = Convert.ToDouble(reader["avg_value"]),
                            Min = Convert.ToDouble(reader["min_value"]),
                            Max = Convert.ToDouble(reader["max_value"])
                        };
                    }
                }
            }

            if (snapshot.PaymentCompletionTime.SampleCount == 0)
            {
                const string paymentSecondsFallbackQuery = @"
                    SELECT
                        COUNT(*) AS sample_count,
                        COALESCE(AVG(payment_completion_seconds), 0) AS avg_value,
                        COALESCE(MIN(payment_completion_seconds), 0) AS min_value,
                        COALESCE(MAX(payment_completion_seconds), 0) AS max_value
                    FROM orders
                    WHERE payment_completion_seconds IS NOT NULL
                      AND payment_completion_seconds > 0
                      AND COALESCE(paid_at, updated_at) >= @startDate
                      AND COALESCE(paid_at, updated_at) < @endDate";

                using var command = new MySqlCommand(paymentSecondsFallbackQuery, connection);
                command.Parameters.AddWithValue("@startDate", startDate);
                command.Parameters.AddWithValue("@endDate", endDate);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var sampleCount = Convert.ToInt32(reader["sample_count"]);
                    if (sampleCount > 0)
                    {
                        snapshot.PaymentCompletionTime = new OperationalMetricSummary
                        {
                            SampleCount = sampleCount,
                            Average = Convert.ToDouble(reader["avg_value"]),
                            Min = Convert.ToDouble(reader["min_value"]),
                            Max = Convert.ToDouble(reader["max_value"])
                        };
                    }
                }
            }

            const string voidAuditQuery = @"
                SELECT
                    o.id AS order_db_id,
                    o.order_id,
                    COALESCE(NULLIF(o.order_number, ''), o.order_id) AS order_number,
                    oe.event_at,
                    oe.actor_type,
                    COALESCE(NULLIF(oe.actor_name, ''), NULLIF(o.voided_by, ''), 'Unknown') AS actor_name,
                    COALESCE(NULLIF(JSON_UNQUOTE(JSON_EXTRACT(oe.payload_json, '$.reason')), ''), NULLIF(o.void_reason, ''), 'Unspecified') AS void_reason
                FROM order_events oe
                INNER JOIN orders o ON o.id = oe.order_id
                WHERE oe.event_type = 'voided'
                  AND oe.event_at >= @startDate AND oe.event_at < @endDate
                ORDER BY oe.event_at DESC";

            using (var command = new MySqlCommand(voidAuditQuery, connection))
            {
                command.Parameters.AddWithValue("@startDate", startDate);
                command.Parameters.AddWithValue("@endDate", endDate);
                using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    snapshot.VoidAudits.Add(new VoidAuditEntry
                    {
                        OrderDbId = Convert.ToInt32(reader["order_db_id"]),
                        OrderId = reader["order_id"]?.ToString() ?? string.Empty,
                        OrderNumber = reader["order_number"]?.ToString() ?? string.Empty,
                        EventAt = Convert.ToDateTime(reader["event_at"]),
                        ActorType = reader["actor_type"]?.ToString() ?? "system",
                        ActorName = reader["actor_name"]?.ToString() ?? "Unknown",
                        Reason = reader["void_reason"]?.ToString() ?? "Unspecified"
                    });
                }
            }

            const string abandonmentCountQuery = @"
                SELECT COUNT(*)
                FROM order_events
                WHERE event_type = 'draft_abandoned'
                  AND event_at >= @startDate AND event_at < @endDate";

            using (var command = new MySqlCommand(abandonmentCountQuery, connection))
            {
                command.Parameters.AddWithValue("@startDate", startDate);
                command.Parameters.AddWithValue("@endDate", endDate);
                var count = await command.ExecuteScalarAsync();
                snapshot.DraftAbandonmentCount = Convert.ToInt32(count);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading operational analytics: {ex.Message}");
        }

        return snapshot;
    }

    public async Task<List<SendSupportRow>> GetSendSupportRowsAsync(int limit = 100)
    {
        var rows = new List<SendSupportRow>();

        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureLifecycleSchemaAsync(connection);

            const string query = @"
                SELECT
                    o.order_id,
                    COALESCE(NULLIF(o.order_number, ''), o.order_id) AS order_number,
                    COALESCE(o.local_lifecycle_state, 'draft') AS lifecycle_state,
                    oi.item_name,
                    oi.quantity,
                    oi.print_group_id,
                    ost.route_target,
                    ost.send_status,
                    ost.failure_reason,
                    ost.created_at
                FROM order_item_send_tracking ost
                INNER JOIN orders o ON o.id = ost.order_id
                INNER JOIN order_items oi ON oi.id = ost.order_item_id
                WHERE ost.send_status IN ('queued', 'failed', 'retrying')
                ORDER BY ost.updated_at DESC
                LIMIT @limit";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@limit", Math.Max(1, limit));

            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new SendSupportRow
                {
                    OrderId = reader["order_id"]?.ToString() ?? string.Empty,
                    OrderNumber = reader["order_number"]?.ToString() ?? string.Empty,
                    LifecycleState = reader["lifecycle_state"]?.ToString() ?? "draft",
                    ItemName = reader["item_name"]?.ToString() ?? string.Empty,
                    Quantity = Convert.ToInt32(reader["quantity"]),
                    PrintGroupId = reader["print_group_id"]?.ToString(),
                    RouteTarget = reader["route_target"]?.ToString(),
                    SendStatus = reader["send_status"]?.ToString() ?? "queued",
                    FailureReason = reader["failure_reason"]?.ToString(),
                    CreatedAt = Convert.ToDateTime(reader["created_at"])
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading send support rows: {ex.Message}");
        }

        return rows;
    }

    private async Task InsertOrderEventAsync(
        MySqlConnection connection,
        int orderDbId,
        string eventType,
        string actorType,
        string? actorId,
        string? actorName,
        DateTime? eventAt,
        string? payloadJson,
        MySqlTransaction? transaction = null)
    {
        const string insertEventSql = @"
            INSERT INTO order_events (order_id, event_type, actor_type, actor_id, actor_name, event_at, payload_json)
            VALUES (@orderId, @eventType, @actorType, @actorId, @actorName, @eventAt, @payloadJson)";

        using var command = new MySqlCommand(insertEventSql, connection, transaction);
        command.Parameters.AddWithValue("@orderId", orderDbId);
        command.Parameters.AddWithValue("@eventType", eventType);
        command.Parameters.AddWithValue("@actorType", actorType);
        command.Parameters.AddWithValue("@actorId", actorId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@actorName", actorName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@eventAt", eventAt ?? DateTime.Now);
        command.Parameters.AddWithValue("@payloadJson", payloadJson ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task PublishOrderTerminalEventAsync(
        MySqlConnection connection,
        Order order,
        string action,
        object? payload = null)
    {
        await TerminalEventSyncService.PublishAsync(
            connection,
            AppDataChangeKind.Orders,
            "order",
            order.OrderId,
            string.IsNullOrWhiteSpace(order.OrderNumber) ? order.OrderId : order.OrderNumber,
            new
            {
                action,
                orderId = order.OrderId,
                orderDbId = order.Id,
                orderNumber = order.OrderNumber,
                orderType = order.OrderType,
                lifecycle = ToDbLifecycleState(order.LocalLifecycleState),
                payload
            });
        NotifyClientsOrderUpdated(order.OrderId);
    }

    private static async Task PublishOrderTerminalEventAsync(
        MySqlConnection connection,
        string orderIdentifier,
        string? orderNumber,
        string action,
        object? payload = null)
    {
        var resolved = await ResolveOrderEventIdentityAsync(connection, orderIdentifier, orderNumber);
        await TerminalEventSyncService.PublishAsync(
            connection,
            AppDataChangeKind.Orders,
            "order",
            resolved.EntityId,
            resolved.OrderNumber,
            new
            {
                action,
                orderId = resolved.EntityId,
                orderNumber = resolved.OrderNumber,
                payload
            });
        NotifyClientsOrderUpdated(resolved.EntityId);
    }

    /// <summary>
    /// Phase 4: push order.updated to every paired Client POS after Mother persists a mutation
    /// (Mother till or Client API). Clients then fetch the authoritative order.
    /// </summary>
    private static void NotifyClientsOrderUpdated(string? orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return;
        }

        try
        {
            var broadcast = ServiceHelper.GetService<ClientWebSocketBroadcastService>();
            if (broadcast is null)
            {
                return;
            }

            _ = broadcast.PublishDataChangedAsync("order.updated", orderId.Trim());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OrderService] Client order.updated notify failed: {ex.Message}");
        }
    }

    private static async Task<(string EntityId, string? OrderNumber)> ResolveOrderEventIdentityAsync(
        MySqlConnection connection,
        string orderIdentifier,
        string? orderNumber)
    {
        if (string.IsNullOrWhiteSpace(orderIdentifier))
        {
            return (string.Empty, orderNumber);
        }

        try
        {
            const string sql = @"
                SELECT order_id, order_number
                FROM orders
                WHERE order_id = @identifier OR id = @dbId
                LIMIT 1";

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@identifier", orderIdentifier);
            command.Parameters.AddWithValue("@dbId", int.TryParse(orderIdentifier, out var dbId) ? dbId : -1);

            await using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var entityId = reader["order_id"]?.ToString();
                var resolvedOrderNumber = reader.IsDBNull(reader.GetOrdinal("order_number"))
                    ? null
                    : reader.GetString("order_number");

                return (
                    string.IsNullOrWhiteSpace(entityId) ? orderIdentifier : entityId,
                    string.IsNullOrWhiteSpace(orderNumber) ? resolvedOrderNumber : orderNumber);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Order terminal event identity warning: {ex.Message}");
        }

        return (orderIdentifier, orderNumber);
    }

    private async Task UpdateOrderOperationalSummaryAsync(MySqlConnection connection, int orderDbId)
    {
        const string metricsSql = @"
            SELECT
                MIN(CASE WHEN event_type IN ('sent', 'resend') THEN event_at END) AS first_sent_at,
                MAX(CASE WHEN event_type IN ('sent', 'resend') THEN event_at END) AS last_sent_at,
                COUNT(CASE WHEN event_type IN ('sent', 'resend') THEN 1 END) AS send_attempt_count,
                COUNT(CASE WHEN event_type = 'send_failed' THEN 1 END) AS send_failure_count,
                MIN(CASE WHEN event_type = 'payment_attempt' THEN event_at END) AS first_payment_attempt_at,
                COUNT(CASE WHEN event_type = 'payment_attempt' THEN 1 END) AS payment_attempt_count,
                MIN(CASE WHEN event_type = 'payment_approved' THEN event_at END) AS first_payment_approved_at,
                MAX(event_at) AS last_event_at
            FROM order_events
            WHERE order_id = @orderId";

        DateTime? firstSentAt = null;
        DateTime? lastSentAt = null;
        int sendAttemptCount = 0;
        int sendFailureCount = 0;
        DateTime? firstPaymentAttemptAt = null;
        int paymentAttemptCount = 0;
        DateTime? firstPaymentApprovedAt = null;
        DateTime? lastEventAt = null;

        using (var metricsCommand = new MySqlCommand(metricsSql, connection))
        {
            metricsCommand.Parameters.AddWithValue("@orderId", orderDbId);
            using var reader = (MySqlDataReader)await metricsCommand.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                firstSentAt = reader["first_sent_at"] as DateTime?;
                lastSentAt = reader["last_sent_at"] as DateTime?;
                sendAttemptCount = Convert.ToInt32(reader["send_attempt_count"]);
                sendFailureCount = Convert.ToInt32(reader["send_failure_count"]);
                firstPaymentAttemptAt = reader["first_payment_attempt_at"] as DateTime?;
                paymentAttemptCount = Convert.ToInt32(reader["payment_attempt_count"]);
                firstPaymentApprovedAt = reader["first_payment_approved_at"] as DateTime?;
                lastEventAt = reader["last_event_at"] as DateTime?;
            }
        }

        DateTime? orderCreatedAt = null;
        string? localLifecycleState = null;
        DateTime? orderUpdatedAt = null;
        using (var orderCommand = new MySqlCommand("SELECT created_at, updated_at, local_lifecycle_state FROM orders WHERE id = @orderId", connection))
        {
            orderCommand.Parameters.AddWithValue("@orderId", orderDbId);
            using var orderReader = (MySqlDataReader)await orderCommand.ExecuteReaderAsync();
            if (await orderReader.ReadAsync())
            {
                orderCreatedAt = orderReader["created_at"] as DateTime?;
                orderUpdatedAt = orderReader["updated_at"] as DateTime?;
                localLifecycleState = orderReader["local_lifecycle_state"]?.ToString();
            }
        }

        int? sendLatencyMs = null;
        if (orderCreatedAt.HasValue && firstSentAt.HasValue && firstSentAt.Value >= orderCreatedAt.Value)
        {
            sendLatencyMs = (int)Math.Round((firstSentAt.Value - orderCreatedAt.Value).TotalMilliseconds);
        }

        int? paymentCompletionSeconds = null;
        if (firstPaymentApprovedAt.HasValue)
        {
            var paymentStart = firstPaymentAttemptAt ?? orderCreatedAt;
            if (paymentStart.HasValue && firstPaymentApprovedAt.Value >= paymentStart.Value)
            {
                paymentCompletionSeconds = (int)Math.Round((firstPaymentApprovedAt.Value - paymentStart.Value).TotalSeconds);
            }
        }

        var isDraftLifecycle = string.Equals(localLifecycleState, "draft", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localLifecycleState, "active", StringComparison.OrdinalIgnoreCase);
        var isAbandonedDraft = isDraftLifecycle
            && orderUpdatedAt.HasValue
            && orderUpdatedAt.Value <= DateTime.Now.AddHours(-2)
            && sendAttemptCount == 0
            && paymentAttemptCount == 0;
        DateTime? draftAbandonedAt = isAbandonedDraft ? orderUpdatedAt : null;

        using var updateCommand = new MySqlCommand(@"
            UPDATE orders
            SET first_sent_at = @firstSentAt,
                last_sent_at = @lastSentAt,
                send_attempt_count = @sendAttemptCount,
                send_failure_count = @sendFailureCount,
                send_latency_ms = @sendLatencyMs,
                first_payment_attempt_at = @firstPaymentAttemptAt,
                payment_attempt_count = @paymentAttemptCount,
                payment_completion_seconds = @paymentCompletionSeconds,
                operational_last_event_at = @operationalLastEventAt,
                draft_abandoned_flag = @draftAbandonedFlag,
                draft_abandoned_at = @draftAbandonedAt,
                updated_at = @preserveUpdatedAt
            WHERE id = @orderId", connection);

        updateCommand.Parameters.AddWithValue("@orderId", orderDbId);
        updateCommand.Parameters.AddWithValue("@firstSentAt", firstSentAt ?? (object)DBNull.Value);
        updateCommand.Parameters.AddWithValue("@lastSentAt", lastSentAt ?? (object)DBNull.Value);
        updateCommand.Parameters.AddWithValue("@sendAttemptCount", sendAttemptCount);
        updateCommand.Parameters.AddWithValue("@sendFailureCount", sendFailureCount);
        updateCommand.Parameters.AddWithValue("@sendLatencyMs", sendLatencyMs ?? (object)DBNull.Value);
        updateCommand.Parameters.AddWithValue("@firstPaymentAttemptAt", firstPaymentAttemptAt ?? (object)DBNull.Value);
        updateCommand.Parameters.AddWithValue("@paymentAttemptCount", paymentAttemptCount);
        updateCommand.Parameters.AddWithValue("@paymentCompletionSeconds", paymentCompletionSeconds ?? (object)DBNull.Value);
        updateCommand.Parameters.AddWithValue("@operationalLastEventAt", lastEventAt ?? (object)DBNull.Value);
        updateCommand.Parameters.AddWithValue("@draftAbandonedFlag", isAbandonedDraft);
        updateCommand.Parameters.AddWithValue("@draftAbandonedAt", draftAbandonedAt ?? (object)DBNull.Value);
        updateCommand.Parameters.AddWithValue("@preserveUpdatedAt", orderUpdatedAt ?? DateTime.Now);
        await updateCommand.ExecuteNonQueryAsync();
    }

    public async Task<string> CreateSendBatchAsync(int orderDbId, IEnumerable<OrderItem> items)
    {
        using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        await EnsureLifecycleSchemaAsync(connection);

        var batchId = Guid.NewGuid().ToString();
        var now = DateTime.Now;

        foreach (var item in items)
        {
            var insertTrackingQuery = @"
                INSERT INTO order_item_send_tracking
                    (order_id, order_item_id, send_batch_id, station_type, print_group_id, route_target, send_status, sent_at, printed_at, failure_reason, attempt_count, created_at, updated_at)
                VALUES
                    (@orderId, @orderItemId, @sendBatchId, @stationType, @printGroupId, @routeTarget, 'queued', NULL, NULL, NULL, 1, @createdAt, @updatedAt)";

            using var command = new MySqlCommand(insertTrackingQuery, connection);
            command.Parameters.AddWithValue("@orderId", orderDbId);
            command.Parameters.AddWithValue("@orderItemId", item.Id);
            command.Parameters.AddWithValue("@sendBatchId", batchId);
            command.Parameters.AddWithValue("@stationType", string.IsNullOrWhiteSpace(item.PrintGroupId) ? "kitchen" : "kitchen");
            command.Parameters.AddWithValue("@printGroupId", item.PrintGroupId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@routeTarget", item.PrintGroupId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@createdAt", now);
            command.Parameters.AddWithValue("@updatedAt", now);
            await command.ExecuteNonQueryAsync();
        }

        await PublishOrderTerminalEventAsync(
            connection,
            orderDbId.ToString(),
            null,
            "print_queued",
            new { batchId });

        return batchId;
    }

    public async Task<List<OrderItemSendTracking>> GetLatestSendTrackingAsync(int orderDbId)
    {
        using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        await EnsureLifecycleSchemaAsync(connection);

        const string query = @"
            SELECT ost.*
            FROM order_item_send_tracking ost
            INNER JOIN (
                SELECT order_item_id, MAX(id) AS max_id
                FROM order_item_send_tracking
                WHERE order_id = @orderId
                GROUP BY order_item_id
            ) latest ON ost.id = latest.max_id
            WHERE ost.order_id = @orderId";

        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@orderId", orderDbId);

        var tracking = new List<OrderItemSendTracking>();
        using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tracking.Add(new OrderItemSendTracking
            {
                Id = Convert.ToInt32(reader["id"]),
                OrderDbId = Convert.ToInt32(reader["order_id"]),
                OrderItemDbId = Convert.ToInt32(reader["order_item_id"]),
                SendBatchId = reader["send_batch_id"].ToString() ?? string.Empty,
                StationType = reader["station_type"].ToString() ?? "kitchen",
                PrintGroupId = reader["print_group_id"]?.ToString(),
                RouteTarget = reader["route_target"]?.ToString(),
                SendStatus = reader["send_status"].ToString() ?? "queued",
                SentAt = reader["sent_at"] as DateTime?,
                PrintedAt = reader["printed_at"] as DateTime?,
                FailureReason = reader["failure_reason"]?.ToString(),
                AttemptCount = Convert.ToInt32(reader["attempt_count"]),
                CreatedAt = Convert.ToDateTime(reader["created_at"]),
                UpdatedAt = Convert.ToDateTime(reader["updated_at"])
            });
        }

        return tracking;
    }

    public async Task<bool> MarkSendBatchResultAsync(int orderDbId, string batchId, IEnumerable<int> printedOrderItemIds, IEnumerable<(int OrderItemDbId, string Reason)> failedItems)
    {
        using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        await EnsureLifecycleSchemaAsync(connection);

        var now = DateTime.Now;
        var printedItemIds = printedOrderItemIds.ToList();
        var failedItemRows = failedItems.ToList();

        foreach (var orderItemId in printedItemIds)
        {
            using var command = new MySqlCommand(@"
                UPDATE order_item_send_tracking
                SET send_status = 'printed',
                    sent_at = COALESCE(sent_at, @now),
                    printed_at = @now,
                    failure_reason = NULL,
                    attempt_count = attempt_count + 1,
                    updated_at = @now
                WHERE order_id = @orderId AND send_batch_id = @batchId AND order_item_id = @orderItemId", connection);
            command.Parameters.AddWithValue("@now", now);
            command.Parameters.AddWithValue("@orderId", orderDbId);
            command.Parameters.AddWithValue("@batchId", batchId);
            command.Parameters.AddWithValue("@orderItemId", orderItemId);
            await command.ExecuteNonQueryAsync();
        }

        foreach (var failed in failedItemRows)
        {
            using var command = new MySqlCommand(@"
                UPDATE order_item_send_tracking
                SET send_status = 'failed',
                    failure_reason = @reason,
                    attempt_count = attempt_count + 1,
                    updated_at = @now
                WHERE order_id = @orderId AND send_batch_id = @batchId AND order_item_id = @orderItemId", connection);
            command.Parameters.AddWithValue("@now", now);
            command.Parameters.AddWithValue("@orderId", orderDbId);
            command.Parameters.AddWithValue("@batchId", batchId);
            command.Parameters.AddWithValue("@orderItemId", failed.OrderItemDbId);
            command.Parameters.AddWithValue("@reason", failed.Reason);
            await command.ExecuteNonQueryAsync();
        }

        await PublishOrderTerminalEventAsync(
            connection,
            orderDbId.ToString(),
            null,
            "print_result",
            new
            {
                batchId,
                printedCount = printedItemIds.Count,
                failedCount = failedItemRows.Count
            });

        return true;
    }

    public async Task<bool> RecordPaymentLineAsync(
        string externalOrderId,
        string paymentMethod,
        decimal amount,
        string status,
        decimal tipAmount = 0,
        string? reference = null,
        string? createdBy = null,
        object? metadata = null,
        decimal? maximumApprovedTotal = null)
    {
        if (string.IsNullOrWhiteSpace(externalOrderId) || string.IsNullOrWhiteSpace(paymentMethod))
        {
            return false;
        }

        var normalizedMethod = paymentMethod.Trim().ToLowerInvariant() switch
        {
            "cash" => "cash",
            "card" => "card",
            "giftcard" => "gift_card",
            "gift_card" => "gift_card",
            "refund" => "refund",
            "tip_adjust" => "tip_adjust",
            _ => "cash"
        };

        var normalizedStatus = status.Trim().ToLowerInvariant() switch
        {
            "attempted" => "attempted",
            "approved" => "approved",
            "failed" => "failed",
            "voided" => "voided",
            _ => "attempted"
        };

        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureLifecycleSchemaAsync(connection);
            using var transaction = await connection.BeginTransactionAsync();

            const string findOrderSql = "SELECT id FROM orders WHERE order_id = @orderId LIMIT 1 FOR UPDATE";
            using var findOrderCommand = new MySqlCommand(findOrderSql, connection, transaction);
            findOrderCommand.Parameters.AddWithValue("@orderId", externalOrderId);
            var orderIdObj = await findOrderCommand.ExecuteScalarAsync();
            if (orderIdObj == null)
            {
                return false;
            }

            var orderDbId = Convert.ToInt32(orderIdObj);

            if (normalizedStatus == "approved" && !string.IsNullOrWhiteSpace(reference))
            {
                const string duplicateSql = """
                    SELECT COUNT(*) FROM order_payments
                    WHERE order_id = @orderId AND status = 'approved' AND reference = @reference
                    """;
                using var duplicateCommand = new MySqlCommand(duplicateSql, connection, transaction);
                duplicateCommand.Parameters.AddWithValue("@orderId", orderDbId);
                duplicateCommand.Parameters.AddWithValue("@reference", reference);
                if (Convert.ToInt32(await duplicateCommand.ExecuteScalarAsync() ?? 0) > 0)
                {
                    await transaction.CommitAsync();
                    return true;
                }
            }

            if (normalizedStatus == "approved" && maximumApprovedTotal.HasValue)
            {
                const string approvedTotalSql = """
                    SELECT COALESCE(SUM(amount), 0) FROM order_payments
                    WHERE order_id = @orderId AND status = 'approved'
                    """;
                using var approvedTotalCommand = new MySqlCommand(approvedTotalSql, connection, transaction);
                approvedTotalCommand.Parameters.AddWithValue("@orderId", orderDbId);
                var approvedTotal = Convert.ToDecimal(await approvedTotalCommand.ExecuteScalarAsync() ?? 0m);
                if (approvedTotal + amount > maximumApprovedTotal.Value + 0.009m)
                {
                    await transaction.RollbackAsync();
                    return false;
                }
            }

            const string nextAttemptSql = "SELECT COALESCE(MAX(attempt_no), 0) + 1 FROM order_payments WHERE order_id = @orderId";
            using var nextAttemptCommand = new MySqlCommand(nextAttemptSql, connection, transaction);
            nextAttemptCommand.Parameters.AddWithValue("@orderId", orderDbId);
            var attemptNo = Convert.ToInt32(await nextAttemptCommand.ExecuteScalarAsync());

            const string insertSql = @"
                INSERT INTO order_payments
                    (order_id, attempt_no, payment_method, amount, currency_code, status, reference, tip_amount, metadata_json, created_at, created_by)
                VALUES
                    (@orderId, @attemptNo, @paymentMethod, @amount, 'GBP', @status, @reference, @tipAmount, @metadataJson, @createdAt, @createdBy)";

            using var insertCommand = new MySqlCommand(insertSql, connection, transaction);
            insertCommand.Parameters.AddWithValue("@orderId", orderDbId);
            insertCommand.Parameters.AddWithValue("@attemptNo", attemptNo);
            insertCommand.Parameters.AddWithValue("@paymentMethod", normalizedMethod);
            insertCommand.Parameters.AddWithValue("@amount", amount);
            insertCommand.Parameters.AddWithValue("@status", normalizedStatus);
            insertCommand.Parameters.AddWithValue("@reference", reference ?? (object)DBNull.Value);
            insertCommand.Parameters.AddWithValue("@tipAmount", tipAmount);
            insertCommand.Parameters.AddWithValue("@metadataJson", metadata == null ? (object)DBNull.Value : JsonSerializer.Serialize(metadata));
            insertCommand.Parameters.AddWithValue("@createdAt", DateTime.Now);
            insertCommand.Parameters.AddWithValue("@createdBy", createdBy ?? (object)DBNull.Value);
            await insertCommand.ExecuteNonQueryAsync();

            await transaction.CommitAsync();

            await PublishOrderTerminalEventAsync(
                connection,
                externalOrderId,
                null,
                normalizedStatus == "approved" ? "payment_approved" : "payment_updated",
                new
                {
                    paymentMethod = normalizedMethod,
                    amount,
                    status = normalizedStatus,
                    attemptNo
                });

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error recording payment line: {ex.Message}");
            return false;
        }
    }

    public async Task<List<OrderPayment>> GetOrderPaymentsAsync(int orderDbId)
    {
        var payments = new List<OrderPayment>();

        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureLifecycleSchemaAsync(connection);

            const string query = @"
                SELECT id, order_id, attempt_no, payment_method, amount, currency_code, status,
                       reference, tip_amount, metadata_json, created_at, created_by
                FROM order_payments
                WHERE order_id = @orderId
                ORDER BY attempt_no ASC, created_at ASC, id ASC";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@orderId", orderDbId);

            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                payments.Add(new OrderPayment
                {
                    Id = Convert.ToInt32(reader["id"]),
                    OrderDbId = Convert.ToInt32(reader["order_id"]),
                    AttemptNo = Convert.ToInt32(reader["attempt_no"]),
                    PaymentMethod = reader["payment_method"]?.ToString() ?? string.Empty,
                    Amount = Convert.ToDecimal(reader["amount"]),
                    CurrencyCode = reader["currency_code"]?.ToString() ?? "GBP",
                    Status = reader["status"]?.ToString() ?? "attempted",
                    Reference = reader["reference"]?.ToString(),
                    TipAmount = reader["tip_amount"] != DBNull.Value ? Convert.ToDecimal(reader["tip_amount"]) : 0m,
                    MetadataJson = reader["metadata_json"]?.ToString(),
                    CreatedAt = Convert.ToDateTime(reader["created_at"]),
                    CreatedBy = reader["created_by"]?.ToString()
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading order payments: {ex.Message}");
        }

        return payments;
    }

    /// <summary>
    /// Finds the final ledger row for an idempotent payment reference.  This is
    /// deliberately read-only and is used after an interrupted client request
    /// so the terminal can check Mother rather than submit another charge.
    /// </summary>
    public async Task<(string OrderId, OrderPayment Payment)?> FindPaymentByReferenceAsync(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;

        try
        {
            using var connection = new MySqlConnection(TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();
            await EnsureLifecycleSchemaAsync(connection);
            const string sql = @"
                SELECT o.order_id, p.id, p.order_id, p.attempt_no, p.payment_method, p.amount,
                       p.currency_code, p.status, p.reference, p.tip_amount, p.metadata_json,
                       p.created_at, p.created_by
                FROM order_payments p
                INNER JOIN orders o ON o.id = p.order_id
                WHERE p.reference = @reference
                ORDER BY p.id DESC
                LIMIT 1";
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@reference", reference);
            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return (reader.GetString(0), new OrderPayment
            {
                Id = reader.GetInt32(1),
                OrderDbId = reader.GetInt32(2),
                AttemptNo = reader.GetInt32(3),
                PaymentMethod = reader.GetString(4),
                Amount = reader.GetDecimal(5),
                CurrencyCode = reader.GetString(6),
                Status = reader.GetString(7),
                Reference = reader.IsDBNull(8) ? null : reader.GetString(8),
                TipAmount = reader.IsDBNull(9) ? 0m : reader.GetDecimal(9),
                MetadataJson = reader.IsDBNull(10) ? null : reader.GetString(10),
                CreatedAt = reader.GetDateTime(11),
                CreatedBy = reader.IsDBNull(12) ? null : reader.GetString(12)
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error finding payment by reference: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Quarantines legacy empty table rows created by the old eager-draft flow.
    /// It deliberately does not touch any order with items, payments, refunds,
    /// kitchen tracking, or kitchen revisions. Rows are retained for audit but
    /// removed from operational/history/report states.
    /// </summary>
    public async Task<int> QuarantineCorruptedEmptyTableOrdersAsync(int staleAfterMinutes = 120)
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            await EnsureLifecycleSchemaAsync(connection);
            await using var transaction = await connection.BeginTransactionAsync();

            const string candidatePredicate = @"
                LOWER(COALESCE(o.source_channel, 'local')) = 'local'
                AND LOWER(COALESCE(o.order_type, 'table')) = 'table'
                AND COALESCE(o.total_amount, 0) = 0
                AND o.updated_at < DATE_SUB(NOW(), INTERVAL @staleMinutes MINUTE)
                AND NOT EXISTS (SELECT 1 FROM order_items oi WHERE oi.order_id = o.id)
                AND NOT EXISTS (SELECT 1 FROM order_payments op WHERE op.order_id = o.id)
                AND NOT EXISTS (SELECT 1 FROM order_refunds ore WHERE ore.order_id = o.id)
                AND NOT EXISTS (SELECT 1 FROM order_item_send_tracking ost WHERE ost.order_id = o.id)
                AND NOT EXISTS (SELECT 1 FROM kitchen_order_revisions kor WHERE kor.order_id = o.id)";

            await using (var unlinkSessions = new MySqlCommand($@"
                UPDATE TableSessions session
                INNER JOIN orders o ON o.order_id = session.CurrentOrderId
                SET session.CurrentOrderId = NULL,
                    session.UpdatedDate = CURRENT_TIMESTAMP
                WHERE {candidatePredicate}", connection, transaction))
            {
                unlinkSessions.Parameters.AddWithValue("@staleMinutes", Math.Max(30, staleAfterMinutes));
                await unlinkSessions.ExecuteNonQueryAsync();
            }

            int quarantined;
            await using (var quarantine = new MySqlCommand($@"
                UPDATE orders o
                SET o.local_lifecycle_state = 'draft',
                    o.status = 'new',
                    o.is_open = 0,
                    o.draft_abandoned_flag = 1,
                    o.draft_abandoned_at = COALESCE(o.draft_abandoned_at, o.updated_at),
                    o.void_reason = NULL,
                    o.voided_at = NULL,
                    o.voided_by = NULL,
                    o.paid_at = NULL
                WHERE {candidatePredicate}", connection, transaction))
            {
                quarantine.Parameters.AddWithValue("@staleMinutes", Math.Max(30, staleAfterMinutes));
                quarantined = await quarantine.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            if (quarantined > 0)
            {
                AppDiagnostics.Log($"Quarantined {quarantined} legacy empty table order row(s).");
            }
            return quarantined;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Empty table order quarantine skipped: {ex.Message}");
            return 0;
        }
    }

    public async Task<bool> VoidApprovedPaymentAsync(int paymentId, string voidedBy, string reason)
    {
        if (paymentId <= 0 || string.IsNullOrWhiteSpace(voidedBy) || string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
        await connection.OpenAsync();
        await EnsureLifecycleSchemaAsync(connection);
        const string sql = """
            UPDATE order_payments
            SET status = 'voided',
                metadata_json = JSON_SET(
                    COALESCE(metadata_json, JSON_OBJECT()),
                    '$.voidedBy', @voidedBy,
                    '$.voidReason', @reason,
                    '$.voidedAt', @voidedAt)
            WHERE id = @paymentId AND status = 'approved'
            """;
        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@paymentId", paymentId);
        command.Parameters.AddWithValue("@voidedBy", voidedBy.Trim());
        command.Parameters.AddWithValue("@reason", reason.Trim());
        command.Parameters.AddWithValue("@voidedAt", DateTime.UtcNow.ToString("O"));
        // Voiding the approved row also removes its associated tip from every
        // reconciliation query because both are stored on the same payment row.
        return await command.ExecuteNonQueryAsync() == 1;
    }

    private static void AddServiceChargeParameters(MySqlCommand command, Order order)
    {
        command.Parameters.AddWithValue("@serviceChargePercentage", order.ServiceChargePercentage);
        command.Parameters.AddWithValue("@serviceChargeBasis", order.ServiceChargeBasis);
        command.Parameters.AddWithValue("@serviceChargeAmount", order.ServiceChargeAmount);
        command.Parameters.AddWithValue("@serviceChargeStatus", string.IsNullOrWhiteSpace(order.ServiceChargeStatus)
            ? "not_configured"
            : order.ServiceChargeStatus);
        command.Parameters.AddWithValue("@serviceChargeClassification",
            string.IsNullOrWhiteSpace(order.ServiceChargeClassification)
                ? DBNull.Value
                : order.ServiceChargeClassification);
        command.Parameters.AddWithValue("@serviceChargeRemovalReason", order.ServiceChargeRemovalReason ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@serviceChargeRemovedByUserId", order.ServiceChargeRemovedByUserId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@serviceChargeRemovedByName", order.ServiceChargeRemovedByName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@serviceChargeApprovedByUserId", order.ServiceChargeApprovedByUserId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@serviceChargeApprovedByName", order.ServiceChargeApprovedByName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@serviceChargeRemovedAt", order.ServiceChargeRemovedAt ?? (object)DBNull.Value);
    }

    private static void AddCloudPaymentParameters(MySqlCommand command, Order order)
    {
        command.Parameters.AddWithValue("@paymentStatus", order.PaymentStatusRaw ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@amountPaid", order.AmountPaid ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@paymentProvider", order.PaymentProvider ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@paymentReference", order.TransactionId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@paymentCurrency", order.CurrencyCode ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@voucherCode", order.VoucherCode ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@promoCode", order.PromoCode ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@giftCardNumberMasked", order.GiftCardNumberMasked ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@giftCardAmountPaid", order.GiftCardAmountPaid ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@giftCardRemainingBalance", order.GiftCardRemainingBalance ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@loyaltyPointsEarned", order.LoyaltyPointsEarned);
        command.Parameters.AddWithValue("@loyaltyPointsRedeemed", order.LoyaltyPointsRedeemed);
        command.Parameters.AddWithValue("@loyaltyPointsDiscount", order.LoyaltyPointsDiscount);
        command.Parameters.AddWithValue("@loyaltyBalanceAfter", order.LoyaltyBalanceAfter ?? (object)DBNull.Value);
    }

    private Order MapOrderFromReader(MySqlDataReader reader)
    {
        return new Order
        {
            Id = Convert.ToInt32(reader["id"]),
            OrderId = reader["order_id"].ToString() ?? "",
            OrderNumber = reader["order_number"]?.ToString(),
            CloudOrderId = reader["cloud_order_id"]?.ToString(),
            
            // Customer information
            CustomerName = reader["customer_name"].ToString() ?? "",
            CustomerPhone = reader["customer_phone"]?.ToString(),
            CustomerEmail = reader["customer_email"]?.ToString(),
            CustomerAddress = reader["customer_address"]?.ToString(),
            TableSessionId = HasColumn(reader, "table_session_id") && reader["table_session_id"] != DBNull.Value
                ? Convert.ToInt32(reader["table_session_id"])
                : null,
            
            // Financial information
            TotalAmount = reader["total_amount"] != DBNull.Value ? Convert.ToDecimal(reader["total_amount"]) : 0,
            SubtotalAmount = reader["subtotal_amount"] != DBNull.Value ? Convert.ToDecimal(reader["subtotal_amount"]) : 0,
            DiscountAmount = HasColumn(reader, "discount_amount") && reader["discount_amount"] != DBNull.Value
                ? Convert.ToDecimal(reader["discount_amount"])
                : 0,
            DeliveryFee = reader["delivery_fee"] != DBNull.Value ? Convert.ToDecimal(reader["delivery_fee"]) : 0,
            ServiceChargePercentage = HasColumn(reader, "service_charge_percentage") && reader["service_charge_percentage"] != DBNull.Value
                ? Convert.ToDecimal(reader["service_charge_percentage"])
                : 0,
            ServiceChargeBasis = HasColumn(reader, "service_charge_basis") && reader["service_charge_basis"] != DBNull.Value
                ? Convert.ToDecimal(reader["service_charge_basis"])
                : 0,
            ServiceChargeAmount = HasColumn(reader, "service_charge_amount") && reader["service_charge_amount"] != DBNull.Value
                ? Convert.ToDecimal(reader["service_charge_amount"])
                : 0,
            ServiceChargeStatus = HasColumn(reader, "service_charge_status")
                ? reader["service_charge_status"]?.ToString() ?? "not_configured"
                : "not_configured",
            ServiceChargeClassification = HasColumn(reader, "service_charge_classification")
                ? reader["service_charge_classification"]?.ToString()
                : null,
            ServiceChargeRemovalReason = HasColumn(reader, "service_charge_removal_reason") ? reader["service_charge_removal_reason"]?.ToString() : null,
            ServiceChargeRemovedByUserId = HasColumn(reader, "service_charge_removed_by_user_id") && reader["service_charge_removed_by_user_id"] != DBNull.Value
                ? Convert.ToInt32(reader["service_charge_removed_by_user_id"])
                : null,
            ServiceChargeRemovedByName = HasColumn(reader, "service_charge_removed_by_name") ? reader["service_charge_removed_by_name"]?.ToString() : null,
            ServiceChargeApprovedByUserId = HasColumn(reader, "service_charge_approved_by_user_id") && reader["service_charge_approved_by_user_id"] != DBNull.Value
                ? Convert.ToInt32(reader["service_charge_approved_by_user_id"])
                : null,
            ServiceChargeApprovedByName = HasColumn(reader, "service_charge_approved_by_name") ? reader["service_charge_approved_by_name"]?.ToString() : null,
            ServiceChargeRemovedAt = HasColumn(reader, "service_charge_removed_at") && reader["service_charge_removed_at"] != DBNull.Value
                ? Convert.ToDateTime(reader["service_charge_removed_at"])
                : null,
            TaxAmount = reader["tax_amount"] != DBNull.Value ? Convert.ToDecimal(reader["tax_amount"]) : 0,
            CashTipAmount = HasColumn(reader, "cash_tip_amount") && reader["cash_tip_amount"] != DBNull.Value
                ? Convert.ToDecimal(reader["cash_tip_amount"])
                : 0,
            CardTipAmount = HasColumn(reader, "card_tip_amount") && reader["card_tip_amount"] != DBNull.Value
                ? Convert.ToDecimal(reader["card_tip_amount"])
                : 0,
            
            // Order details
            OrderType = NormalizeOrderType(reader["order_type"]?.ToString()),
            SourceChannel = NormalizeSourceChannel(HasColumn(reader, "source_channel") ? reader["source_channel"]?.ToString() : null),
            PaymentMethod = reader["payment_method"]?.ToString(),
            PaymentStatusRaw = HasColumn(reader, "payment_status") ? reader["payment_status"]?.ToString() : null,
            AmountPaid = HasColumn(reader, "amount_paid") && reader["amount_paid"] != DBNull.Value
                ? Convert.ToDecimal(reader["amount_paid"])
                : null,
            PaymentProvider = HasColumn(reader, "payment_provider") ? reader["payment_provider"]?.ToString() : null,
            TransactionId = HasColumn(reader, "payment_reference") ? reader["payment_reference"]?.ToString() : null,
            CurrencyCode = HasColumn(reader, "payment_currency") ? reader["payment_currency"]?.ToString() : null,
            VoucherCode = HasColumn(reader, "voucher_code") ? reader["voucher_code"]?.ToString() : null,
            PromoCode = HasColumn(reader, "promo_code") ? reader["promo_code"]?.ToString() : null,
            GiftCardNumberMasked = HasColumn(reader, "gift_card_number_masked") ? reader["gift_card_number_masked"]?.ToString() : null,
            GiftCardAmountPaid = HasColumn(reader, "gift_card_amount_paid") && reader["gift_card_amount_paid"] != DBNull.Value
                ? Convert.ToDecimal(reader["gift_card_amount_paid"])
                : null,
            GiftCardRemainingBalance = HasColumn(reader, "gift_card_remaining_balance") && reader["gift_card_remaining_balance"] != DBNull.Value
                ? Convert.ToDecimal(reader["gift_card_remaining_balance"])
                : null,
            LoyaltyPointsEarned = HasColumn(reader, "loyalty_points_earned") && reader["loyalty_points_earned"] != DBNull.Value
                ? Convert.ToInt32(reader["loyalty_points_earned"])
                : 0,
            LoyaltyPointsRedeemed = HasColumn(reader, "loyalty_points_redeemed") && reader["loyalty_points_redeemed"] != DBNull.Value
                ? Convert.ToInt32(reader["loyalty_points_redeemed"])
                : 0,
            LoyaltyPointsDiscount = HasColumn(reader, "loyalty_points_discount") && reader["loyalty_points_discount"] != DBNull.Value
                ? Convert.ToDecimal(reader["loyalty_points_discount"])
                : 0,
            LoyaltyBalanceAfter = HasColumn(reader, "loyalty_balance_after") && reader["loyalty_balance_after"] != DBNull.Value
                ? Convert.ToInt32(reader["loyalty_balance_after"])
                : null,
            PaymentStatus = OnlineOrderPaymentHelper.ToPaymentStatus(
                reader["payment_method"]?.ToString(),
                HasColumn(reader, "payment_status") ? reader["payment_status"]?.ToString() : null),
            SpecialInstructions = reader["special_instructions"]?.ToString(),
            ScheduledTime = reader["scheduled_time"] as DateTime?,
            
            // Status and timing
            Status = Enum.Parse<OrderStatus>(reader["status"].ToString() ?? "New", true),
            SyncStatus = Enum.Parse<Models.SyncStatus>(reader["sync_status"].ToString() ?? "Pending", true),
            LocalLifecycleState = HasColumn(reader, "local_lifecycle_state")
                ? ParseLocalLifecycleState(reader["local_lifecycle_state"]?.ToString())
                : LocalLifecycleState.Draft,
            IsOpen = HasColumn(reader, "is_open")
                ? Convert.ToBoolean(reader["is_open"])
                : !string.Equals(reader["status"]?.ToString(), "completed", StringComparison.OrdinalIgnoreCase),
            VoidReason = HasColumn(reader, "void_reason") ? reader["void_reason"]?.ToString() : null,
            VoidedAt = HasColumn(reader, "voided_at") ? reader["voided_at"] as DateTime? : null,
            VoidedBy = HasColumn(reader, "voided_by") ? reader["voided_by"]?.ToString() : null,
            PaidAt = HasColumn(reader, "paid_at") ? reader["paid_at"] as DateTime? : null,
            FirstSentAt = HasColumn(reader, "first_sent_at") ? reader["first_sent_at"] as DateTime? : null,
            LastSentAt = HasColumn(reader, "last_sent_at") ? reader["last_sent_at"] as DateTime? : null,
            SendAttemptCount = HasColumn(reader, "send_attempt_count") ? Convert.ToInt32(reader["send_attempt_count"]) : 0,
            SendFailureCount = HasColumn(reader, "send_failure_count") ? Convert.ToInt32(reader["send_failure_count"]) : 0,
            SendLatencyMs = HasColumn(reader, "send_latency_ms") && reader["send_latency_ms"] != DBNull.Value
                ? Convert.ToInt32(reader["send_latency_ms"])
                : null,
            FirstPaymentAttemptAt = HasColumn(reader, "first_payment_attempt_at") ? reader["first_payment_attempt_at"] as DateTime? : null,
            PaymentAttemptCount = HasColumn(reader, "payment_attempt_count") ? Convert.ToInt32(reader["payment_attempt_count"]) : 0,
            PaymentCompletionSeconds = HasColumn(reader, "payment_completion_seconds") && reader["payment_completion_seconds"] != DBNull.Value
                ? Convert.ToInt32(reader["payment_completion_seconds"])
                : null,
            OperationalLastEventAt = HasColumn(reader, "operational_last_event_at") ? reader["operational_last_event_at"] as DateTime? : null,
            DraftAbandonedFlag = HasColumn(reader, "draft_abandoned_flag") && Convert.ToBoolean(reader["draft_abandoned_flag"]),
            DraftAbandonedAt = HasColumn(reader, "draft_abandoned_at") ? reader["draft_abandoned_at"] as DateTime? : null,
            KitchenTime = reader["kitchen_time"] as DateTime?,
            PreparingTime = reader["preparing_time"] as DateTime?,
            ReadyTime = reader["ready_time"] as DateTime?,
            DeliveringTime = reader["delivering_time"] as DateTime?,
            CompletedTime = reader["completed_time"] as DateTime?,
            CreatedAt = Convert.ToDateTime(reader["created_at"]),
            UpdatedAt = Convert.ToDateTime(reader["updated_at"]),
            OrderData = reader["order_data"]?.ToString()
        };
    }

    private static DateTime NormalizeTimestampForDb(DateTime value)
    {
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Kind);
    }

    private static string GetCurrentTerminalName()
    {
        try
        {
            return TerminalConfigurationService.GetConfiguration().TerminalName;
        }
        catch
        {
            return "Terminal";
        }
    }

    private static void UpdateActiveTableCache(Order order)
    {
        if (!string.Equals(order.SourceChannel, "local", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(order.OrderType, "table", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var tableNumber = ParseTableNumberFromCustomerName(order.CustomerName);
        var updatedAt = order.UpdatedAt == default ? DateTime.Now : order.UpdatedAt;
        ActiveTableOrderCacheService.Upsert(order.OrderId, order.TableSessionId, tableNumber, updatedAt, order.IsOpen);
    }

    private static string? ParseTableNumberFromCustomerName(string? customerName)
    {
        if (string.IsNullOrWhiteSpace(customerName))
        {
            return null;
        }

        const string prefix = "Table ";
        if (!customerName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parsed = customerName[prefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(parsed) ? null : parsed;
    }

    private static bool HasColumn(MySqlDataReader reader, string columnName)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
