using OrderWeb.DatabaseSetup.Services;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public class ProductionCredentialPolicyTests
{
    [Fact]
    public void ValidateAppCredentials_RejectsRootUser()
    {
        var result = ProductionCredentialPolicy.ValidateAppCredentials("root", "root");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateAppCredentials_RejectsWeakPasswords()
    {
        var result = ProductionCredentialPolicy.ValidateAppCredentials("orderweb_app", "root");
        Assert.False(result.IsValid);

        result = ProductionCredentialPolicy.ValidateAppCredentials("orderweb_app", "admin123");
        Assert.False(result.IsValid);
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
    public void ValidateAppCredentials_AcceptsStrongManualPassword()
    {
        var result = ProductionCredentialPolicy.ValidateAppCredentials("orderweb_app", "OrderWeb-Production-2026!A");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void BuildConnectionString_PrefersTls()
    {
        var config = new OrderWeb.DatabaseSetup.Models.DatabaseConfig
        {
            DatabasePassword = MotherInstaller.GeneratePassword()
        };

        var builder = new MySqlConnector.MySqlConnectionStringBuilder(ConfigStore.BuildConnectionString(config));

        Assert.Equal(MySqlConnector.MySqlSslMode.Preferred, builder.SslMode);
        Assert.Equal("orderweb_app", builder.UserID);
    }

    [Fact]
    public void ValidateChildHosts_AcceptsExactPrivateAddresses()
    {
        var result = MotherInstaller.ValidateChildHosts(["192.168.10.21", "10.20.0.4"]);

        Assert.Equal(2, result.Count);
    }

    [Theory]
    [InlineData("192.168.%")]
    [InlineData("0.0.0.0")]
    [InlineData("8.8.8.8")]
    public void ValidateChildHosts_RejectsWildcardOrNonPrivateAddress(string value)
    {
        Assert.Throws<ArgumentException>(() => MotherInstaller.ValidateChildHosts([value]));
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
