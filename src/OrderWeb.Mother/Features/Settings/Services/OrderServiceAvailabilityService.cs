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
            const string sql = """
                SELECT table_enabled, collection_enabled, delivery_enabled,
                       updated_by_user_id, updated_by_name, updated_at
                FROM order_service_availability_settings
                WHERE id = 1
                """;
            await using var command = new MySqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Order service settings are missing. Run database migration 029.");
            }

            _cached = Read(reader);
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
        _ => false
    };

    public bool IsRouteEnabled(string? route)
    {
        var normalized = route?.Trim().Trim('/').ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            "restaurant" or "visuallayout" or "floor" or "table" or "reservation" => _cached.TableEnabled,
            "collection" => _cached.CollectionEnabled,
            "delivery" or "weborders" => _cached.DeliveryEnabled,
            _ => true
        };
    }

    public async Task<OrderServiceAvailabilitySettings> SaveAsync(
        bool tableEnabled,
        bool collectionEnabled,
        bool deliveryEnabled,
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
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var previous = await ReadForUpdateAsync(connection, transaction, cancellationToken);
            var actorName = !string.IsNullOrWhiteSpace(actor.Name) ? actor.Name : actor.Username;

            const string updateSql = """
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
                update.Parameters.AddWithValue("@userId", actor.Id > 0 ? actor.Id : DBNull.Value);
                update.Parameters.AddWithValue("@userName", actorName);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            const string auditSql = """
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

    private static async Task<OrderServiceAvailabilitySettings> ReadForUpdateAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT table_enabled, collection_enabled, delivery_enabled,
                   updated_by_user_id, updated_by_name, updated_at
            FROM order_service_availability_settings
            WHERE id = 1
            FOR UPDATE
            """;
        await using var command = new MySqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Order service settings are missing. Run database migration 029.");
        }

        return Read(reader);
    }

    private static OrderServiceAvailabilitySettings Read(MySqlDataReader reader) => new()
    {
        TableEnabled = reader.GetBoolean("table_enabled"),
        CollectionEnabled = reader.GetBoolean("collection_enabled"),
        DeliveryEnabled = reader.GetBoolean("delivery_enabled"),
        UpdatedByUserId = reader["updated_by_user_id"] == DBNull.Value ? null : reader.GetInt32("updated_by_user_id"),
        UpdatedByName = reader["updated_by_name"] == DBNull.Value ? null : reader.GetString("updated_by_name"),
        UpdatedAt = reader.GetDateTime("updated_at")
    };
}
