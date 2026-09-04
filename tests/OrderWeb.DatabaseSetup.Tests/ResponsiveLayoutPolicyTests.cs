using POS_in_NET.Helpers;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public sealed class ResponsiveLayoutPolicyTests
{
    [Theory]
    [InlineData(1280, 720, ResponsiveSizeClass.Compact)]
    [InlineData(1366, 768, ResponsiveSizeClass.Standard)]
    [InlineData(1920, 1080, ResponsiveSizeClass.Large)]
    public void SupportedPosSizes_MapToStableProfiles(
        double width,
        double height,
        ResponsiveSizeClass expected)
    {
        var profile = ResponsiveLayoutProfile.ForSize(width, height);
        Assert.Equal(expected, profile.SizeClass);
        Assert.InRange(profile.PagePadding, 12, 20);
        Assert.InRange(profile.SafeBottom, 12, 16);
    }

    [Fact]
    public void EveryContentPage_ReceivesTheGlobalResponsiveBehavior()
    {
        var root = FindRepositoryRoot();
        var appXaml = File.ReadAllText(Path.Combine(root, "src", "OrderWeb.Mother", "App.xaml"));

        Assert.Contains("TargetType=\"ContentPage\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("ResponsiveLayout.Enabled", appXaml, StringComparison.Ordinal);
        Assert.Contains("ResponsiveSafeBottom", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainResponsiveLayouts_DoNotRestoreLegacy520Or560PixelLists()
    {
        var root = FindRepositoryRoot();
        var sourceFolders = new[] { "src/OrderWeb.Mother/Features", "src/OrderWeb.Mother/Shared" };
        var violations = sourceFolders
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar)), "*.xaml", SearchOption.AllDirectories))
            .Where(path =>
            {
                var xaml = File.ReadAllText(path);
                return xaml.Contains("HeightRequest=\"520\"", StringComparison.Ordinal)
                    || xaml.Contains("HeightRequest=\"560\"", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("src/OrderWeb.Mother/Features/Orders/Views/MoreOptionsDialog.xaml")]
    [InlineData("src/OrderWeb.Mother/Features/Payments/Views/CashPaymentDialog.xaml")]
    [InlineData("src/OrderWeb.Mother/Features/Payments/Views/CardPaymentDialog.xaml")]
    public void HighTrafficDialogs_KeepActionsOutsideTheScrollableBody(string relativePath)
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains("*,Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"1\"", xaml, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "OrderWeb.Mother", "OrderWeb.Mother.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the OrderWeb.POS repository root.");
    }
}
