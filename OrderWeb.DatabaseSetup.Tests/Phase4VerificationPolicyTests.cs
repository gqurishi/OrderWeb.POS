using System.Text.Json;
using POS_in_NET.Services;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public sealed class Phase4VerificationPolicyTests
{
    [Fact]
    public void PerformanceBudgets_MatchPhase4Targets()
    {
        Assert.Equal(50, PosPerformanceTargets.TapFeedbackMilliseconds);
        Assert.Equal(150, PosPerformanceTargets.NavigationFrameMilliseconds);
        Assert.Equal(600, PosPerformanceTargets.NormalPageDataMilliseconds);
    }

    [Fact]
    public void VerificationManifest_CoversEveryScaleAndWorkflow()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "Verification", "phase4-workflows.json")));
        var manifest = document.RootElement;

        Assert.Equal(
            new[] { 100, 125, 150, 175 },
            manifest.GetProperty("scalingPercentages").EnumerateArray().Select(value => value.GetInt32()));

        var workflows = manifest.GetProperty("workflows")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToList();
        var required = new[]
        {
            "Dashboard to Collection", "Dashboard to Delivery", "Dashboard to Table",
            "Add items and notes", "More Options and Discount", "Fire Course",
            "Payment and split payment", "Customer selection and address", "Previous Orders",
            "Order History", "Reports", "Settings and menu management"
        };

        Assert.All(required, workflow => Assert.Contains(workflow, workflows));
    }

    [Fact]
    public void NavigationCoordinator_MeasuresFeedbackAndBlocksDoubleTaps()
    {
        var source = ReadSource("Shared/Services/NavigationCoordinator.cs");

        Assert.Contains("WaitAsync(0)", source, StringComparison.Ordinal);
        Assert.Contains("RecordDuplicateNavigationAttempt", source, StringComparison.Ordinal);
        Assert.Contains("RecordTapFeedback", source, StringComparison.Ordinal);
        Assert.Contains("MarkNavigationFrameVisible", source, StringComparison.Ordinal);
        var yieldPosition = source.IndexOf("await Task.Yield()", StringComparison.Ordinal);
        var feedbackPosition = source.IndexOf("RecordTapFeedback", StringComparison.Ordinal);
        var commitPosition = source.IndexOf("ResolveVisiblePage()", StringComparison.Ordinal);
        Assert.True(yieldPosition < feedbackPosition);
        Assert.True(feedbackPosition < commitPosition);
    }

    [Theory]
    [InlineData("Features/Orders/Pages/OrderPlacementPageSimple.xaml.cs")]
    [InlineData("Features/Orders/Pages/OrderHistoryPage.xaml.cs")]
    [InlineData("Features/Reports/Pages/ReportPage.xaml.cs")]
    public void ReusableOrderScreens_HaveLiveRefreshProtection(string relativePath)
    {
        var source = ReadSource(relativePath);
        Assert.Contains("AppDataRefreshService.DataChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("AppDataRefreshService.DataChanged -=", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Dashboard")]
    [InlineData("Order Entry")]
    [InlineData("Order History")]
    [InlineData("Reports")]
    [InlineData("Settings")]
    [InlineData("Menu Management")]
    public void ImportantPages_RecordWhenDataIsVisible(string operation)
    {
        var root = FindRepositoryRoot();
        var source = Directory.EnumerateFiles(Path.Combine(root, "Features"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .FirstOrDefault(text => text.Contains($"BeginDataLoad(\"{operation}\")", StringComparison.Ordinal));

        Assert.NotNull(source);
        Assert.Contains("MarkDataVisible", source!, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "POS-in-NET.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the POS repository root.");
    }
}
