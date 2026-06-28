using OrderWeb.DatabaseSetup.Services;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public class MigrationEngineTests
{
    [Fact]
    public void DiscoverMigrationFiles_UsesFullFileStemAsMigrationId()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "orderweb-migrations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "001_initial_schema.sql"), "-- test");
            File.WriteAllText(Path.Combine(tempDir, "010_postcode_orderweb_api.sql"), "-- test");
            File.WriteAllText(Path.Combine(tempDir, "VERIFY_REQUIRED_SCHEMA.sql"), "-- ignored");

            var engine = new MigrationEngine(tempDir, "1.0.0");
            var files = engine.DiscoverMigrationFiles();

            Assert.Equal(2, files.Count);
            Assert.Equal("001_initial_schema", files[0].Id);
            Assert.Equal("010_postcode_orderweb_api", files[1].Id);
            Assert.Equal(10, engine.GetBundledSchemaVersion());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}

public class SqlExecutorTests
{
    [Fact]
    public void SplitSqlStatements_SplitsOnSemicolonOutsideQuotes()
    {
        const string sql = """
            CREATE TABLE demo (name VARCHAR(10));
            INSERT INTO demo VALUES ('a;b');
            -- trailing comment
            SELECT 1;
            """;

        var statements = SqlExecutor.SplitSqlStatements(sql).ToList();
        Assert.Equal(3, statements.Count);
        Assert.Contains("CREATE TABLE demo", statements[0]);
        Assert.Contains("INSERT INTO demo", statements[1]);
        Assert.Contains("SELECT 1", statements[2]);
    }
}
