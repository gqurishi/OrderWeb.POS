namespace OrderWeb.SharedUI.Views;

/// <summary>Live Order filter tabs (Mother green selected style).</summary>
public enum LiveOrderFilter
{
    All,
    Collection,
    Delivery,
    Table
}

/// <summary>Order card vs Mother table-session card.</summary>
public enum LiveOrderCardKind
{
    Order,
    TableSession
}

/// <summary>Optional badge chip; host supplies text/colors, SharedUI only draws.</summary>
public sealed record LiveOrderBadgePresentation(
    string Text,
    string BackgroundColor,
    string TextColor);

/// <summary>
/// Host-neutral card DTO for Live Order board.
/// Hosts map Order / TableSession / MotherOrderState → this; SharedUI does not load data.
/// </summary>
public sealed record LiveOrderCardPresentation(
    string Key,
    LiveOrderCardKind Kind,
    string Title,
    string OrderNumber,
    string? Subtitle,
    string TotalText,
    string TimeText,
    string AccentColorHex,
    IReadOnlyList<LiveOrderBadgePresentation>? Badges = null);

public sealed class LiveOrderFilterChangedEventArgs(LiveOrderFilter filter) : EventArgs
{
    public LiveOrderFilter Filter { get; } = filter;
}

public sealed class LiveOrderCardTappedEventArgs(LiveOrderCardPresentation card) : EventArgs
{
    public LiveOrderCardPresentation Card { get; } = card;
}

/// <summary>Sample cards for visual smoke of SharedUI board (Mother look).</summary>
public static class LiveOrderSampleData
{
    public static IReadOnlyList<LiveOrderCardPresentation> OrderCards { get; } =
    [
        new(
            Key: "sample-col-1",
            Kind: LiveOrderCardKind.Order,
            Title: "Collection",
            OrderNumber: "#KITT0001",
            Subtitle: "Jane Smith",
            TotalText: "£12.50",
            TimeText: "14:25 · 11/09",
            AccentColorHex: "#10B981",
            Badges:
            [
                new("WEB", "#DBEAFE", "#1D4ED8"),
                new("COLLECTION", "#E0F2FE", "#0369A1"),
                new("CASH DUE", "#FEF3C7", "#B45309")
            ]),
        new(
            Key: "sample-del-1",
            Kind: LiveOrderCardKind.Order,
            Title: "Delivery",
            OrderNumber: "#5d0cbd12",
            Subtitle: null,
            TotalText: "£18.00",
            TimeText: "14:30 · 11/09",
            AccentColorHex: "#10B981"),
        new(
            Key: "sample-stale-1",
            Kind: LiveOrderCardKind.Order,
            Title: "Collection",
            OrderNumber: "#DRAFT99",
            Subtitle: "Guest left",
            TotalText: "£6.00",
            TimeText: "13:10 · 11/09",
            AccentColorHex: "#DC2626")
    ];

    public static IReadOnlyList<LiveOrderCardPresentation> TableSessionCards { get; } =
    [
        new(
            Key: "sample-table-1",
            Kind: LiveOrderCardKind.TableSession,
            Title: "Table",
            OrderNumber: "#TBL001",
            Subtitle: "Table 4",
            TotalText: "£10.00",
            TimeText: "2 guests · 14:25",
            AccentColorHex: "#10B981"),
        new(
            Key: "sample-table-2",
            Kind: LiveOrderCardKind.TableSession,
            Title: "Table",
            OrderNumber: string.Empty,
            Subtitle: "Table 7",
            TotalText: "£0.00",
            TimeText: "4 guests · 14:40",
            AccentColorHex: "#F59E0B")
    ];

    public static string EmptyTextFor(LiveOrderFilter filter) => filter switch
    {
        LiveOrderFilter.Collection => "No open collection orders",
        LiveOrderFilter.Delivery => "No open delivery orders",
        LiveOrderFilter.Table => "No active table sessions",
        _ => "No open local orders"
    };

    public static IReadOnlyList<LiveOrderCardPresentation> CardsFor(LiveOrderFilter filter) => filter switch
    {
        LiveOrderFilter.Collection => OrderCards.Where(c => c.Title == "Collection").ToList(),
        LiveOrderFilter.Delivery => OrderCards.Where(c => c.Title == "Delivery").ToList(),
        LiveOrderFilter.Table => TableSessionCards,
        _ => OrderCards
    };
}
