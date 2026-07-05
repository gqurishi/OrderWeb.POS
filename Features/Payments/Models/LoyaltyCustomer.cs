using System.Text.Json;
using System.Text.Json.Serialization;

namespace POS_in_NET.Models;

public class LoyaltyFlexibleIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        {
            return number;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var decimalValue))
        {
            return (int)Math.Round(decimalValue, MidpointRounding.AwayFromZero);
        }

        if (reader.TokenType == JsonTokenType.String && decimal.TryParse(reader.GetString(), out var parsed))
        {
            return (int)Math.Round(parsed, MidpointRounding.AwayFromZero);
        }

        return 0;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}

/// <summary>
/// Customer loyalty information from OrderWeb.net
/// </summary>
public class LoyaltyCustomer
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("display_phone")]
    public string DisplayPhone { get; set; } = string.Empty;

    [JsonPropertyName("loyalty_card_number")]
    public string LoyaltyCardNumber { get; set; } = string.Empty;

    [JsonPropertyName("customer_name")]
    public string CustomerName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
    
    // Points Information
    [JsonPropertyName("points_balance")]
    [JsonConverter(typeof(LoyaltyFlexibleIntConverter))]
    public int PointsBalance { get; set; }

    [JsonPropertyName("total_points_earned")]
    [JsonConverter(typeof(LoyaltyFlexibleIntConverter))]
    public int TotalPointsEarned { get; set; }

    [JsonPropertyName("total_points_redeemed")]
    [JsonConverter(typeof(LoyaltyFlexibleIntConverter))]
    public int TotalPointsRedeemed { get; set; }
    
    // Tier Information
    [JsonPropertyName("tier_level")]
    public string? TierLevel { get; set; }

    [JsonPropertyName("next_tier_points")]
    [JsonConverter(typeof(LoyaltyFlexibleIntConverter))]
    public int NextTierPoints { get; set; }
    
    // Account Information
    [JsonPropertyName("is_active")]
    [JsonConverter(typeof(LoyaltyFlexibleIntConverter))]
    public int IsActive { get; set; }

    [JsonPropertyName("joined_date")]
    public DateTime JoinedDate { get; set; }

    [JsonPropertyName("last_order_date")]
    public DateTime? LastOrderDate { get; set; }
    
    // Statistics
    [JsonPropertyName("total_orders")]
    [JsonConverter(typeof(LoyaltyFlexibleIntConverter))]
    public int TotalOrders { get; set; }

    [JsonPropertyName("total_spent")]
    public string TotalSpent { get; set; } = "£0.00";
    
    // UI Helper Properties
    public bool HasPoints => PointsBalance > 0;
    public string PointsDisplay => $"{PointsBalance:N0} pts";
    public string TierDisplay => string.IsNullOrWhiteSpace(TierLevel) ? "Bronze" : TierLevel;
    public string PointsToNextTier => NextTierPoints > 0 ? $"{NextTierPoints} points to {GetNextTier()}" : "Max tier reached!";
    
    private string GetNextTier()
    {
        return TierLevel?.ToLower() switch
        {
            "bronze" => "Silver",
            "silver" => "Gold",
            "gold" => "Platinum",
            _ => "Next Tier"
        };
    }
}

public class LoyaltySummary
{
    [JsonPropertyName("points_balance")]
    [JsonConverter(typeof(LoyaltyFlexibleIntConverter))]
    public int PointsBalance { get; set; }

    [JsonPropertyName("money_value")]
    public decimal MoneyValue { get; set; }

    [JsonPropertyName("can_redeem")]
    public bool CanRedeem { get; set; }

    [JsonPropertyName("total_earned")]
    [JsonConverter(typeof(LoyaltyFlexibleIntConverter))]
    public int TotalEarned { get; set; }

    [JsonPropertyName("total_redeemed")]
    [JsonConverter(typeof(LoyaltyFlexibleIntConverter))]
    public int TotalRedeemed { get; set; }

    [JsonPropertyName("last_transaction")]
    public DateTime? LastTransaction { get; set; }

    [JsonPropertyName("member_since")]
    public DateTime? MemberSince { get; set; }
}

/// <summary>
/// Loyalty points transaction history
/// </summary>
public class LoyaltyTransaction
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("points_change")]
    [JsonConverter(typeof(LoyaltyFlexibleIntConverter))]
    public int PointsChange { get; set; }

    [JsonPropertyName("points_amount")]
    [JsonConverter(typeof(LoyaltyFlexibleIntConverter))]
    public int PointsAmount
    {
        get => PointsChange;
        set => PointsChange = value;
    }

    [JsonPropertyName("transaction_type")]
    public string TransactionType { get; set; } = string.Empty; // earned, redeemed, adjusted

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("order_value")]
    public decimal? OrderValue { get; set; }
    
    // UI Helper Properties
    public string PointsDisplay => PointsChange > 0 ? $"+{PointsChange} pts" : $"{PointsChange} pts";
    public string TypeDisplay => TransactionType switch
    {
        "bonus" => "Bonus",
        "earned" => "Earned",
        "redeemed" => "Redeemed",
        "adjusted" => "Adjusted",
        _ => TransactionType
    };
    public string OrderValueDisplay => OrderValue.HasValue ? $"£{OrderValue:F2}" : "-";
}

/// <summary>
/// API response wrapper for loyalty lookup
/// </summary>
public class LoyaltyLookupResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("customer")]
    public LoyaltyCustomer? Customer { get; set; }

    [JsonPropertyName("customer_exists")]
    public bool CustomerExists { get; set; }

    [JsonPropertyName("loyalty")]
    public LoyaltySummary? Loyalty { get; set; }

    [JsonPropertyName("transactions")]
    public List<LoyaltyTransaction> Transactions { get; set; } = new();

    [JsonPropertyName("recent_transactions")]
    public List<LoyaltyTransaction> RecentTransactions { get; set; } = new();

    public LoyaltyLookupResponse Normalize()
    {
        if (Customer != null)
        {
            if (string.IsNullOrWhiteSpace(Customer.CustomerName) && !string.IsNullOrWhiteSpace(Customer.Name))
            {
                Customer.CustomerName = Customer.Name.Trim();
            }

            if (string.IsNullOrWhiteSpace(Customer.DisplayPhone))
            {
                Customer.DisplayPhone = Customer.Phone;
            }

            if (Loyalty != null)
            {
                if (Customer.PointsBalance <= 0 && Loyalty.PointsBalance > 0)
                {
                    Customer.PointsBalance = Loyalty.PointsBalance;
                }

                Customer.TotalPointsEarned = Loyalty.TotalEarned;
                Customer.TotalPointsRedeemed = Loyalty.TotalRedeemed;
                Customer.LastOrderDate = Loyalty.LastTransaction ?? Loyalty.MemberSince;
                Customer.IsActive = Loyalty.CanRedeem || Loyalty.PointsBalance > 0 ? 1 : Customer.IsActive;
            }
        }

        Transactions ??= new List<LoyaltyTransaction>();
        RecentTransactions ??= new List<LoyaltyTransaction>();

        if (Transactions.Count == 0 && RecentTransactions.Count > 0)
        {
            Transactions = RecentTransactions;
        }

        foreach (var transaction in Transactions)
        {
            if (string.IsNullOrWhiteSpace(transaction.Description) && !string.IsNullOrWhiteSpace(transaction.Reason))
            {
                transaction.Description = transaction.Reason.Trim();
            }
        }

        return this;
    }
}

/// <summary>
/// Request model for adding/redeeming points
/// </summary>
public class LoyaltyActionRequest
{
    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty; // "add" or "redeem"

    [JsonPropertyName("points")]
    public int Points { get; set; }

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Request model for creating new customer
/// </summary>
public class CreateCustomerRequest
{
    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}
