using POS_in_NET.Services;

namespace POS_in_NET.MigrationRunner;

/// <summary>
/// Simple console runner to execute database migration
/// Run this to upgrade Pos-net to restaurant_local
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine(" DATABASE MIGRATION TOOL");
        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("This will migrate your database from 'Pos-net' to 'restaurant_local'");
        Console.WriteLine();
        
        var migrationService = new DatabaseMigrationService();
        
        Console.WriteLine(" Testing current database connection...");
        var oldDbService = new DatabaseService();
        var canConnect = await oldDbService.TestConnectionAsync();
        
        if (!canConnect)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(" Cannot connect to database. Check MySQL is running.");
            Console.ResetColor();
            return;
        }
        
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(" Connected to current database");
        Console.ResetColor();
        Console.WriteLine();
        
        Console.WriteLine("  MIGRATION WILL:");
        Console.WriteLine("   • Create new 'restaurant_local' database");
        Console.WriteLine("   • Create 15 comprehensive tables");
        Console.WriteLine("   • Migrate users → staff");
        Console.WriteLine("   • Migrate orders → online_orders");
        Console.WriteLine("   • Migrate order_items → online_order_items");
        Console.WriteLine("   • Preserve settings, cloud_config, business_info");
        Console.WriteLine("   • Start with empty menu, inventory, orders, and reports for production");
        Console.WriteLine();
        
        Console.Write("Ready to proceed? (yes/no): ");
        var confirm = Console.ReadLine()?.ToLower();
        
        if (confirm != "yes")
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(" Migration cancelled");
            Console.ResetColor();
            return;
        }
        
        Console.WriteLine();
        Console.WriteLine(" Starting migration...");
        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine();
        
        var success = await migrationService.MigrateDatabaseAsync();
        
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════");
        
        if (success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(" MIGRATION SUCCESSFUL!");
            Console.ResetColor();
            Console.WriteLine();
            
            Console.WriteLine("Testing new database...");
            var testSuccess = await migrationService.TestNewDatabaseAsync();
            
            if (testSuccess)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" New database verified successfully!");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("Your POS now has:");
                Console.WriteLine("    15 comprehensive tables");
                Console.WriteLine("    Separated local/online orders");
                Console.WriteLine("    Customer management");
                Console.WriteLine("    Menu & inventory system");
                Console.WriteLine("    Staff shifts & payroll");
                Console.WriteLine("    Multi-printer queue");
                Console.WriteLine("    Payment tracking");
                Console.WriteLine("    Sync logging");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(" NEXT STEPS:");
                Console.WriteLine("   1. Restart your POS application");
                Console.WriteLine("   2. The app will now use 'restaurant_local' database");
                Console.WriteLine("   3. Configure cloud sync in settings");
                Console.ResetColor();
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(" MIGRATION FAILED");
            Console.WriteLine("Check the error messages above for details");
            Console.ResetColor();
        }
        
        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}
