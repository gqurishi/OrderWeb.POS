namespace POS_in_NET.Models;

/// <summary>
/// Customer loyalty information from OrderWeb.net
/// </summary>
public class LoyaltyCustomer
{
    public string Phone { get; set; } = string.Empty;
    public string DisplayPhone { get; set; } = string.Empty;
    public string LoyaltyCardNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? Email { get; set; }
    
    // Points Information
    public int PointsBalance { get; set; }
    public int TotalPointsEarned { get; set; }
    public int TotalPointsRedeemed { get; set; }
    
    // Tier Information
    public string? TierLevel { get; set; }
    public int NextTierPoints { get; set; }
    
    // Account Information
    public int IsActive { get; set; }
    public DateTime JoinedDate { get; set; }
    public DateTime? LastOrderDate { get; set; }
    
    // Statistics
    public int TotalOrders { get; set; }
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

/// <summary>
/// Loyalty points transaction history
/// </summary>
public class LoyaltyTransaction
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int PointsChange { get; set; }
    public string TransactionType { get; set; } = string.Empty; // earned, redeemed, adjusted
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public decimal? OrderValue { get; set; }
    
    // UI Helper Properties
    public string PointsDisplay => PointsChange > 0 ? $"+{PointsChange} pts" : $"{PointsChange} pts";
    public string TypeDisplay => TransactionType switch
    {
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
    public bool Success { get; set; }
    public string? Error { get; set; }
    public LoyaltyCustomer? Customer { get; set; }
    public List<LoyaltyTransaction> Transactions { get; set; } = new();
}

/// <summary>
/// Request model for adding/redeeming points
/// </summary>
public class LoyaltyActionRequest
{
    public string Phone { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // "add" or "redeem"
    public int Points { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Request model for creating new customer
/// </summary>
public class CreateCustomerRequest
{
    public string Phone { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
}
