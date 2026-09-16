namespace OrderWeb.SharedUI.Views;

/// <summary>Advance Orders range chips (maps to Mother <c>today</c> / <c>tomorrow</c> / <c>7d</c>).</summary>
public enum AdvanceOrderRange
{
    Today,
    Tomorrow,
    Next7Days
}

/// <summary>
/// Host-neutral row for Advance Orders list.
/// Hosts map Mother/Client DTOs → this; SharedUI does not call HTTP.
/// </summary>
public sealed record AdvanceOrderRowPresentation(
    string OrderId,
    string OrderNumber,
    string OrderType,
    string ScheduledDisplay,
    string CustomerName,
    string? CustomerPhone,
    string TotalText,
    bool KitchenPrinted,
    string Status);

public sealed class AdvanceOrderRangeChangedEventArgs(AdvanceOrderRange range) : EventArgs
{
    public AdvanceOrderRange Range { get; } = range;
}

public sealed class AdvanceOrderRowTappedEventArgs(AdvanceOrderRowPresentation row) : EventArgs
{
    public AdvanceOrderRowPresentation Row { get; } = row;
}

public sealed class AdvanceOrderPrintKitchenEventArgs(AdvanceOrderRowPresentation row) : EventArgs
{
    public AdvanceOrderRowPresentation Row { get; } = row;
}

/// <summary>Sample rows for SharedUI smoke / host stub preview.</summary>
public static class AdvanceOrderSampleData
{
    public static IReadOnlyList<AdvanceOrderRowPresentation> TodayRows { get; } =
    [
        new(
            OrderId: "sample-adv-1",
            OrderNumber: "#ADV1001",
            OrderType: "Collection",
            ScheduledDisplay: "15 Sep 18:30",
            CustomerName: "Jane Smith",
            CustomerPhone: "07700 900123",
            TotalText: "£24.50",
            KitchenPrinted: false,
            Status: "Pending"),
        new(
            OrderId: "sample-adv-2",
            OrderNumber: "#ADV1002",
            OrderType: "Delivery",
            ScheduledDisplay: "15 Sep 19:45",
            CustomerName: "Alex Khan",
            CustomerPhone: "07700 900456",
            TotalText: "£31.00",
            KitchenPrinted: true,
            Status: "Printed")
    ];

    public static IReadOnlyList<AdvanceOrderRowPresentation> RowsFor(AdvanceOrderRange range) =>
        range switch
        {
            AdvanceOrderRange.Tomorrow =>
            [
                new(
                    OrderId: "sample-adv-3",
                    OrderNumber: "#ADV2001",
                    OrderType: "Collection",
                    ScheduledDisplay: "16 Sep 12:00",
                    CustomerName: "Sam Lee",
                    CustomerPhone: "07700 900789",
                    TotalText: "£15.80",
                    KitchenPrinted: false,
                    Status: "Pending")
            ],
            AdvanceOrderRange.Next7Days => TodayRows.Concat(
            [
                new(
                    OrderId: "sample-adv-4",
                    OrderNumber: "#ADV3001",
                    OrderType: "Delivery",
                    ScheduledDisplay: "18 Sep 17:15",
                    CustomerName: "Priya Patel",
                    CustomerPhone: null,
                    TotalText: "£42.00",
                    KitchenPrinted: false,
                    Status: "Pending")
            ]).ToList(),
            _ => TodayRows
        };

    public static string ApiRangeKey(AdvanceOrderRange range) =>
        range switch
        {
            AdvanceOrderRange.Tomorrow => "tomorrow",
            AdvanceOrderRange.Next7Days => "7d",
            _ => "today"
        };
}
