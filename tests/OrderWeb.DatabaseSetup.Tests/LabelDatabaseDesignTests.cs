using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public class LabelDatabaseDesignTests
{
    [Fact]
    public void Phase5Migration_StoresProfilesAndDurableSnapshotJobs()
    {
        var root = FindRepoRoot();
        var sql = File.ReadAllText(Path.Combine(root, "database", "Migrations", "039_label_media_profiles_and_jobs.sql"));

        Assert.Contains("CREATE TABLE IF NOT EXISTS label_media_profiles", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS label_print_jobs", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("printable_name_snapshot", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("payload_snapshot JSON", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("generated_tpcl_data", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reprint_of_job_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("toshiba_tpcl", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("driver_data", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("driver_executable", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase7LabelRenderer_PrintsOptionalTextAndParentItem()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "OrderWeb.Mother", "Features", "Printing", "Services", "BrotherLabelPrinter.cs"));
        var itemMethod = Between(source, "private byte[] GenerateItemLabel", "private byte[] GenerateComponentLabel");
        var componentMethod = Between(source, "private byte[] GenerateComponentLabel", "private async Task SendToPrinterAsync");

        Assert.Contains("PrintText(itemName", itemMethod, StringComparison.Ordinal);
        Assert.Contains("PrintText(labelText.Trim()", itemMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime", itemMethod, StringComparison.Ordinal);

        Assert.Contains("PrintText(componentName", componentMethod, StringComparison.Ordinal);
        Assert.Contains("PrintText(parentItemName", componentMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime", componentMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void ToshibaModule_IsIndependentAndUsesDocumentedTpclFraming()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "OrderWeb.Mother", "Features", "Printing", "Services", "ToshibaTpclModule.cs"));

        Assert.Contains("AddCommand(commands, $\"D", source, StringComparison.Ordinal);
        Assert.Contains("AddCommand(commands, \"C\")", source, StringComparison.Ordinal);
        Assert.Contains("PC{id}", source, StringComparison.Ordinal);
        Assert.Contains("RC{id}", source, StringComparison.Ordinal);
        Assert.Contains("XS;I,", source, StringComparison.Ordinal);
        Assert.Contains("AddCommand(bytes, \"WB\")", source, StringComparison.Ordinal);
        Assert.Contains("output.Add(Escape)", source, StringComparison.Ordinal);
        Assert.Contains("output.Add(LineFeed)", source, StringComparison.Ordinal);
        Assert.Contains("output.Add(Null)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ESCCommands", source, StringComparison.Ordinal);
        Assert.Contains("printable ASCII", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase9Transport_SerialisesEndpointsAndSeparatesResultLevels()
    {
        var root = FindRepoRoot();
        var transport = File.ReadAllText(Path.Combine(root, "src", "OrderWeb.Mother", "Features", "Printing", "Services", "ToshibaRawTcpTransport.cs"));
        var result = File.ReadAllText(Path.Combine(root, "src", "OrderWeb.Mother", "Features", "Printing", "Models", "ToshibaNetworkResult.cs"));
        var sql = File.ReadAllText(Path.Combine(root, "database", "Migrations", "041_label_network_results.sql"));

        Assert.Contains("ConcurrentDictionary<string, SemaphoreSlim>", transport, StringComparison.Ordinal);
        Assert.Contains("endpointLock.WaitAsync", transport, StringComparison.Ordinal);
        Assert.Contains("ConnectionTimeout", transport, StringComparison.Ordinal);
        Assert.Contains("DataSendTimeout", transport, StringComparison.Ordinal);
        Assert.Contains("StatusResponseTimeout", transport, StringComparison.Ordinal);
        Assert.Contains("CompleteJobTimeout", transport, StringComparison.Ordinal);
        Assert.Contains("response.Length == 23", transport, StringComparison.Ordinal);
        Assert.Contains("PhysicalLabelConfirmed", result, StringComparison.Ordinal);
        Assert.Contains("PhysicalLabelConfirmed = true", result, StringComparison.Ordinal);
        Assert.Contains("physical_label_confirmed", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("printer_status_response", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string Between(string value, string start, string end)
    {
        var startIndex = value.IndexOf(start, StringComparison.Ordinal);
        var endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return value[startIndex..endIndex];
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "database", "Migrations")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
