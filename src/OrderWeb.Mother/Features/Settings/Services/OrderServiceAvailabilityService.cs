using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class OrderServiceAvailabilityService
{
    private readonly DatabaseService _databaseService;
    private readonly AuthenticationService _authenticationService;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private OrderServiceAvailabilitySettings _cached = new();
    private bool _hasLoaded;
    private bool _reservationColumnsEnsured;

    public event EventHandler<OrderServiceAvailabilitySettings>? SettingsChanged;

    public OrderServiceAvailabilityService(
        DatabaseService databaseService,
        AuthenticationService authenticationService)
    {
        _databaseService = databaseService;
        _authenticationService = authenticationService;
    }

    public OrderServiceAvailabilitySettings Current => _cached.Copy();

    public async Task<OrderServiceAvailabilitySettings> GetAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (_hasLoaded && !forceRefresh)
        {
            return Current;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_hasLoaded && !forceRefresh)
            {
                return Current;
            }

            await using var connection = await _databaseService.GetConnectionAsync();
            await EnsureReservationColumnsAsync(connection, cancellationToken);

            _cached = await ReadSettingsAsync(connection, transaction: null, forUpdate: false, cancellationToken);
            _hasLoaded = true;
            return Current;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public bool IsEnabled(PosOrderService service) => service switch
    {
        PosOrderService.Table => _cached.TableEnabled,
        PosOrderService.Collection => _cached.CollectionEnabled,
        PosOrderService.Delivery => _cached.DeliveryEnabled,
        PosOrderService.Reservation => _cached.ReservationEnabled,
        _ => false
    };

    public bool IsRouteEnabled(string? route)
    {
        var normalized = route?.Trim().Trim('/').ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            "restaurant" or "visuallayout" or "layout" or "floor" or "table" => _cached.TableEnabled,
            "reservation" => _cached.ReservationEnabled,
            "collection" => _cached.CollectionEnabled,
            "delivery" or "weborders" => _cached.DeliveryEnabled,
            _ => true
        };
    }

    /// <summary>
    /// Client menus need Terminal Access AND the matching Order Service switch on.
    /// </summary>
    public IReadOnlySet<string> FilterClientFeatures(IReadOnlySet<string> granted)
    {
        var filtered = new HashSet<string>(granted, StringComparer.OrdinalIgnoreCase);
        if (!_cached.TableEnabled)
        {
            filtered.Remove(OrderWeb.Contracts.Features.PosFeatureKeys.DineIn);
        }

        if (!_cached.CollectionEnabled)
        {
            filtered.Remove(OrderWeb.Contracts.Features.PosFeatureKeys.Collection);
        }

        if (!_cached.DeliveryEnabled)
        {
            filtered.Remove(OrderWeb.Contracts.Features.PosFeatureKeys.Delivery);
        }

        if (!_cached.ReservationEnabled)
        {
            filtered.Remove(OrderWeb.Contracts.Features.PosFeatureKeys.Reservations);
        }

        return filtered;
    }

    public async Task<OrderServiceAvailabilitySettings> SaveAsync(
        bool tableEnabled,
        bool collectionEnabled,
        bool deliveryEnabled,
        bool reservationEnabled,
        CancellationToken cancellationToken = default)
    {
        var actor = _authenticationService.CurrentUser;
        if (actor?.Role != UserRole.Admin)
        {
            throw new UnauthorizedAccessException("Only an Administrator can change order services.");
        }

        if (!tableEnabled && !collectionEnabled && !deliveryEnabled)
        {
            throw new ArgumentException("At least one order service must remain enabled.");
        }

        await using var connection = await _databaseService.GetConnectionAsync();
        await EnsureReservationColumnsAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var previous = await ReadSettingsAsync(connection, transaction, forUpdate: true, cancellationToken);
            var actorName = !string.IsNullOrWhiteSpace(actor.Name) ? actor.Name : actor.Username;
            var hasReservationColumn = await ColumnExistsAsync(
                connection,
                "order_service_availability_settings",
                "reservation_enabled",
                cancellationToken);

            var updateSql = hasReservationColumn
                ? """
                    UPDATE order_service_availability_settings
                    SET table_enabled = @tableEnabled,
                        collection_enabled = @collectionEnabled,
                        delivery_enabled = @deliveryEnabled,
                        reservation_enabled = @reservationEnabled,
                        updated_by_user_id = @userId,
                        updated_by_name = @userName,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE id = 1
                    """
                : """
                    UPDATE order_service_availability_settings
                    SET table_enabled = @tableEnabled,
                        collection_enabled = @collectionEnabled,
                        delivery_enabled = @deliveryEnabled,
                        updated_by_user_id = @userId,
                        updated_by_name = @userName,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE id = 1
                    """;

            await using (var update = new MySqlCommand(updateSql, connection, transaction))
            {
                update.Parameters.AddWithValue("@tableEnabled", tableEnabled);
                update.Parameters.AddWithValue("@collectionEnabled", collectionEnabled);
                update.Parameters.AddWithValue("@deliveryEnabled", deliveryEnabled);
                if (hasReservationColumn)
                {
                    update.Parameters.AddWithValue("@reservationEnabled", reservationEnabled);
                }

                update.Parameters.AddWithValue("@userId", actor.Id > 0 ? actor.Id : DBNull.Value);
                update.Parameters.AddWithValue("@userName", actorName);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            var hasReservationAudit = await ColumnExistsAsync(
                connection,
                "order_service_availability_events",
                "previous_reservation_enabled",
                cancellationToken);

            var auditSql = hasReservationAudit
                ? """
                    INSERT INTO order_service_availability_events
                        (previous_table_enabled, new_table_enabled,
                         previous_collection_enabled, new_collection_enabled,
                         previous_delivery_enabled, new_delivery_enabled,
                         previous_reservation_enabled, new_reservation_enabled,
                         changed_by_user_id, changed_by_name)
                    VALUES
                        (@previousTable, @newTable,
                         @previousCollection, @newCollection,
                         @previousDelivery, @newDelivery,
                         @previousReservation, @newReservation,
                         @userId, @userName)
                    """
                : """
                    INSERT INTO order_service_availability_events
                        (previous_table_enabled, new_table_enabled,
                         previous_collection_enabled, new_collection_enabled,
                         previous_delivery_enabled, new_delivery_enabled,
                         changed_by_user_id, changed_by_name)
                    VALUES
                        (@previousTable, @newTable,
                         @previousCollection, @newCollection,
                         @previousDelivery, @newDelivery,
                         @userId, @userName)
                    """;

            await using (var audit = new MySqlCommand(auditSql, connection, transaction))
            {
                audit.Parameters.AddWithValue("@previousTable", previous.TableEnabled);
                audit.Parameters.AddWithValue("@newTable", tableEnabled);
                audit.Parameters.AddWithValue("@previousCollection", previous.CollectionEnabled);
                audit.Parameters.AddWithValue("@newCollection", collectionEnabled);
                audit.Parameters.AddWithValue("@previousDelivery", previous.DeliveryEnabled);
                audit.Parameters.AddWithValue("@newDelivery", deliveryEnabled);
                if (hasReservationAudit)
                {
                    audit.Parameters.AddWithValue("@previousReservation", previous.ReservationEnabled);
                    audit.Parameters.AddWithValue("@newReservation", reservationEnabled);
                }

                audit.Parameters.AddWithValue("@userId", actor.Id > 0 ? actor.Id : DBNull.Value);
                audit.Parameters.AddWithValue("@userName", actorName);
                await audit.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            _cached = new OrderServiceAvailabilitySettings
            {
                TableEnabled = tableEnabled,
                CollectionEnabled = collectionEnabled,
                DeliveryEnabled = deliveryEnabled,
                ReservationEnabled = reservationEnabled,
                UpdatedByUserId = actor.Id > 0 ? actor.Id : null,
                UpdatedByName = actorName,
                UpdatedAt = DateTime.Now
            };
            _hasLoaded = true;
            SettingsChanged?.Invoke(this, Current);
            return Current;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task EnsureReservationColumnsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (_reservationColumnsEnsured || RuntimeSchemaPolicy.IsMigrationManaged)
        {
            return;
        }

        try
        {
            await using (var settings = new MySqlCommand(
                """
                ALTER TABLE order_service_availability_settings
                    ADD COLUMN IF NOT EXISTS reservation_enabled BOOLEAN NOT NULL DEFAULT TRUE
                        AFTER delivery_enabled
                """,
                connection))
            {
                await settings.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var events = new MySqlCommand(
                """
                ALTER TABLE order_service_availability_events
                    ADD COLUMN IF NOT EXISTS previous_reservation_enabled BOOLEAN NOT NULL DEFAULT TRUE
                        AFTER new_delivery_enabled,
                    ADD COLUMN IF NOT EXISTS new_reservation_enabled BOOLEAN NOT NULL DEFAULT TRUE
                        AFTER previous_reservation_enabled
                """,
                connection))
            {
                await events.ExecuteNonQueryAsync(cancellationToken);
            }

            _reservationColumnsEnsured = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OrderServices] reservation column ensure skipped: {ex.Message}");
        }
    }

    private static async Task<OrderServiceAvailabilitySettings> ReadSettingsAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var hasReservation = await ColumnExistsAsync(
            connection,
            "order_service_availability_settings",
            "reservation_enabled",
            cancellationToken);

        var sql = hasReservation
            ? """
                SELECT table_enabled, collection_enabled, delivery_enabled, reservation_enabled,
                       updated_by_user_id, updated_by_name, updated_at
                FROM order_service_availability_settings
                WHERE id = 1
                """
            : """
                SELECT table_enabled, collection_enabled, delivery_enabled,
                       updated_by_user_id, updated_by_name, updated_at
                FROM order_service_availability_settings
                WHERE id = 1
                """;

        if (forUpdate)
        {
            sql += " FOR UPDATE";
        }

        await using var command = transaction is null
            ? new MySqlCommand(sql, connection)
            : new MySqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Order service settings are missing. Run database migration 029.");
        }

        return Read(reader, hasReservation);
    }

    private static async Task<bool> ColumnExistsAsync(
        MySqlConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @tableName
              AND COLUMN_NAME = @columnName
            """;
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@columnName", columnName);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return count > 0;
    }

    private static OrderServiceAvailabilitySettings Read(MySqlDataReader reader, bool hasReservationColumn)
    {
        var reservationEnabled = true;
        if (hasReservationColumn)
        {
            try
            {
                var ordinal = reader.GetOrdinal("reservation_enabled");
                if (!reader.IsDBNull(ordinal))
                {
                    reservationEnabled = reader.GetBoolean(ordinal);
                }
            }
            catch (IndexOutOfRangeException)
            {
                reservationEnabled = true;
            }
        }

        return new OrderServiceAvailabilitySettings
        {
            TableEnabled = reader.GetBoolean("table_enabled"),
            CollectionEnabled = reader.GetBoolean("collection_enabled"),
            DeliveryEnabled = reader.GetBoolean("delivery_enabled"),
            ReservationEnabled = reservationEnabled,
            UpdatedByUserId = reader["updated_by_user_id"] == DBNull.Value ? null : reader.GetInt32("updated_by_user_id"),
            UpdatedByName = reader["updated_by_name"] == DBNull.Value ? null : reader.GetString("updated_by_name"),
            UpdatedAt = reader.GetDateTime("updated_at")
        };
    }
}
