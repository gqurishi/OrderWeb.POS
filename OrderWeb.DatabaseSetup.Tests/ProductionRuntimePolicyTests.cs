using POS_in_NET.Services;
using System.Reflection;
using System.Text;
using OrderWeb.DatabaseSetup.Services;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public sealed class ProductionRuntimePolicyTests
{
    [Theory]
    [InlineData("http://orderweb.net/api", "wss://orderweb.net/ws/pos")]
    [InlineData("https://orderweb.net/api", "ws://orderweb.net/ws/pos")]
    [InlineData("https://user:password@orderweb.net/api", "wss://orderweb.net/ws/pos")]
    [InlineData("https://orderweb.net/api", "wss://orderweb.net/ws/pos?apiKey=secret")]
    public void CloudEndpoints_RejectInsecureOrEmbeddedCredentials(string apiUrl, string socketUrl)
    {
        Assert.False(CloudEndpointSecurityPolicy.Validate(apiUrl, socketUrl).IsValid);
    }

    [Fact]
    public void CloudEndpoints_AcceptEncryptedTransport()
    {
        Assert.True(CloudEndpointSecurityPolicy.Validate(
            "https://orderweb.net/api",
            "wss://orderweb.net/ws/pos").IsValid);
    }

    [Theory]
    [InlineData(10, 0, 10)]
    [InlineData(10, 4, 6)]
    [InlineData(10, 15, 0)]
    public void RefundPolicy_ComputesRemainingAmount(decimal original, decimal refunded, decimal expected)
    {
        Assert.Equal(expected, RefundAmountPolicy.Remaining(original, refunded));
    }

    [Fact]
    public void RefundPolicy_RejectsCumulativeOverRefund()
    {
        Assert.False(RefundAmountPolicy.Validate(6.01m, 10m, 4m).IsValid);
        Assert.True(RefundAmountPolicy.Validate(6m, 10m, 4m).IsValid);
    }

    [Fact]
    public void BackupPackage_IsEncryptedAndAuthenticated()
    {
        var plaintext = Encoding.UTF8.GetBytes("customer and financial backup data");
        var encrypt = typeof(BackupService).GetMethod("EncryptPackage", BindingFlags.NonPublic | BindingFlags.Static);
        var decrypt = typeof(BackupService).GetMethod("DecryptPackageIfNeeded", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(encrypt);
        Assert.NotNull(decrypt);

        var encrypted = Assert.IsType<byte[]>(encrypt!.Invoke(null, [plaintext, "Strong-Database-Password-123!"]));
        Assert.DoesNotContain("customer and financial", Encoding.UTF8.GetString(encrypted));

        var decrypted = Assert.IsType<byte[]>(decrypt!.Invoke(null, [encrypted, "Strong-Database-Password-123!"]));
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void BackupPackage_RejectsWrongCredential()
    {
        var plaintext = Encoding.UTF8.GetBytes("financial backup");
        var encrypt = typeof(BackupService).GetMethod("EncryptPackage", BindingFlags.NonPublic | BindingFlags.Static)!;
        var decrypt = typeof(BackupService).GetMethod("DecryptPackageIfNeeded", BindingFlags.NonPublic | BindingFlags.Static)!;
        var encrypted = Assert.IsType<byte[]>(encrypt.Invoke(null, [plaintext, "Correct-Database-Password-123!"]));

        var exception = Assert.Throws<TargetInvocationException>(() =>
            decrypt.Invoke(null, [encrypted, "Wrong-Database-Password-123!"]));
        Assert.IsType<InvalidDataException>(exception.InnerException);
    }
}
