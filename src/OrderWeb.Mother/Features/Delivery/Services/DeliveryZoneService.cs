using System.Text.RegularExpressions;
using MySqlConnector;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

public sealed partial class DeliveryZoneService
{
    private readonly DatabaseService _databaseService;
    private bool _tablesEnsured;

    public DeliveryZoneService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public static string NormalizePostcode(string? postcode)
    {
        if (string.IsNullOrWhiteSpace(postcode))
        {
            return string.Empty;
        }

        var compact = NonAlphaNumericRegex().Replace(postcode.Trim().ToUpperInvariant(), string.Empty);
        if (compact.Length <= 3)
        {
            return compact;
        }

        return $"{compact[..^3]} {compact[^3..]}";
    }

    public async Task EnsureTablesAsync()
    {
        if (RuntimeSchemaPolicy.IsMigrationManaged) return;
        if (_tablesEnsured)
        {
            return;
        }

        await using var connection = await _databaseService.GetConnectionAsync();

        var createZones = @"
            CREATE TABLE IF NOT EXISTS delivery_zones (
                id INT AUTO_INCREMENT PRIMARY KEY,
                name VARCHAR(120) NOT NULL,
                delivery_fee DECIMAL(10,2) NOT NULL DEFAULT 0.00,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

        var createPostcodes = @"
            CREATE TABLE IF NOT EXISTS delivery_zone_postcodes (
                id INT AUTO_INCREMENT PRIMARY KEY,
                zone_id INT NOT NULL,
                postcode VARCHAR(16) NOT NULL,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE KEY ux_delivery_zone_postcode (postcode),
                INDEX idx_delivery_zone_postcodes_zone (zone_id),
                CONSTRAINT fk_delivery_zone_postcodes_zone
                    FOREIGN KEY (zone_id) REFERENCES delivery_zones(id)
                    ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

        var createUnassigned = @"
            CREATE TABLE IF NOT EXISTS delivery_unassigned_postcodes (
                id INT AUTO_INCREMENT PRIMARY KEY,
                postcode VARCHAR(16) NOT NULL,
                request_count INT NOT NULL DEFAULT 1,
                first_seen_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                last_seen_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE KEY ux_delivery_unassigned_postcode (postcode)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

        await using (var command = new MySqlCommand(createZones, connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = new MySqlCommand(createPostcodes, connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = new MySqlCommand(createUnassigned, connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        _tablesEnsured = true;
    }

    public async Task<List<DeliveryZone>> GetZonesAsync()
    {
        await EnsureTablesAsync();
        var zones = new List<DeliveryZone>();

        await using var connection = await _databaseService.GetConnectionAsync();
        var query = @"
            SELECT z.id, z.name, z.delivery_fee, z.created_at, p.id AS postcode_id, p.postcode, p.created_at AS postcode_created_at
            FROM delivery_zones z
            LEFT JOIN delivery_zone_postcodes p ON p.zone_id = z.id
            ORDER BY z.name ASC, p.postcode ASC";

        await using var command = new MySqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var byId = new Dictionary<int, DeliveryZone>();
        while (await reader.ReadAsync())
        {
            var zoneId = reader.GetInt32("id");
            if (!byId.TryGetValue(zoneId, out var zone))
            {
                zone = new DeliveryZone
                {
                    Id = zoneId,
                    Name = reader.GetString("name"),
                    DeliveryFee = reader.GetDecimal("delivery_fee"),
                    CreatedAt = reader.GetDateTime("created_at")
                };
                byId.Add(zoneId, zone);
                zones.Add(zone);
            }

            if (!reader.IsDBNull(reader.GetOrdinal("postcode_id")))
            {
                zone.Postcodes.Add(new DeliveryZonePostcode
                {
                    Id = reader.GetInt32("postcode_id"),
                    ZoneId = zoneId,
                    Postcode = reader.GetString("postcode"),
                    CreatedAt = reader.GetDateTime("postcode_created_at")
                });
            }
        }

        return zones;
    }

    public async Task<int> CreateZoneAsync(string name, decimal deliveryFee)
    {
        await EnsureTablesAsync();

        await using var connection = await _databaseService.GetConnectionAsync();
        var query = @"
            INSERT INTO delivery_zones (name, delivery_fee)
            VALUES (@name, @deliveryFee);
            SELECT LAST_INSERT_ID();";

        await using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@name", name.Trim());
        command.Parameters.AddWithValue("@deliveryFee", deliveryFee);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task DeleteZoneAsync(int zoneId)
    {
        await EnsureTablesAsync();
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand("DELETE FROM delivery_zones WHERE id = @zoneId", connection);
        command.Parameters.AddWithValue("@zoneId", zoneId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task AddPostcodeAsync(int zoneId, string postcode)
    {
        await EnsureTablesAsync();
        var normalized = NormalizePostcode(postcode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Enter a valid postcode.");
        }

        await using var connection = await _databaseService.GetConnectionAsync();

        var existingZone = await FindZoneNameForPostcodeAsync(connection, normalized);
        if (!string.IsNullOrWhiteSpace(existingZone))
        {
            throw new InvalidOperationException($"{normalized} is already in {existingZone}. Remove it there first.");
        }

        var query = "INSERT INTO delivery_zone_postcodes (zone_id, postcode) VALUES (@zoneId, @postcode)";
        await using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@zoneId", zoneId);
        command.Parameters.AddWithValue("@postcode", normalized);
        await command.ExecuteNonQueryAsync();
    }

    public async Task RemovePostcodeAsync(int postcodeId)
    {
        await EnsureTablesAsync();
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand("DELETE FROM delivery_zone_postcodes WHERE id = @postcodeId", connection);
        command.Parameters.AddWithValue("@postcodeId", postcodeId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<DeliveryZoneMatch?> FindZoneForPostcodeAsync(string postcode)
    {
        await EnsureTablesAsync();
        var normalized = NormalizePostcode(postcode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        await using var connection = await _databaseService.GetConnectionAsync();
        var query = @"
            SELECT z.id, z.name, z.delivery_fee, p.postcode
            FROM delivery_zone_postcodes p
            INNER JOIN delivery_zones z ON z.id = p.zone_id
            WHERE p.postcode = @postcode
            LIMIT 1";

        await using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@postcode", normalized);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new DeliveryZoneMatch
        {
            ZoneId = reader.GetInt32("id"),
            ZoneName = reader.GetString("name"),
            DeliveryFee = reader.GetDecimal("delivery_fee"),
            Postcode = reader.GetString("postcode")
        };
    }

    public async Task SaveUnassignedPostcodeAsync(string postcode)
    {
        await EnsureTablesAsync();
        var normalized = NormalizePostcode(postcode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        await using var connection = await _databaseService.GetConnectionAsync();
        var query = @"
            INSERT INTO delivery_unassigned_postcodes (postcode, request_count, first_seen_at, last_seen_at)
            VALUES (@postcode, 1, NOW(), NOW())
            ON DUPLICATE KEY UPDATE
                request_count = request_count + 1,
                last_seen_at = NOW()";

        await using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@postcode", normalized);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<UnassignedDeliveryPostcode>> GetUnassignedPostcodesAsync()
    {
        await EnsureTablesAsync();
        var rows = new List<UnassignedDeliveryPostcode>();

        await using var connection = await _databaseService.GetConnectionAsync();
        var query = @"
            SELECT id, postcode, request_count, first_seen_at, last_seen_at
            FROM delivery_unassigned_postcodes
            ORDER BY last_seen_at DESC, request_count DESC";

        await using var command = new MySqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new UnassignedDeliveryPostcode
            {
                Id = reader.GetInt32("id"),
                Postcode = reader.GetString("postcode"),
                RequestCount = reader.GetInt32("request_count"),
                FirstSeenAt = reader.GetDateTime("first_seen_at"),
                LastSeenAt = reader.GetDateTime("last_seen_at")
            });
        }

        return rows;
    }

    public async Task DeleteUnassignedPostcodeAsync(int id)
    {
        await EnsureTablesAsync();
        await using var connection = await _databaseService.GetConnectionAsync();
        await using var command = new MySqlCommand("DELETE FROM delivery_unassigned_postcodes WHERE id = @id", connection);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> FindZoneNameForPostcodeAsync(MySqlConnection connection, string postcode)
    {
        var query = @"
            SELECT z.name
            FROM delivery_zone_postcodes p
            INNER JOIN delivery_zones z ON z.id = p.zone_id
            WHERE p.postcode = @postcode
            LIMIT 1";

        await using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@postcode", postcode);

        return (await command.ExecuteScalarAsync())?.ToString();
    }

    [GeneratedRegex("[^A-Z0-9]", RegexOptions.Compiled)]
    private static partial Regex NonAlphaNumericRegex();
}
