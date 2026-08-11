namespace OrderWeb.DatabaseSetup.Models;

/// <summary>
/// Metadata written after install-mother (no secrets).
/// </summary>
public sealed class InstallManifest
{
    public string Product { get; set; } = PosDefaults.ProductName;
    public string InstallType { get; set; } = "mother";
    public string AppVersion { get; set; } = "1.0.1";
    public DateTime InstalledAtUtc { get; set; } = DateTime.UtcNow;
    public string DatabaseName { get; set; } = PosDefaults.ProductionDatabaseName;
    public string DatabaseUser { get; set; } = PosDefaults.ProductionDatabaseUser;
    public string DatabaseHost { get; set; } = "localhost";
    public int DatabasePort { get; set; } = 3306;
    public string ConfigPath { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
}
