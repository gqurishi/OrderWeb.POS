namespace OrderWeb.DatabaseSetup.Models;

public sealed class DatabaseConfig
{
    public string DatabaseHost { get; set; } = "localhost";
    public int DatabasePort { get; set; } = 3306;
    public string DatabaseName { get; set; } = PosDefaults.ProductionDatabaseName;
    public string DatabaseUser { get; set; } = PosDefaults.ProductionDatabaseUser;
    public string DatabasePassword { get; set; } = string.Empty;
    public bool InstalledBySetup { get; set; }
}
