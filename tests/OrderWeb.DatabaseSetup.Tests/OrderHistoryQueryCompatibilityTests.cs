using POS_in_NET.Services;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public sealed class OrderHistoryQueryCompatibilityTests
{
    [Fact]
    public void LegacySchema_UsesNullPaymentStatusProjection()
    {
        Assert.Equal("NULL", OrderHistoryQueryCompatibility.PaymentStatusProjection(false));
    }

    [Fact]
    public void CurrentSchema_UsesStoredPaymentStatus()
    {
        Assert.Equal("o.payment_status", OrderHistoryQueryCompatibility.PaymentStatusProjection(true));
    }
}
