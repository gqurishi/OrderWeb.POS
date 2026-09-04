namespace POS_in_NET.Models;

public enum ServiceChargeClassification
{
    Optional,
    Compulsory
}

public sealed class TableServiceChargeSettings
{
    public bool IsEnabled { get; set; }
    public decimal Percentage { get; set; }
    public ServiceChargeClassification Classification { get; set; } = ServiceChargeClassification.Optional;
    public int? UpdatedByUserId { get; set; }
    public string UpdatedByName { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }

    public TableServiceChargeSettings Copy() => new()
    {
        IsEnabled = IsEnabled,
        Percentage = Percentage,
        Classification = Classification,
        UpdatedByUserId = UpdatedByUserId,
        UpdatedByName = UpdatedByName,
        UpdatedAt = UpdatedAt
    };
}

public readonly record struct ServiceChargeValidationResult(bool IsValid, string Message, decimal NormalizedPercentage);

public static class TableServiceChargePolicy
{
    public const decimal MinimumPercentage = 0.01m;
    public const decimal MaximumPercentage = 30.00m;
    public const decimal ExampleSubtotal = 100.00m;

    public static ServiceChargeValidationResult Validate(bool isEnabled, decimal percentage)
    {
        if (!isEnabled)
        {
            return new ServiceChargeValidationResult(true, string.Empty, 0.00m);
        }

        if (percentage < MinimumPercentage || percentage > MaximumPercentage)
        {
            return new ServiceChargeValidationResult(
                false,
                $"Enter a percentage between {MinimumPercentage:0.00}% and {MaximumPercentage:0.00}%.",
                0.00m);
        }

        if (decimal.Round(percentage, 2) != percentage)
        {
            return new ServiceChargeValidationResult(false, "Use no more than two decimal places.", 0.00m);
        }

        return new ServiceChargeValidationResult(true, string.Empty, percentage);
    }

    public static decimal CalculateExampleCharge(decimal percentage) =>
        decimal.Round(ExampleSubtotal * percentage / 100m, 2, MidpointRounding.AwayFromZero);
}
