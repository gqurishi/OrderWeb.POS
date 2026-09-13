namespace POS_in_NET.Services;

/// <summary>
/// One label decision for every print path. Input is the saved order type and
/// source (local or web), never the takeaway flag that hides collection and delivery.
/// </summary>
public static class LabelPrintRules
{
    public static LabelPrintDecision Decide(string? orderType, string? sourceChannel)
    {
        var website = IsWebsite(sourceChannel);
        var kind = Classify(orderType);

        if (kind == LabelFulfillment.Unknown)
        {
            kind = website ? LabelFulfillment.Collection : LabelFulfillment.Skip;
        }

        if (kind == LabelFulfillment.Skip)
        {
            return LabelPrintDecision.Skip;
        }

        var title = kind == LabelFulfillment.Delivery
            ? (website ? "WEB DELIVERY" : "DELIVERY")
            : (website ? "WEB COLLECTION" : "COLLECTION");
        return new LabelPrintDecision(true, title);
    }

    /// <summary>
    /// Copies already on the kitchen line. A new line holds the full quantity.
    /// An add line holds only the extra. Do not subtract the previous quantity again.
    /// </summary>
    public static int StickerCopies(int lineQuantity) => Math.Clamp(lineQuantity, 0, 99);

    public static string FormatSticker(LabelPrintDecision decision, string? reference)
    {
        var number = string.IsNullOrWhiteSpace(reference) ? "" : reference.Trim();
        return string.IsNullOrEmpty(number)
            ? decision.StickerTitle
            : $"{decision.StickerTitle} #{number}";
    }

    private static bool IsWebsite(string? sourceChannel)
    {
        var source = sourceChannel?.Trim() ?? "";
        return source.Equals("web", StringComparison.OrdinalIgnoreCase)
            || source.Equals("online", StringComparison.OrdinalIgnoreCase)
            || source.Equals("orderweb", StringComparison.OrdinalIgnoreCase);
    }

    private static LabelFulfillment Classify(string? orderType)
    {
        var type = (orderType ?? string.Empty).Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
        if (type.Length == 0)
        {
            return LabelFulfillment.Unknown;
        }

        return type switch
        {
            "delivery" or "del" or "home_delivery" => LabelFulfillment.Delivery,
            "pickup" or "pick_up" or "collection" or "collect" or "col" or "takeaway" or "take_away" => LabelFulfillment.Collection,
            "table" or "tbl" or "dine_in" or "dinein" or "restaurant"
                or "eat_in" or "in_house" or "inhouse" or "sit_in" or "sitin" => LabelFulfillment.Skip,
            _ => LabelFulfillment.Skip
        };
    }

    private enum LabelFulfillment
    {
        Skip,
        Unknown,
        Collection,
        Delivery
    }
}

public sealed record LabelPrintDecision(bool ShouldPrint, string StickerTitle)
{
    public static LabelPrintDecision Skip { get; } = new(false, "");
}
