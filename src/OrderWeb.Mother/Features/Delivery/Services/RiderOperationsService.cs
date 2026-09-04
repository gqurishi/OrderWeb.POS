using System.Globalization;
using System.Net;
using System.Text.Json;
using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class RiderOperationsService
{
    private readonly DatabaseService _databaseService;
    private readonly OrderWebApiClient _apiClient;

    public RiderOperationsService(DatabaseService databaseService, OrderWebApiClient apiClient)
    {
        _databaseService = databaseService;
        _apiClient = apiClient;
    }

    public async Task<IReadOnlyList<RiderOperation>> GetBoardAsync(DateTime localNow, CancellationToken cancellationToken = default)
    {
        var start = RiderOperationPolicy.GetDayStart(localNow);
        var end = RiderOperationPolicy.GetDayEnd(localNow);
        var rows = new List<RiderOperation>();

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            """
            SELECT o.id, o.order_id, o.order_number, o.cloud_order_id, o.source_channel,
                   o.customer_name, o.customer_phone, o.customer_address, o.total_amount,
                   o.payment_method, o.payment_status,
                   COALESCE(o.amount_paid, (
                       SELECT SUM(op.amount) FROM order_payments op
                       WHERE op.order_id = o.id AND op.status = 'approved'
                   )) AS amount_paid,
                   o.status AS order_status,
                   o.local_lifecycle_state, o.created_at, o.scheduled_time, o.first_sent_at,
                   r.id AS rider_job_id, r.operation_status, r.quote_id, r.quote_amount,
                   r.quote_currency, r.quote_expires_at, r.estimated_pickup_at,
                   r.estimated_delivery_at, r.provider_name, r.dispatch_id, r.rider_name,
                   r.rider_phone, r.cash_collection_amount, r.last_error
            FROM orders o
            LEFT JOIN rider_dispatch_jobs r ON r.order_db_id = o.id
            WHERE LOWER(COALESCE(o.order_type, '')) IN ('delivery', 'del')
              AND COALESCE(o.draft_abandoned_flag, 0) = 0
              AND EXISTS (
                  SELECT 1 FROM order_items oi
                  WHERE oi.order_id = o.id AND oi.quantity > 0 AND TRIM(oi.item_name) <> ''
              )
              AND (
                  (o.source_channel = 'web' OR o.local_lifecycle_state IN ('sent_partial', 'sent_full', 'payment_partial', 'paid'))
              )
              AND (
                  (o.created_at >= @dayStart AND o.created_at < @dayEnd)
                  OR COALESCE(r.operation_status, '') NOT IN ('delivered', 'cancelled')
                  OR (r.id IS NULL AND o.status NOT IN ('completed', 'cancelled'))
              )
            ORDER BY
              CASE COALESCE(r.operation_status, '')
                WHEN 'dispatch_check_required' THEN 0 WHEN 'quote_failed' THEN 0
                WHEN 'ready_for_rider' THEN 1 WHEN 'quote_available' THEN 1
                WHEN 'rider_assigned' THEN 2 WHEN 'delivering' THEN 2
                WHEN 'delivered' THEN 5 WHEN 'cancelled' THEN 6 ELSE 3
              END,
              COALESCE(o.scheduled_time, o.created_at), o.id
            """,
            connection);
        command.Parameters.AddWithValue("@dayStart", start);
        command.Parameters.AddWithValue("@dayEnd", end);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(MapOperation(reader));
        }

        return rows;
    }

    public async Task RefreshActiveDispatchesAsync(CancellationToken cancellationToken = default)
    {
        var config = await _apiClient.GetConfigAsync();
        if (config == null) return;

        var references = new List<DispatchReference>();
        await using (var connection = await _databaseService.GetConnectionAsync())
        await using (var command = new MySqlCommand(
            """
            SELECT order_db_id, dispatch_id, dispatch_idempotency_key
            FROM rider_dispatch_jobs
            WHERE operation_status NOT IN ('delivered', 'cancelled')
              AND (dispatch_id IS NOT NULL OR dispatch_idempotency_key IS NOT NULL)
            ORDER BY updated_at
            LIMIT 50
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                references.Add(new DispatchReference(
                    reader.GetInt32("order_db_id"),
                    ReadString(reader, "dispatch_id"),
                    ReadString(reader, "dispatch_idempotency_key")));
        }

        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var lookup = !string.IsNullOrWhiteSpace(reference.DispatchId)
                    ? $"/pos/rider/dispatches/{Uri.EscapeDataString(reference.DispatchId)}"
                    : $"/pos/rider/dispatches/by-idempotency/{Uri.EscapeDataString(reference.IdempotencyKey)}";
                var path = $"{lookup}?tenant={Uri.EscapeDataString(config.TenantSlug)}";
                using var request = _apiClient.CreateRequest(config, HttpMethod.Get, path);
                using var response = await _apiClient.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.NotFound) continue;
                if (!response.IsSuccessStatusCode) continue;
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var dispatch = ParseDispatch(body);
                if (!string.IsNullOrWhiteSpace(dispatch.DispatchId))
                    await SaveDispatchAsync(reference.OrderDbId, dispatch, body, actor: null, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Rider status refresh failed for order {reference.OrderDbId}: {ex.Message}");
            }
        }
    }

    public async Task<RiderQuoteResult> RequestQuoteAsync(int orderDbId, User? actor, CancellationToken cancellationToken = default)
    {
        var operation = await GetOperationAsync(orderDbId, cancellationToken);
        if (operation == null) return new RiderQuoteResult(false, "This delivery order no longer exists.");

        if (operation.OperationStatus == RiderOperationPolicy.QuoteAvailable &&
            operation.QuoteExpiresAt.HasValue && operation.QuoteExpiresAt.Value <= DateTime.Now)
        {
            await SetStatusAsync(orderDbId, RiderOperationPolicy.QuoteExpired, actor, "The previous quotation expired.", cancellationToken);
            operation = await GetOperationAsync(orderDbId, cancellationToken) ?? operation;
        }

        var validation = ValidateForDispatch(operation);
        if (!validation.Success) return new RiderQuoteResult(false, validation.Message, operation);
        if (!RiderOperationPolicy.CanRequestQuote(operation.OperationStatus))
            return new RiderQuoteResult(false, "This order already has an active rider request.", operation);

        var config = await _apiClient.GetConfigAsync();
        if (config == null)
            return new RiderQuoteResult(false, "OrderWeb rider service is not configured. Check Cloud Settings.", operation);

        var deviceId = await _apiClient.GetDeviceIdAsync();
        var pickup = await GetPickupAsync(cancellationToken);
        if (pickup == null || string.IsNullOrWhiteSpace(pickup.Address) || string.IsNullOrWhiteSpace(pickup.Postcode))
            return new RiderQuoteResult(false, "Add the restaurant pickup address and postcode in Business Information before requesting a rider.", operation);
        var idempotencyKey = OrderWebApiClient.BuildIdempotencyKey("rider-quote", config.TenantSlug, operation.OrderId);
        var cashToCollect = RiderOperationPolicy.CalculateCashCollection(
            operation.TotalAmount, operation.AmountPaid, operation.PaymentMethod, operation.PaymentStatus);

        await EnsureJobAsync(operation.OrderDbId, idempotencyKey, cashToCollect, actor, cancellationToken);
        await SetStatusAsync(operation.OrderDbId, RiderOperationPolicy.QuoteRequested, actor, null, cancellationToken);

        var payload = new
        {
            tenant = config.TenantSlug,
            pos_order_id = operation.OrderId,
            cloud_order_id = NullIfBlank(operation.CloudOrderId),
            order_number = operation.OrderNumber,
            source = operation.SourceChannel,
            pickup = new
            {
                name = pickup.Name,
                phone = pickup.Phone,
                address = pickup.Address,
                city = pickup.City,
                postcode = pickup.Postcode
            },
            customer = new
            {
                name = operation.CustomerName,
                phone = operation.CustomerPhone,
                address = operation.CustomerAddress
            },
            requested_delivery_at = operation.ScheduledTime?.ToUniversalTime().ToString("O"),
            order_value = operation.TotalAmount,
            currency = "GBP",
            payment_method = OnlineOrderPaymentHelper.GetStorageMethod(operation.PaymentMethod),
            payment_status = operation.PaymentStatus,
            cash_collection_amount = cashToCollect,
            device_id = deviceId,
            idempotency_key = idempotencyKey
        };

        try
        {
            using var request = _apiClient.CreateRequest(config, HttpMethod.Post, "/pos/rider/quotes", payload, idempotencyKey);
            using var response = await _apiClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var message = BuildApiError("Rider quote was not available", response.StatusCode, body);
                await SetStatusAsync(orderDbId, RiderOperationPolicy.QuoteFailed, actor, message, cancellationToken);
                return new RiderQuoteResult(false, message, await GetOperationAsync(orderDbId, cancellationToken));
            }

            var quote = ParseQuote(body);
            if (string.IsNullOrWhiteSpace(quote.QuoteId) || !quote.Amount.HasValue)
            {
                const string message = "OrderWeb returned an incomplete rider quote.";
                await SetStatusAsync(orderDbId, RiderOperationPolicy.QuoteFailed, actor, message, cancellationToken);
                return new RiderQuoteResult(false, message, await GetOperationAsync(orderDbId, cancellationToken));
            }

            await SaveQuoteAsync(orderDbId, quote, body, actor, cancellationToken);
            return new RiderQuoteResult(true, "Rider quote received.", await GetOperationAsync(orderDbId, cancellationToken));
        }
        catch (Exception ex)
        {
            var message = $"Could not reach OrderWeb: {ex.Message}";
            await SetStatusAsync(orderDbId, RiderOperationPolicy.QuoteFailed, actor, message, cancellationToken);
            return new RiderQuoteResult(false, message, await GetOperationAsync(orderDbId, cancellationToken));
        }
    }

    public async Task<RiderDispatchResult> ConfirmDispatchAsync(int orderDbId, User? actor, CancellationToken cancellationToken = default)
    {
        var operation = await GetOperationAsync(orderDbId, cancellationToken);
        if (operation == null) return new RiderDispatchResult(false, "This delivery order no longer exists.");
        if (!RiderOperationPolicy.CanConfirmQuote(operation.OperationStatus, operation.QuoteId, operation.QuoteExpiresAt, DateTime.Now))
        {
            if (operation.QuoteExpiresAt.HasValue && operation.QuoteExpiresAt.Value <= DateTime.Now)
                await SetStatusAsync(orderDbId, RiderOperationPolicy.QuoteExpired, actor, "The quotation expired before confirmation.", cancellationToken);
            return new RiderDispatchResult(false, "The rider quote has expired. Request a new quote.", await GetOperationAsync(orderDbId, cancellationToken));
        }

        var config = await _apiClient.GetConfigAsync();
        if (config == null) return new RiderDispatchResult(false, "OrderWeb rider service is not configured.", operation);

        var idempotencyKey = OrderWebApiClient.BuildIdempotencyKey("rider-dispatch", config.TenantSlug, operation.OrderId, operation.QuoteId);
        await StoreDispatchAttemptAsync(orderDbId, idempotencyKey, actor, cancellationToken);
        var payload = new
        {
            tenant = config.TenantSlug,
            quote_id = operation.QuoteId,
            pos_order_id = operation.OrderId,
            cloud_order_id = NullIfBlank(operation.CloudOrderId),
            cash_collection_amount = operation.CashCollectionAmount,
            idempotency_key = idempotencyKey
        };

        try
        {
            using var request = _apiClient.CreateRequest(config, HttpMethod.Post, "/pos/rider/dispatches", payload, idempotencyKey);
            using var response = await _apiClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var message = BuildApiError("Rider dispatch could not be confirmed", response.StatusCode, body);
                var uncertain = (int)response.StatusCode >= 500;
                await SetStatusAsync(orderDbId, uncertain ? RiderOperationPolicy.DispatchCheckRequired : RiderOperationPolicy.QuoteAvailable, actor, message, cancellationToken);
                return new RiderDispatchResult(false, message, await GetOperationAsync(orderDbId, cancellationToken));
            }

            var dispatch = ParseDispatch(body);
            if (string.IsNullOrWhiteSpace(dispatch.DispatchId))
            {
                const string message = "OrderWeb accepted the request but did not return a dispatch ID. Check the existing request before retrying.";
                await SetStatusAsync(orderDbId, RiderOperationPolicy.DispatchCheckRequired, actor, message, cancellationToken);
                return new RiderDispatchResult(false, message, await GetOperationAsync(orderDbId, cancellationToken));
            }

            await SaveDispatchAsync(orderDbId, dispatch, body, actor, cancellationToken);
            return new RiderDispatchResult(true, "Rider request confirmed.", await GetOperationAsync(orderDbId, cancellationToken));
        }
        catch (Exception ex)
        {
            var message = $"The response was interrupted. The POS will not send a duplicate request: {ex.Message}";
            await SetStatusAsync(orderDbId, RiderOperationPolicy.DispatchCheckRequired, actor, message, cancellationToken);
            return new RiderDispatchResult(false, message, await GetOperationAsync(orderDbId, cancellationToken));
        }
    }

    private async Task<RiderOperation?> GetOperationAsync(int orderDbId, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            """
            SELECT o.id, o.order_id, o.order_number, o.cloud_order_id, o.source_channel,
                   o.customer_name, o.customer_phone, o.customer_address, o.total_amount,
                   o.payment_method, o.payment_status,
                   COALESCE(o.amount_paid, (
                       SELECT SUM(op.amount) FROM order_payments op
                       WHERE op.order_id = o.id AND op.status = 'approved'
                   )) AS amount_paid,
                   o.status AS order_status,
                   o.local_lifecycle_state, o.created_at, o.scheduled_time, o.first_sent_at,
                   r.id AS rider_job_id, r.operation_status, r.quote_id, r.quote_amount,
                   r.quote_currency, r.quote_expires_at, r.estimated_pickup_at,
                   r.estimated_delivery_at, r.provider_name, r.dispatch_id, r.rider_name,
                   r.rider_phone, r.cash_collection_amount, r.last_error
            FROM orders o LEFT JOIN rider_dispatch_jobs r ON r.order_db_id = o.id
            WHERE o.id = @orderId AND LOWER(COALESCE(o.order_type, '')) IN ('delivery', 'del')
              AND EXISTS (SELECT 1 FROM order_items oi WHERE oi.order_id = o.id AND oi.quantity > 0 AND TRIM(oi.item_name) <> '')
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("@orderId", orderDbId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapOperation(reader) : null;
    }

    private async Task<PickupDetails?> GetPickupAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            """
            SELECT restaurant_name, phone_number, address, city, postcode
            FROM business_info ORDER BY id LIMIT 1
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new PickupDetails(
            ReadString(reader, "restaurant_name"),
            ReadString(reader, "phone_number"),
            ReadString(reader, "address"),
            ReadString(reader, "city"),
            ReadString(reader, "postcode"));
    }

    private static RiderOperation MapOperation(MySqlDataReader reader)
    {
        var total = ReadDecimal(reader, "total_amount") ?? 0m;
        var paymentMethod = ReadString(reader, "payment_method");
        var paymentStatus = ReadString(reader, "payment_status");
        var amountPaid = ReadDecimal(reader, "amount_paid");
        var storedCollection = ReadDecimal(reader, "cash_collection_amount");
        return new RiderOperation
        {
            OrderDbId = reader.GetInt32("id"),
            RiderJobId = ReadInt64(reader, "rider_job_id"),
            OrderId = ReadString(reader, "order_id"),
            OrderNumber = ReadString(reader, "order_number"),
            CloudOrderId = ReadString(reader, "cloud_order_id"),
            SourceChannel = ReadString(reader, "source_channel"),
            CustomerName = ReadString(reader, "customer_name"),
            CustomerPhone = ReadString(reader, "customer_phone"),
            CustomerAddress = ReadString(reader, "customer_address"),
            TotalAmount = total,
            PaymentMethod = paymentMethod,
            PaymentStatus = paymentStatus,
            AmountPaid = amountPaid,
            CashCollectionAmount = storedCollection ?? RiderOperationPolicy.CalculateCashCollection(total, amountPaid, paymentMethod, paymentStatus),
            OrderStatus = ReadString(reader, "order_status"),
            LifecycleState = ReadString(reader, "local_lifecycle_state"),
            CreatedAt = reader.GetDateTime("created_at"),
            ScheduledTime = ReadDateTime(reader, "scheduled_time"),
            FirstSentAt = ReadDateTime(reader, "first_sent_at"),
            OperationStatus = ReadString(reader, "operation_status", RiderOperationPolicy.AwaitingKitchen),
            QuoteId = ReadString(reader, "quote_id"),
            QuoteAmount = ReadDecimal(reader, "quote_amount"),
            QuoteCurrency = ReadString(reader, "quote_currency", "GBP"),
            QuoteExpiresAt = ReadDateTime(reader, "quote_expires_at"),
            EstimatedPickupAt = ReadDateTime(reader, "estimated_pickup_at"),
            EstimatedDeliveryAt = ReadDateTime(reader, "estimated_delivery_at"),
            ProviderName = ReadString(reader, "provider_name"),
            DispatchId = ReadString(reader, "dispatch_id"),
            RiderName = ReadString(reader, "rider_name"),
            RiderPhone = ReadString(reader, "rider_phone"),
            LastError = ReadString(reader, "last_error")
        };
    }

    private async Task EnsureJobAsync(int orderDbId, string idempotencyKey, decimal cashCollection, User? actor, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            """
            INSERT INTO rider_dispatch_jobs
                (order_db_id, operation_status, cash_collection_amount, quote_idempotency_key,
                 requested_by_user_id, requested_by_name, requested_at)
            VALUES (@orderId, 'awaiting_kitchen', @cash, @key, @userId, @userName, NOW())
            ON DUPLICATE KEY UPDATE
                cash_collection_amount = VALUES(cash_collection_amount),
                requested_by_user_id = VALUES(requested_by_user_id),
                requested_by_name = VALUES(requested_by_name),
                requested_at = NOW()
            """,
            connection);
        command.Parameters.AddWithValue("@orderId", orderDbId);
        command.Parameters.AddWithValue("@cash", cashCollection);
        command.Parameters.AddWithValue("@key", idempotencyKey);
        command.Parameters.AddWithValue("@userId", actor?.Id ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@userName", actor?.Name ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SetStatusAsync(int orderDbId, string status, User? actor, string? error, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var previous = string.Empty;
        long jobId;
        await using (var select = new MySqlCommand("SELECT id, operation_status FROM rider_dispatch_jobs WHERE order_db_id = @orderId FOR UPDATE", connection, transaction))
        {
            select.Parameters.AddWithValue("@orderId", orderDbId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Rider job was not created.");
            jobId = reader.GetInt64("id");
            previous = ReadString(reader, "operation_status");
        }

        await using (var update = new MySqlCommand("UPDATE rider_dispatch_jobs SET operation_status = @status, last_error = @error WHERE id = @jobId", connection, transaction))
        {
            update.Parameters.AddWithValue("@status", status);
            update.Parameters.AddWithValue("@error", error ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("@jobId", jobId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!string.Equals(previous, status, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(error))
            await InsertEventAsync(connection, transaction, jobId, orderDbId, "status_changed", previous, status, actor, error, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task SaveQuoteAsync(int orderDbId, ParsedQuote quote, string payload, User? actor, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var (jobId, previous) = await LockJobAsync(connection, transaction, orderDbId, cancellationToken);
        await using var update = new MySqlCommand(
            """
            UPDATE rider_dispatch_jobs SET operation_status = 'quote_available', quote_id = @quoteId,
                quote_amount = @amount, quote_currency = @currency, quote_expires_at = @expires,
                estimated_pickup_at = @pickup, estimated_delivery_at = @delivery,
                provider_name = @provider, provider_payload = @payload, last_error = NULL
            WHERE id = @jobId
            """, connection, transaction);
        update.Parameters.AddWithValue("@quoteId", quote.QuoteId);
        update.Parameters.AddWithValue("@amount", quote.Amount!.Value);
        update.Parameters.AddWithValue("@currency", quote.Currency);
        update.Parameters.AddWithValue("@expires", quote.ExpiresAt ?? (object)DBNull.Value);
        update.Parameters.AddWithValue("@pickup", quote.PickupAt ?? (object)DBNull.Value);
        update.Parameters.AddWithValue("@delivery", quote.DeliveryAt ?? (object)DBNull.Value);
        update.Parameters.AddWithValue("@provider", quote.Provider ?? (object)DBNull.Value);
        update.Parameters.AddWithValue("@payload", payload);
        update.Parameters.AddWithValue("@jobId", jobId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await InsertEventAsync(connection, transaction, jobId, orderDbId, "quote_received", previous, RiderOperationPolicy.QuoteAvailable, actor, payload, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task StoreDispatchAttemptAsync(int orderDbId, string key, User? actor, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand(
            """
            UPDATE rider_dispatch_jobs SET dispatch_idempotency_key = COALESCE(dispatch_idempotency_key, @key),
                confirmed_by_user_id = @userId, confirmed_by_name = @userName, confirmed_at = NOW()
            WHERE order_db_id = @orderId
            """, connection);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@userId", actor?.Id ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@userName", actor?.Name ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@orderId", orderDbId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SaveDispatchAsync(int orderDbId, ParsedDispatch dispatch, string payload, User? actor, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var (jobId, previous) = await LockJobAsync(connection, transaction, orderDbId, cancellationToken);
        await using var update = new MySqlCommand(
            """
            UPDATE rider_dispatch_jobs SET operation_status = @status, dispatch_id = @dispatchId,
                rider_name = @riderName, rider_phone = @riderPhone,
                provider_payload = @payload, last_error = NULL
            WHERE id = @jobId
            """, connection, transaction);
        update.Parameters.AddWithValue("@status", dispatch.Status);
        update.Parameters.AddWithValue("@dispatchId", dispatch.DispatchId);
        update.Parameters.AddWithValue("@riderName", dispatch.RiderName ?? (object)DBNull.Value);
        update.Parameters.AddWithValue("@riderPhone", dispatch.RiderPhone ?? (object)DBNull.Value);
        update.Parameters.AddWithValue("@payload", payload);
        update.Parameters.AddWithValue("@jobId", jobId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        if (!string.Equals(previous, dispatch.Status, StringComparison.OrdinalIgnoreCase))
            await InsertEventAsync(connection, transaction, jobId, orderDbId, "dispatch_confirmed", previous, dispatch.Status, actor, payload, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<(long JobId, string Previous)> LockJobAsync(MySqlConnection connection, MySqlTransaction transaction, int orderDbId, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand("SELECT id, operation_status FROM rider_dispatch_jobs WHERE order_db_id = @orderId FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("@orderId", orderDbId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Rider job was not created.");
        return (reader.GetInt64("id"), ReadString(reader, "operation_status"));
    }

    private static async Task InsertEventAsync(MySqlConnection connection, MySqlTransaction transaction, long jobId, int orderDbId, string eventType, string? previous, string? next, User? actor, string? details, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            """
            INSERT INTO rider_dispatch_events
                (rider_job_id, order_db_id, event_type, previous_status, new_status,
                 actor_user_id, actor_name, details_json)
            VALUES (@jobId, @orderId, @event, @previous, @next, @userId, @userName, @details)
            """, connection, transaction);
        command.Parameters.AddWithValue("@jobId", jobId);
        command.Parameters.AddWithValue("@orderId", orderDbId);
        command.Parameters.AddWithValue("@event", eventType);
        command.Parameters.AddWithValue("@previous", previous ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@next", next ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@userId", actor?.Id ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@userName", actor?.Name ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@details", ToJsonString(details) ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static (bool Success, string Message) ValidateForDispatch(RiderOperation operation)
    {
        if (string.IsNullOrWhiteSpace(operation.CustomerPhone)) return (false, "Add the customer's telephone number before requesting a rider.");
        if (string.IsNullOrWhiteSpace(operation.CustomerAddress)) return (false, "Add the complete delivery address before requesting a rider.");
        if (operation.TotalAmount <= 0) return (false, "The delivery total must be greater than zero.");
        return (true, string.Empty);
    }

    private static ParsedQuote ParseQuote(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = Unwrap(document.RootElement, "quote", "data");
        return new ParsedQuote(
            GetString(root, "quote_id", "id"),
            GetDecimal(root, "price", "amount", "quote_amount", "delivery_price"),
            GetString(root, "currency", "quote_currency") ?? "GBP",
            GetDateTime(root, "expires_at", "expiry"),
            GetDateTime(root, "estimated_pickup_at", "pickup_at"),
            GetDateTime(root, "estimated_delivery_at", "delivery_at"),
            GetString(root, "provider_name", "provider"));
    }

    private static ParsedDispatch ParseDispatch(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = Unwrap(document.RootElement, "dispatch", "job", "data");
        var status = NormalizeProviderStatus(GetString(root, "status"));
        JsonElement rider = root;
        if (TryGet(root, "rider", out var riderElement) && riderElement.ValueKind == JsonValueKind.Object) rider = riderElement;
        return new ParsedDispatch(
            GetString(root, "dispatch_id", "job_id", "id"),
            status,
            GetString(rider, "name", "rider_name"),
            GetString(rider, "phone", "rider_phone"));
    }

    private static string NormalizeProviderStatus(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "assigned" or "rider_assigned" => RiderOperationPolicy.RiderAssigned,
        "collected" or "picked_up" => RiderOperationPolicy.Collected,
        "delivering" or "on_the_way" => RiderOperationPolicy.Delivering,
        "delivered" or "completed" => RiderOperationPolicy.Delivered,
        "cancelled" or "canceled" => RiderOperationPolicy.Cancelled,
        _ => RiderOperationPolicy.FindingRider
    };

    private static JsonElement Unwrap(JsonElement root, params string[] properties)
    {
        foreach (var property in properties)
            if (TryGet(root, property, out var value) && value.ValueKind == JsonValueKind.Object) return value;
        return root;
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
        value = default;
        return false;
    }

    private static string? GetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (TryGet(root, name, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                return value.ToString();
        return null;
    }

    private static decimal? GetDecimal(JsonElement root, params string[] names)
    {
        var value = GetString(root, names);
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static DateTime? GetDateTime(JsonElement root, params string[] names)
    {
        var value = GetString(root, names);
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.ToLocalTime() : null;
    }

    private static string BuildApiError(string prefix, HttpStatusCode statusCode, string body)
    {
        var detail = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(body);
            detail = GetString(document.RootElement, "message", "error", "detail") ?? string.Empty;
        }
        catch { }
        return string.IsNullOrWhiteSpace(detail) ? $"{prefix} ({(int)statusCode})." : $"{prefix}: {detail}";
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string? ToJsonString(string? value) => string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Serialize(new { message = value });
    private static string ReadString(MySqlDataReader reader, string column, string fallback = "") { var i = reader.GetOrdinal(column); return reader.IsDBNull(i) ? fallback : Convert.ToString(reader.GetValue(i)) ?? fallback; }
    private static decimal? ReadDecimal(MySqlDataReader reader, string column) { var i = reader.GetOrdinal(column); return reader.IsDBNull(i) ? null : reader.GetDecimal(i); }
    private static DateTime? ReadDateTime(MySqlDataReader reader, string column) { var i = reader.GetOrdinal(column); return reader.IsDBNull(i) ? null : reader.GetDateTime(i); }
    private static long? ReadInt64(MySqlDataReader reader, string column) { var i = reader.GetOrdinal(column); return reader.IsDBNull(i) ? null : reader.GetInt64(i); }

    private sealed record ParsedQuote(string? QuoteId, decimal? Amount, string Currency, DateTime? ExpiresAt, DateTime? PickupAt, DateTime? DeliveryAt, string? Provider);
    private sealed record ParsedDispatch(string? DispatchId, string Status, string? RiderName, string? RiderPhone);
    private sealed record PickupDetails(string Name, string Phone, string Address, string City, string Postcode);
    private sealed record DispatchReference(int OrderDbId, string DispatchId, string IdempotencyKey);
}
