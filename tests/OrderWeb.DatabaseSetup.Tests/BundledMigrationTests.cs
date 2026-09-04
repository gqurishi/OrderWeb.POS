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
        Assert.Equal(37, engine.GetBundledSchemaVersion());
        Assert.Equal(37, OrderWeb.DatabaseSetup.Models.PosDefaults.RequiredSchemaVersion);
        Assert.Contains(engine.DiscoverMigrationFiles(), f => f.Id == "030_kitchen_red_printing");
        Assert.Contains(engine.DiscoverMigrationFiles(), f => f.Id == "031_course_fire_status");
        Assert.Contains(engine.DiscoverMigrationFiles(), f => f.Id == "032_local_order_retention");
        Assert.Contains(engine.DiscoverMigrationFiles(), f => f.Id == "033_phase2_query_indexes");
        Assert.Contains(engine.DiscoverMigrationFiles(), f => f.Id == "034_orderweb_payment_details");
        Assert.Contains(engine.DiscoverMigrationFiles(), f => f.Id == "035_orderweb_contract_v2");
        Assert.Contains(engine.DiscoverMigrationFiles(), f => f.Id == "036_rider_operations");
    }

    [Fact]
    public void Phase2Indexes_MatchBoundedOrderHistoryQueries()
    {
        var migrationsPath = FindRepoMigrationsPath();
        Assert.NotNull(migrationsPath);

        var sql = File.ReadAllText(Path.Combine(migrationsPath!, "033_phase2_query_indexes.sql"));
        Assert.Contains("idx_orders_local_history", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source_channel, created_at", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("idx_orders_customer_phone_created", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OrderWebPaymentDetailsMigration_IsAdditiveAndComplete()
    {
        var migrationsPath = FindRepoMigrationsPath();
        Assert.NotNull(migrationsPath);

        var sql = File.ReadAllText(Path.Combine(migrationsPath!, "034_orderweb_payment_details.sql"));
        foreach (var column in new[]
                 {
                     "payment_status", "amount_paid", "payment_provider", "payment_reference",
                     "payment_currency", "voucher_code", "cash_tip_amount", "card_tip_amount"
                 })
        {
            Assert.Contains(column, sql, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("DROP ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OrderWebContractV2Migration_IsAdditiveAndSupportsExternalItemIds()
    {
        var migrationsPath = FindRepoMigrationsPath();
        Assert.NotNull(migrationsPath);

        var sql = File.ReadAllText(Path.Combine(migrationsPath!, "035_orderweb_contract_v2.sql"));
        foreach (var column in new[]
                 {
                     "promo_code", "gift_card_number_masked", "gift_card_remaining_balance",
                     "loyalty_points_earned", "loyalty_points_redeemed", "loyalty_points_discount",
                     "loyalty_balance_after", "cloud_item_external_id"
                 })
        {
            Assert.Contains(column, sql, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("DROP ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindRepoMigrationsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "database", "migrations");
            if (File.Exists(Path.Combine(candidate, "031_course_fire_status.sql")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    [Fact]
    public void KitchenPrintingMigrations_AreAdditive()
    {
        var migrationsPath = FindRepoMigrationsPath();
        Assert.NotNull(migrationsPath);

        var redSql = File.ReadAllText(Path.Combine(migrationsPath!, "030_kitchen_red_printing.sql"));
        var fireSql = File.ReadAllText(Path.Combine(migrationsPath!, "031_course_fire_status.sql"));

        Assert.Contains("supports_two_color", redSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("print_in_red", redSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("course_type", fireSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fired_at", fireSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fired_by", fireSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP ", redSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP ", fireSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", fireSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalOrderRetentionMigration_RequiresAuditedCloudSafeCleanup()
    {
        var migrationsPath = FindRepoMigrationsPath();
        Assert.NotNull(migrationsPath);

        var sql = File.ReadAllText(Path.Combine(migrationsPath!, "032_local_order_retention.sql"));

        Assert.Contains("local_retention_purge", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("idx_orders_local_retention", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM orders", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OrderServiceAvailabilityMigration_IsAdditiveAndDefaultsAllServicesOn()
    {
        var migrationsPath = FindRepoMigrationsPath();
        Assert.NotNull(migrationsPath);

        var sql = File.ReadAllText(Path.Combine(migrationsPath!, "029_order_service_availability.sql"));

        Assert.Contains("order_service_availability_settings", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("order_service_availability_events", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("table_enabled BOOLEAN NOT NULL DEFAULT TRUE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("collection_enabled BOOLEAN NOT NULL DEFAULT TRUE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delivery_enabled BOOLEAN NOT NULL DEFAULT TRUE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP COLUMN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TableServiceChargeMigration_IsAdditiveAndKeepsDeliveryFeeSeparate()
    {
        var migrationsPath = FindRepoMigrationsPath();
        Assert.NotNull(migrationsPath);

        var sql = File.ReadAllText(Path.Combine(migrationsPath!, "028_table_service_charge_schema.sql"));

        Assert.Contains("table_service_charge_settings", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("service_charge_percentage", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("service_charge_basis", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("service_charge_amount", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("service_charge_status", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("order_service_charge_events", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP COLUMN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE orders", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MODIFY COLUMN delivery_fee", sql, StringComparison.OrdinalIgnoreCase);
    }
}
