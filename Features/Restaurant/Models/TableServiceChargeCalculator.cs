namespace POS_in_NET.Models;

public enum TableServiceChargeStatus
{
    NotConfigured,
    Applied,
    Removed
}

/// <summary>
/// The single calculation result used for table service charges.
/// Delivery fees and tips are intentionally outside this result.
/// </summary>
public readonly record struct TableServiceChargeCalculation(
    decimal ChargeBasis,
    decimal ServiceCharge,
    decimal OrderTotal);

/// <summary>
/// Pure calculator for table service charges. Keeping this free of UI and
/// database concerns ensures every caller follows the same calculation rules.
/// </summary>
public static class TableServiceChargeCalculator
{
    public const string TableOrderMode = "dine_in";

    public static TableServiceChargeCalculation Calculate(
        string? orderMode,
        decimal itemSubtotal,
        decimal discount,
        decimal percentage,
        bool isApplied = true)
    {
        if (itemSubtotal < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(itemSubtotal), "Item subtotal cannot be negative.");
        }

        if (discount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(discount), "Discount cannot be negative.");
        }

        if (percentage < 0m || percentage > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(percentage), "Service-charge percentage must be between 0 and 100.");
        }

        var chargeBasis = Math.Max(0m, itemSubtotal - discount);
        var isTableOrder = string.Equals(orderMode, TableOrderMode, StringComparison.OrdinalIgnoreCase);
        var serviceCharge = isTableOrder && isApplied && percentage > 0m
            ? decimal.Round(chargeBasis * percentage / 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        return new TableServiceChargeCalculation(
            chargeBasis,
            serviceCharge,
            chargeBasis + serviceCharge);
    }
}

public static class TableOrderFinancialPolicy
{
    public static bool ShouldOfferTip(
        bool isCollectionOrDelivery,
        TableServiceChargeStatus serviceChargeStatus,
        decimal serviceChargePercentage) =>
        !isCollectionOrDelivery
        && serviceChargeStatus == TableServiceChargeStatus.NotConfigured
        && serviceChargePercentage <= 0m;

    public static bool CanChangeServiceCharge(
        bool isTableOrder,
        decimal percentage,
        TableServiceChargeStatus status,
        bool isOrderModifiable,
        decimal totalPaid) =>
        isTableOrder
        && percentage > 0m
        && status is TableServiceChargeStatus.Applied or TableServiceChargeStatus.Removed
        && isOrderModifiable
        && totalPaid <= 0m;

    public static bool ShouldAdoptCurrentServiceCharge(
        bool isTableOrder,
        bool settingEnabled,
        TableServiceChargeStatus savedStatus,
        decimal savedPercentage,
        bool isOrderModifiable,
        decimal totalPaid) =>
        isTableOrder
        && settingEnabled
        && savedStatus == TableServiceChargeStatus.NotConfigured
        && savedPercentage <= 0m
        && isOrderModifiable
        && totalPaid <= 0m;

    public static decimal AllocateTip(decimal tipRemaining, decimal paymentAmount, decimal remainingBalance)
    {
        if (tipRemaining <= 0m || paymentAmount <= 0m || remainingBalance <= 0m)
        {
            return 0m;
        }

        if (paymentAmount >= remainingBalance - 0.009m)
        {
            return tipRemaining;
        }

        return Math.Min(
            tipRemaining,
            decimal.Round(tipRemaining * paymentAmount / remainingBalance, 2, MidpointRounding.AwayFromZero));
    }
}
