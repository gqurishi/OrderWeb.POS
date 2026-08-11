using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed class TableServiceChargeSettingsService
{
    private readonly DatabaseService _databaseService;
    private readonly AuthenticationService _authenticationService;

    public TableServiceChargeSettingsService(
        DatabaseService databaseService,
        AuthenticationService authenticationService)
    {
        _databaseService = databaseService;
        _authenticationService = authenticationService;
    }

    public async Task<TableServiceChargeSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _databaseService.GetConnectionAsync();
        const string sql = @"
            SELECT is_enabled, percentage, classification,
                   updated_by_user_id, updated_by_name, updated_at
            FROM table_service_charge_settings
            WHERE id = 1
            LIMIT 1";

        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Table service-charge settings are missing. Run database migration 028.");
        }

        return ReadSettings(reader);
    }

    public async Task<TableServiceChargeSaveResult> SaveAsync(
        bool isEnabled,
        decimal percentage,
        ServiceChargeClassification classification,
        CancellationToken cancellationToken = default)
    {
        var currentUser = _authenticationService.CurrentUser;
        if (currentUser is not { Role: UserRole.Admin })
        {
            throw new UnauthorizedAccessException("Only an Administrator can change table service-charge settings.");
        }

        var validation = TableServiceChargePolicy.Validate(isEnabled, percentage);
        if (!validation.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(percentage), validation.Message);
        }

        var actorName = !string.IsNullOrWhiteSpace(currentUser.Name)
            ? currentUser.Name.Trim()
            : !string.IsNullOrWhiteSpace(currentUser.Username)
                ? currentUser.Username.Trim()
                : $"User {currentUser.Id}";

        await using var connection = await _databaseService.GetConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var previous = await GetForUpdateAsync(connection, transaction, cancellationToken);
            var normalizedPercentage = validation.NormalizedPercentage;
            var changed = previous.IsEnabled != isEnabled
                || previous.Percentage != normalizedPercentage
                || previous.Classification != classification;

            if (!changed)
            {
                await transaction.CommitAsync(cancellationToken);
                return new TableServiceChargeSaveResult(previous, false);
            }

            const string updateSql = @"
                UPDATE table_service_charge_settings
                SET is_enabled = @isEnabled,
                    percentage = @percentage,
                    classification = @classification,
                    updated_by_user_id = @updatedByUserId,
                    updated_by_name = @updatedByName,
                    updated_at = CURRENT_TIMESTAMP
                WHERE id = 1";

            await using (var update = new MySqlCommand(updateSql, connection, transaction))
            {
                update.Parameters.AddWithValue("@isEnabled", isEnabled);
                update.Parameters.AddWithValue("@percentage", normalizedPercentage);
                update.Parameters.AddWithValue("@classification", ToDatabaseValue(classification));
                update.Parameters.AddWithValue("@updatedByUserId", currentUser.Id > 0 ? currentUser.Id : DBNull.Value);
                update.Parameters.AddWithValue("@updatedByName", actorName);

                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    throw new InvalidOperationException("Table service-charge settings could not be updated.");
                }
            }

            const string auditSql = @"
                INSERT INTO table_service_charge_setting_events
                    (previous_is_enabled, new_is_enabled,
                     previous_percentage, new_percentage,
                     previous_classification, new_classification,
                     changed_by_user_id, changed_by_name, changed_at)
                VALUES
                    (@previousIsEnabled, @newIsEnabled,
                     @previousPercentage, @newPercentage,
                     @previousClassification, @newClassification,
                     @changedByUserId, @changedByName, CURRENT_TIMESTAMP)";

            await using (var audit = new MySqlCommand(auditSql, connection, transaction))
            {
                audit.Parameters.AddWithValue("@previousIsEnabled", previous.IsEnabled);
                audit.Parameters.AddWithValue("@newIsEnabled", isEnabled);
                audit.Parameters.AddWithValue("@previousPercentage", previous.Percentage);
                audit.Parameters.AddWithValue("@newPercentage", normalizedPercentage);
                audit.Parameters.AddWithValue("@previousClassification", ToDatabaseValue(previous.Classification));
                audit.Parameters.AddWithValue("@newClassification", ToDatabaseValue(classification));
                audit.Parameters.AddWithValue("@changedByUserId", currentUser.Id > 0 ? currentUser.Id : DBNull.Value);
                audit.Parameters.AddWithValue("@changedByName", actorName);
                await audit.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            return new TableServiceChargeSaveResult(new TableServiceChargeSettings
            {
                IsEnabled = isEnabled,
                Percentage = normalizedPercentage,
                Classification = classification,
                UpdatedByUserId = currentUser.Id > 0 ? currentUser.Id : null,
                UpdatedByName = actorName,
                UpdatedAt = DateTime.Now
            }, true);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<TableServiceChargeSettings> GetForUpdateAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT is_enabled, percentage, classification,
                   updated_by_user_id, updated_by_name, updated_at
            FROM table_service_charge_settings
            WHERE id = 1
            FOR UPDATE";

        await using var command = new MySqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Table service-charge settings are missing. Run database migration 028.");
        }

        return ReadSettings(reader);
    }

    private static TableServiceChargeSettings ReadSettings(MySqlDataReader reader)
    {
        var classification = reader.GetString("classification");
        return new TableServiceChargeSettings
        {
            IsEnabled = reader.GetBoolean("is_enabled"),
            Percentage = reader.GetDecimal("percentage"),
            Classification = classification.Equals("compulsory", StringComparison.OrdinalIgnoreCase)
                ? ServiceChargeClassification.Compulsory
                : ServiceChargeClassification.Optional,
            UpdatedByUserId = reader.IsDBNull(reader.GetOrdinal("updated_by_user_id"))
                ? null
                : reader.GetInt32("updated_by_user_id"),
            UpdatedByName = reader.IsDBNull(reader.GetOrdinal("updated_by_name"))
                ? string.Empty
                : reader.GetString("updated_by_name"),
            UpdatedAt = reader.GetDateTime("updated_at")
        };
    }

    private static string ToDatabaseValue(ServiceChargeClassification classification) =>
        classification == ServiceChargeClassification.Compulsory ? "compulsory" : "optional";
}

public readonly record struct TableServiceChargeSaveResult(
    TableServiceChargeSettings Settings,
    bool Changed);
