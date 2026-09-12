using MySqlConnector;
using OrderWeb.Contracts.Access;
using POS_in_NET.Models;
using System.Diagnostics;
using System.Security.Cryptography;

namespace POS_in_NET.Services;

public class BusinessSettingsService
{
    private const string LogoCacheFolderName = "business-logos";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
    private readonly string _connectionString;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private BusinessInfo? _cachedBusinessInfo;
    private DateTime _cacheUpdatedAt = DateTime.MinValue;

    public BusinessSettingsService()
    {
        _connectionString = TerminalConfigurationService.GetPosConnectionString();
        // Startup migrations own production schema; constructors must stay I/O free.
    }

    private void EnsureBusinessInfoTableExists()
    {
        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            connection.Open();

            var createTableQuery = @"
                CREATE TABLE IF NOT EXISTS business_info (
                    id INT PRIMARY KEY AUTO_INCREMENT,
                    restaurant_name VARCHAR(255) NOT NULL DEFAULT 'Restaurant POS',
                    address TEXT DEFAULT '',
                    city VARCHAR(100) DEFAULT '',
                    county VARCHAR(100) DEFAULT '',
                    country VARCHAR(100) DEFAULT '',
                    postcode VARCHAR(20) DEFAULT '',
                    phone_number VARCHAR(50) DEFAULT '',
                    email VARCHAR(255) DEFAULT '',
                    website VARCHAR(255) DEFAULT '',
                    vat_number VARCHAR(100) DEFAULT '',
                    tax_code VARCHAR(100) DEFAULT '',
                    description TEXT DEFAULT '',
                    logo_path VARCHAR(500) DEFAULT '',
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    updated_by VARCHAR(100) DEFAULT ''
                );
            ";

            using var command = new MySqlCommand(createTableQuery, connection);
            command.ExecuteNonQuery();

            // Insert default business info if none exists
            var checkQuery = "SELECT COUNT(*) FROM business_info";
            using var checkCommand = new MySqlCommand(checkQuery, connection);
            var count = Convert.ToInt32(checkCommand.ExecuteScalar());

            if (count == 0)
            {
                var insertQuery = @"
                    INSERT INTO business_info (restaurant_name, address, phone_number, email, description)
                    VALUES ('Restaurant POS', '', '', '', 'Premium Dining Experience')
                ";
                using var insertCommand = new MySqlCommand(insertQuery, connection);
                insertCommand.ExecuteNonQuery();
            }

            Debug.WriteLine("Business info table created/verified successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating business info table: {ex.Message}");
        }
    }

    private void EnsureBusinessInfoColumnsExist()
    {
        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            connection.Open();

            var alterStatements = new[]
            {
                "ALTER TABLE business_info ADD COLUMN IF NOT EXISTS city VARCHAR(100) DEFAULT ''",
                "ALTER TABLE business_info ADD COLUMN IF NOT EXISTS county VARCHAR(100) DEFAULT ''",
                "ALTER TABLE business_info ADD COLUMN IF NOT EXISTS country VARCHAR(100) DEFAULT ''",
                "ALTER TABLE business_info ADD COLUMN IF NOT EXISTS postcode VARCHAR(20) DEFAULT ''",
                "ALTER TABLE business_info ADD COLUMN IF NOT EXISTS website VARCHAR(255) DEFAULT ''",
                "ALTER TABLE business_info ADD COLUMN IF NOT EXISTS vat_number VARCHAR(100) DEFAULT ''",
                "ALTER TABLE business_info ADD COLUMN IF NOT EXISTS logo_path VARCHAR(500) DEFAULT ''",
                "ALTER TABLE business_info ADD COLUMN IF NOT EXISTS logo_file_name VARCHAR(255) DEFAULT NULL",
                "ALTER TABLE business_info ADD COLUMN IF NOT EXISTS logo_mime_type VARCHAR(100) DEFAULT NULL",
                "ALTER TABLE business_info ADD COLUMN IF NOT EXISTS logo_content_hash CHAR(64) DEFAULT NULL",
                "ALTER TABLE business_info ADD COLUMN IF NOT EXISTS logo_data MEDIUMBLOB NULL",
                "ALTER TABLE business_info ADD COLUMN IF NOT EXISTS label_printer_ip VARCHAR(15) DEFAULT NULL COMMENT 'Brother QL-820NWB IP address'",
                "ALTER TABLE business_info ADD COLUMN IF NOT EXISTS label_printer_port INT(11) DEFAULT 9100 COMMENT 'Label printer network port'",
                "ALTER TABLE business_info ADD COLUMN IF NOT EXISTS label_printer_enabled TINYINT(1) DEFAULT 0 COMMENT 'Enable automatic label printing'",
                "ALTER TABLE business_info ADD COLUMN IF NOT EXISTS till_logout_minutes INT NOT NULL DEFAULT 3"
            };

            foreach (var statement in alterStatements)
            {
                using var command = new MySqlCommand(statement, connection);
                command.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error ensuring business info columns: {ex.Message}");
        }
    }

    public async Task<BusinessInfo?> GetBusinessInfoAsync(bool forceRefresh = false)
    {
        if (!forceRefresh
            && _cachedBusinessInfo != null
            && DateTime.UtcNow - _cacheUpdatedAt < CacheLifetime)
        {
            return Clone(_cachedBusinessInfo);
        }

        await _cacheLock.WaitAsync();
        try
        {
            if (!forceRefresh
                && _cachedBusinessInfo != null
                && DateTime.UtcNow - _cacheUpdatedAt < CacheLifetime)
            {
                return Clone(_cachedBusinessInfo);
            }

            EnsureBusinessInfoTableExists();
            EnsureBusinessInfoColumnsExist();
            var loaded = await LoadBusinessInfoAsync();
            _cachedBusinessInfo = loaded == null ? null : Clone(loaded);
            _cacheUpdatedAt = DateTime.UtcNow;
            return loaded == null ? null : Clone(loaded);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public void InvalidateCache()
    {
        _cachedBusinessInfo = null;
        _cacheUpdatedAt = DateTime.MinValue;
    }

    private async Task<BusinessInfo?> LoadBusinessInfoAsync()
    {
        try
        {
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            var query = @"
                  SELECT id, restaurant_name, address, city, county, country, postcode,
                      phone_number, email, website, vat_number, tax_code, description, logo_path,
                       logo_file_name, logo_mime_type, logo_content_hash, logo_data,
                       label_printer_ip, label_printer_port, label_printer_enabled,
                       till_logout_minutes,
                       updated_at, updated_by
                FROM business_info
                ORDER BY
                    CASE WHEN TRIM(COALESCE(restaurant_name, '')) <> '' THEN 0 ELSE 1 END,
                    updated_at DESC,
                    id DESC
                LIMIT 1
            ";

            using var command = new MySqlCommand(query, connection);
            using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                var logoPath = reader["logo_path"]?.ToString();
                var logoFileName = reader["logo_file_name"]?.ToString();
                if (!reader.IsDBNull(reader.GetOrdinal("logo_data")) && !string.IsNullOrWhiteSpace(logoFileName))
                {
                    logoPath = await WriteLogoCacheAsync(logoFileName, (byte[])reader["logo_data"]);
                }

                return new BusinessInfo
                {
                    Id = Convert.ToInt32(reader["id"]),
                    RestaurantName = reader["restaurant_name"]?.ToString() ?? "",
                    Address = reader["address"]?.ToString() ?? "",
                    City = reader["city"]?.ToString() ?? "",
                    County = reader["county"]?.ToString() ?? "",
                    Country = reader["country"]?.ToString() ?? "",
                    Postcode = reader["postcode"]?.ToString() ?? "",
                    PhoneNumber = reader["phone_number"]?.ToString() ?? "",
                    Email = reader["email"]?.ToString() ?? "",
                    Website = reader["website"]?.ToString() ?? "",
                    VATNumber = reader["vat_number"]?.ToString() ?? "",
                    TaxCode = reader["tax_code"]?.ToString() ?? "",
                    Description = reader["description"]?.ToString() ?? "",
                    LogoPath = logoPath,
                    LabelPrinterIp = reader["label_printer_ip"]?.ToString(),
                    LabelPrinterPort = reader.IsDBNull(reader.GetOrdinal("label_printer_port")) ? 9100 : Convert.ToInt32(reader["label_printer_port"]),
                    LabelPrinterEnabled = reader.IsDBNull(reader.GetOrdinal("label_printer_enabled")) ? false : Convert.ToBoolean(reader["label_printer_enabled"]),
                    TillLogoutMinutes = TillLogoutMinutes.Normalize(
                        reader.IsDBNull(reader.GetOrdinal("till_logout_minutes")) ? null : Convert.ToInt32(reader["till_logout_minutes"])),
                    UpdatedAt = Convert.ToDateTime(reader["updated_at"]),
                    UpdatedBy = reader["updated_by"]?.ToString() ?? ""
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting business info: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> UpdateBusinessInfoAsync(BusinessInfo businessInfo, string updatedBy)
    {
        try
        {
            EnsureBusinessInfoColumnsExist();
            businessInfo.TillLogoutMinutes = TillLogoutMinutes.Normalize(businessInfo.TillLogoutMinutes);
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            var query = @"
                UPDATE business_info 
                SET restaurant_name = @restaurantName,
                    address = @address,
                    city = @city,
                    county = @county,
                    country = @country,
                    postcode = @postcode,
                    phone_number = @phoneNumber,
                    email = @email,
                    website = @website,
                    vat_number = @vatNumber,
                    tax_code = @taxCode,
                    description = @description,
                    logo_path = @logoPath,
                    label_printer_ip = @labelPrinterIp,
                    label_printer_port = @labelPrinterPort,
                    label_printer_enabled = @labelPrinterEnabled,
                    till_logout_minutes = @tillLogoutMinutes,
                    updated_by = @updatedBy,
                    updated_at = CURRENT_TIMESTAMP
                WHERE id = @id
            ";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@restaurantName", businessInfo.RestaurantName);
            command.Parameters.AddWithValue("@address", businessInfo.Address);
            command.Parameters.AddWithValue("@city", businessInfo.City);
            command.Parameters.AddWithValue("@county", businessInfo.County);
            command.Parameters.AddWithValue("@country", businessInfo.Country);
            command.Parameters.AddWithValue("@postcode", businessInfo.Postcode);
            command.Parameters.AddWithValue("@phoneNumber", businessInfo.PhoneNumber);
            command.Parameters.AddWithValue("@email", businessInfo.Email);
            command.Parameters.AddWithValue("@website", businessInfo.Website);
            command.Parameters.AddWithValue("@vatNumber", businessInfo.VATNumber);
            command.Parameters.AddWithValue("@taxCode", businessInfo.TaxCode);
            command.Parameters.AddWithValue("@description", businessInfo.Description);
            command.Parameters.AddWithValue("@logoPath", (object?)businessInfo.LogoPath ?? DBNull.Value);
            command.Parameters.AddWithValue("@labelPrinterIp", (object?)businessInfo.LabelPrinterIp ?? DBNull.Value);
            command.Parameters.AddWithValue("@labelPrinterPort", businessInfo.LabelPrinterPort);
            command.Parameters.AddWithValue("@labelPrinterEnabled", businessInfo.LabelPrinterEnabled);
            command.Parameters.AddWithValue("@tillLogoutMinutes", businessInfo.TillLogoutMinutes);
            command.Parameters.AddWithValue("@updatedBy", updatedBy);
            command.Parameters.AddWithValue("@id", businessInfo.Id);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            if (rowsAffected > 0)
            {
                InvalidateCache();
            }

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating business info: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> CreateBusinessInfoAsync(BusinessInfo businessInfo, string createdBy)
    {
        try
        {
            EnsureBusinessInfoColumnsExist();
            businessInfo.TillLogoutMinutes = TillLogoutMinutes.Normalize(businessInfo.TillLogoutMinutes);
            using var connection = new MySqlConnection(POS_in_NET.Services.TerminalConfigurationService.GetPosConnectionString());
            await connection.OpenAsync();

            var query = @"
                INSERT INTO business_info (
                    restaurant_name, address, city, county, country, postcode,
                    phone_number, email, website, vat_number, tax_code, description,
                    logo_path, label_printer_ip, label_printer_port, label_printer_enabled, till_logout_minutes, updated_by
                ) VALUES (
                    @restaurantName, @address, @city, @county, @country, @postcode,
                    @phoneNumber, @email, @website, @vatNumber, @taxCode, @description,
                    @logoPath, @labelPrinterIp, @labelPrinterPort, @labelPrinterEnabled, @tillLogoutMinutes, @updatedBy
                )
            ";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@restaurantName", businessInfo.RestaurantName);
            command.Parameters.AddWithValue("@address", businessInfo.Address);
            command.Parameters.AddWithValue("@city", businessInfo.City);
            command.Parameters.AddWithValue("@county", businessInfo.County);
            command.Parameters.AddWithValue("@country", businessInfo.Country);
            command.Parameters.AddWithValue("@postcode", businessInfo.Postcode);
            command.Parameters.AddWithValue("@phoneNumber", businessInfo.PhoneNumber);
            command.Parameters.AddWithValue("@email", businessInfo.Email);
            command.Parameters.AddWithValue("@website", businessInfo.Website);
            command.Parameters.AddWithValue("@vatNumber", businessInfo.VATNumber);
            command.Parameters.AddWithValue("@taxCode", businessInfo.TaxCode);
            command.Parameters.AddWithValue("@description", businessInfo.Description);
            command.Parameters.AddWithValue("@logoPath", (object?)businessInfo.LogoPath ?? DBNull.Value);
            command.Parameters.AddWithValue("@labelPrinterIp", (object?)businessInfo.LabelPrinterIp ?? DBNull.Value);
            command.Parameters.AddWithValue("@labelPrinterPort", businessInfo.LabelPrinterPort);
            command.Parameters.AddWithValue("@labelPrinterEnabled", businessInfo.LabelPrinterEnabled);
            command.Parameters.AddWithValue("@tillLogoutMinutes", businessInfo.TillLogoutMinutes);
            command.Parameters.AddWithValue("@updatedBy", createdBy);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            if (rowsAffected > 0)
            {
                InvalidateCache();
            }
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating business info: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> SaveBusinessLogoAsync(int businessInfoId, string sourceFileName, string? mimeType, byte[] imageBytes)
    {
        if (businessInfoId <= 0 || imageBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var contentHash = ComputeSha256(imageBytes);
            var fileName = BuildCachedLogoFileName(businessInfoId, contentHash, sourceFileName);
            var localPath = await WriteLogoCacheAsync(fileName, imageBytes);

            const string query = @"
                UPDATE business_info
                SET logo_path = @logoPath,
                    logo_file_name = @fileName,
                    logo_mime_type = @mimeType,
                    logo_content_hash = @contentHash,
                    logo_data = @logoData,
                    updated_at = CURRENT_TIMESTAMP
                WHERE id = @id";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@id", businessInfoId);
            command.Parameters.AddWithValue("@logoPath", localPath);
            command.Parameters.AddWithValue("@fileName", fileName);
            command.Parameters.AddWithValue("@mimeType", string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType);
            command.Parameters.AddWithValue("@contentHash", contentHash);
            command.Parameters.Add("@logoData", MySqlDbType.MediumBlob).Value = imageBytes;

            var rowsAffected = await command.ExecuteNonQueryAsync();
            if (rowsAffected > 0)
            {
                InvalidateCache();
            }
            return rowsAffected > 0 ? localPath : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving business logo: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> RemoveBusinessLogoAsync(int businessInfoId)
    {
        if (businessInfoId <= 0)
        {
            return false;
        }

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            const string query = @"
                UPDATE business_info
                SET logo_path = NULL,
                    logo_file_name = NULL,
                    logo_mime_type = NULL,
                    logo_content_hash = NULL,
                    logo_data = NULL,
                    updated_at = CURRENT_TIMESTAMP
                WHERE id = @id";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@id", businessInfoId);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            if (rowsAffected > 0)
            {
                InvalidateCache();
            }
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error removing business logo: {ex.Message}");
            return false;
        }
    }

    private static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildCachedLogoFileName(int businessInfoId, string contentHash, string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".img";
        }

        return $"business_logo_{businessInfoId}_{contentHash}{extension.ToLowerInvariant()}";
    }

    private static string GetLogoCacheDirectory()
    {
        var directory = Path.Combine(FileSystem.AppDataDirectory, LogoCacheFolderName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string GetLogoCachePath(string fileName)
    {
        return Path.Combine(GetLogoCacheDirectory(), Path.GetFileName(fileName));
    }

    private static async Task<string> WriteLogoCacheAsync(string fileName, byte[] imageBytes)
    {
        var localPath = GetLogoCachePath(fileName);
        if (!File.Exists(localPath))
        {
            await File.WriteAllBytesAsync(localPath, imageBytes);
        }

        return localPath;
    }

    private static BusinessInfo Clone(BusinessInfo source) => new()
    {
        Id = source.Id,
        RestaurantName = source.RestaurantName,
        Address = source.Address,
        City = source.City,
        County = source.County,
        Country = source.Country,
        Postcode = source.Postcode,
        PhoneNumber = source.PhoneNumber,
        Email = source.Email,
        Website = source.Website,
        VATNumber = source.VATNumber,
        TaxCode = source.TaxCode,
        Description = source.Description,
        LogoPath = source.LogoPath,
        UpdatedAt = source.UpdatedAt,
        UpdatedBy = source.UpdatedBy,
        LabelPrinterIp = source.LabelPrinterIp,
        LabelPrinterPort = source.LabelPrinterPort,
        LabelPrinterEnabled = source.LabelPrinterEnabled,
        TillLogoutMinutes = source.TillLogoutMinutes
    };
}
