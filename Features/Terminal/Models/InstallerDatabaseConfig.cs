namespace POS_in_NET.Models;

/// <summary>
/// Written by Inno Setup / OrderWeb.DatabaseSetup.exe before first app launch.
/// </summary>
public sealed class InstallerDatabaseConfig
{
    public string DatabaseHost { get; set; } = "localhost";
    public int DatabasePort { get; set; } = 3306;
    public string DatabaseName { get; set; } = PosDatabaseDefaults.ProductionDatabaseName;
    public string DatabaseUser { get; set; } = PosDatabaseDefaults.ProductionDatabaseUser;
    public string DatabasePassword { get; set; } = string.Empty;
    public bool InstalledBySetup { get; set; }
}
