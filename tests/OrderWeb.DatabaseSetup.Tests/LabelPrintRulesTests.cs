using POS_in_NET.Services;
using Xunit;

namespace OrderWeb.DatabaseSetup.Tests;

public sealed class LabelPrintRulesTests
{
    [Theory]
    [InlineData("table", "local")]
    [InlineData("Table", "local")]
    [InlineData("tbl", null)]
    [InlineData("dine_in", "local")]
    [InlineData("dine-in", "local")]
    [InlineData("dinein", "web")]
    [InlineData("restaurant", "web")]
    [InlineData("eat in", "online")]
    [InlineData("in-house", "orderweb")]
    [InlineData("sit_in", "web")]
    [InlineData("", "local")]
    [InlineData(null, "local")]
    [InlineData("unknown", "local")]
    [InlineData("unknown", "web")]
    public void TableAndUnknownTillOrdersSkip(string? orderType, string? sourceChannel)
    {
        var decision = LabelPrintRules.Decide(orderType, sourceChannel);
        Assert.False(decision.ShouldPrint);
        Assert.Equal("", decision.StickerTitle);
    }

    [Theory]
    [InlineData("pickup", "local", "COLLECTION")]
    [InlineData("collection", "local", "COLLECTION")]
    [InlineData("collect", "local", "COLLECTION")]
    [InlineData("col", "local", "COLLECTION")]
    [InlineData("delivery", "local", "DELIVERY")]
    [InlineData("del", "local", "DELIVERY")]
    [InlineData("home-delivery", "local", "DELIVERY")]
    [InlineData("pick_up", "web", "WEB COLLECTION")]
    [InlineData("take_away", "local", "COLLECTION")]
    [InlineData("pickup", "web", "WEB COLLECTION")]
    [InlineData("collection", "online", "WEB COLLECTION")]
    [InlineData("delivery", "web", "WEB DELIVERY")]
    [InlineData("del", "orderweb", "WEB DELIVERY")]
    [InlineData("", "web", "WEB COLLECTION")]
    [InlineData(null, "web", "WEB COLLECTION")]
    public void BagOrdersPrintWithTheRightWords(string? orderType, string? sourceChannel, string title)
    {
        var decision = LabelPrintRules.Decide(orderType, sourceChannel);
        Assert.True(decision.ShouldPrint);
        Assert.Equal(title, decision.StickerTitle);
        Assert.Equal($"{title} #42", LabelPrintRules.FormatSticker(decision, "42"));
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(0, 0)]
    [InlineData(120, 99)]
    public void StickerCopiesUsesTheKitchenLineCount(int lineQuantity, int expected)
    {
        Assert.Equal(expected, LabelPrintRules.StickerCopies(lineQuantity));
    }

    [Theory]
    [InlineData("delivery", "local")]
    [InlineData("Delivery", "web")]
    [InlineData("home_delivery", "web")]
    public void DeliveryNeverUsesTheCollectionWord(string orderType, string source)
    {
        var decision = LabelPrintRules.Decide(orderType, source);
        Assert.True(decision.ShouldPrint);
        Assert.DoesNotContain("COLLECTION", decision.StickerTitle);
        Assert.Contains("DELIVERY", decision.StickerTitle);
        Assert.DoesNotContain("COLLECTION", LabelPrintRules.FormatSticker(decision, "15"));
    }

    [Fact]
    public void StoredTakeawayTypeIsCollection_NotACombinedFlag()
    {
        var decision = LabelPrintRules.Decide("takeaway", "local");
        Assert.True(decision.ShouldPrint);
        Assert.Equal("COLLECTION", decision.StickerTitle);
    }

    [Fact]
    public void TillChecklistUsesOneDecisionForMotherClientAndWebsite()
    {
        AssertSkip("table", "local");
        AssertWords("pickup", "local", "COLLECTION", "1042");
        AssertWords("delivery", "local", "DELIVERY", "1043");

        AssertSkip("table", "local");
        AssertWords("collection", "local", "COLLECTION", "88");
        AssertWords("delivery", "local", "DELIVERY", "89");

        AssertWords("pickup", "web", "WEB COLLECTION", "501");
        AssertWords("collection", "orderweb", "WEB COLLECTION", "502");
        AssertWords("delivery", "web", "WEB DELIVERY", "503");
        AssertWords("takeaway", "online", "WEB COLLECTION", "504");

        AssertSkip("dine_in", "web");
        AssertSkip("restaurant", "web");
        AssertSkip("eat-in", "online");
        AssertSkip("online", "web");
    }

    private static void AssertSkip(string orderType, string source)
    {
        var decision = LabelPrintRules.Decide(orderType, source);
        Assert.False(decision.ShouldPrint);
        Assert.Equal("", decision.StickerTitle);
    }

    private static void AssertWords(string orderType, string source, string title, string reference)
    {
        var decision = LabelPrintRules.Decide(orderType, source);
        Assert.True(decision.ShouldPrint);
        Assert.Equal(title, decision.StickerTitle);
        Assert.Equal($"{title} #{reference}", LabelPrintRules.FormatSticker(decision, reference));
        if (title.Contains("DELIVERY", StringComparison.Ordinal))
        {
            Assert.DoesNotContain("COLLECTION", decision.StickerTitle);
        }
    }
}
