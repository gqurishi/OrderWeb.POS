using OrderWeb.DatabaseSetup.Services;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public class ProductionCredentialPolicyTests
{
    [Fact]
    public void ValidateAppCredentials_AllowsRootUser()
    {
        var result = ProductionCredentialPolicy.ValidateAppCredentials("root", "root");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateAppCredentials_AllowsSimpleManualPasswords()
    {
        var result = ProductionCredentialPolicy.ValidateAppCredentials("orderweb_app", "root");
        Assert.True(result.IsValid);

        result = ProductionCredentialPolicy.ValidateAppCredentials("orderweb_app", "admin123");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateAppCredentials_AcceptsStrongGeneratedPassword()
    {
        var password = MotherInstaller.GeneratePassword();
        var result = ProductionCredentialPolicy.ValidateAppCredentials("orderweb_app", password);

        Assert.True(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(password));
    }

    [Fact]
    public void ValidateAppCredentials_AcceptsNormalManualPassword()
    {
        var result = ProductionCredentialPolicy.ValidateAppCredentials("orderweb_app", "OrderWeb2026");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void GeneratePassword_ReturnsNonEmptyPassword()
    {
        for (var i = 0; i < 20; i++)
        {
            var password = MotherInstaller.GeneratePassword();
            Assert.False(string.IsNullOrWhiteSpace(password));
        }
    }

    [Fact]
    public void ResolveProductionConfigPath_UsesProgramDataOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = ConfigStore.ResolveProductionConfigPath();
        Assert.Contains("OrderWebPOS", path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("orderweb-database.json", path, StringComparison.OrdinalIgnoreCase);
    }
}
