namespace OrderWeb.DatabaseSetup.Models;

public static class PosDefaults
{
    public const string ProductionDatabaseName = "orderweb_pos";
    public const string ProductionDatabaseUser = "orderweb_app";
    public const string InstallerConfigFileName = "orderweb-database.json";
    public const string InstallerConfigFolderName = "OrderWebPOS";
    public const string BackupExtension = ".orderwebbackup";
    public const string BackupFormat = "orderwebbackup-v1";
    public const string ProductName = "OrderWebPOS";
    public const int RequiredSchemaVersion = 25;
}
