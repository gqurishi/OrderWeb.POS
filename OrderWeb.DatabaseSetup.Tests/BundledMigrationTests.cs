using OrderWeb.DatabaseSetup.Services;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public class BundledMigrationTests
{
    [Fact]
    public void BundledMigrations_IncludeLatest()
    {
        var migrationsPath = FindRepoMigrationsPath();
        Assert.NotNull(migrationsPath);

        var engine = new MigrationEngine(migrationsPath!, "1.0.0");
        Assert.Equal(23, engine.GetBundledSchemaVersion());
        Assert.Contains(engine.DiscoverMigrationFiles(), f => f.Id == "023_orderweb_order_settlements");
    }

    private static string? FindRepoMigrationsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Database", "Migrations");
            if (File.Exists(Path.Combine(candidate, "023_orderweb_order_settlements.sql")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
