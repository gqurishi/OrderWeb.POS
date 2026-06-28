namespace POS_in_NET.Models;

/// <summary>
/// Production database naming and credentials policy for OrderWeb POS.
/// Inno Setup / OrderWeb.DatabaseSetup.exe writes these values via installer config JSON.
/// </summary>
public static class PosDatabaseDefaults
{
    public const string ProductionDatabaseName = "orderweb_pos";
    public const string ProductionDatabaseUser = "orderweb_app";

    /// <summary>Legacy name — used only when reading existing installs during migration.</summary>
    public const string LegacyDatabaseName = "Pos-net";

    public const string InstallerConfigFileName = "orderweb-database.json";
    public const string InstallerConfigFolderName = "OrderWebPOS";

    /// <summary>Must match the highest numbered migration in Database/Migrations/.</summary>
    public const int RequiredSchemaVersion = 14;
}
