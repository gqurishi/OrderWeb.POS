namespace OrderWeb.DatabaseSetup.Models;

public sealed class BackupMetadata
{
    public string Product { get; set; } = PosDefaults.ProductName;
    public string BackupFormat { get; set; } = PosDefaults.BackupFormat;
    public string BackupType { get; set; } = "manual";
    public string AppVersion { get; set; } = "1.0.1";
    public string DatabaseName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string SqlSha256 { get; set; } = string.Empty;
    public long SqlSizeBytes { get; set; }
    public int SchemaVersion { get; set; }
}
