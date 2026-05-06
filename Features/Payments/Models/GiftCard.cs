using System.Text.Json;
using System.Text.Json.Serialization;

namespace POS_in_NET.Models;

/// <summary>
/// Custom JSON converter to handle balance as either string or number
/// OrderWeb.net API returns balance as STRING "100.00" not decimal number
/// This is the ROOT CAUSE of the JSON parsing error
/// </summary>
public class FlexibleDecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Handle string format: "100.00"
        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            System.Diagnostics.Debug.WriteLine($"🔍 FlexibleDecimalConverter: Parsing string '{stringValue}'");
            if (decimal.TryParse(stringValue, out var result))
            {
                System.Diagnostics.Debug.WriteLine($"✅ FlexibleDecimalConverter: Successfully parsed to {result}");
                return result;
            }
            System.Diagnostics.Debug.WriteLine($"❌ FlexibleDecimalConverter: Failed to parse '{stringValue}', returning 0");
            return 0;
        }
        // Handle number format: 100.00
        else if (reader.TokenType == JsonTokenType.Number)
        {
            var numValue = reader.GetDecimal();
            System.Diagnostics.Debug.WriteLine($"🔍 FlexibleDecimalConverter: Got number {numValue}");
            return numValue;
        }
        
        System.Diagnostics.Debug.WriteLine($"❌ FlexibleDecimalConverter: Unexpected token type {reader.TokenType}, returning 0");
        return 0;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}

/// <summary>
/// Nullable version of FlexibleDecimalConverter
/// </summary>
public class FlexibleNullableDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        
        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (string.IsNullOrWhiteSpace(stringValue))
            {
                return null;
            }
            if (decimal.TryParse(stringValue, out var result))
            {
                return result;
            }
            return null;
        }
        else if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetDecimal();
        }
        
        return null;
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

/// <summary>
/// Gift card information from OrderWeb.net
/// CRITICAL: API returns balance as STRING "100.00" not decimal number!
/// </summary>
public class GiftCard
{
    [JsonPropertyName("card_number")]
    public string CardNumber { get; set; } = string.Empty;
    
    [JsonPropertyName("balance")]
    [JsonConverter(typeof(FlexibleDecimalConverter))] // ⚠️ CRITICAL: Handle string "100.00"
    public decimal Balance { get; set; }
    
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty; // active, used, expired
    
    [JsonPropertyName("card_type")]
    public string CardType { get; set; } = string.Empty; // digital, physical
    
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
    
    [JsonPropertyName("expiry_date")]
    public DateTime? ExpiryDate { get; set; }
    
    // UI Helper Properties
    [JsonIgnore]
    public string BalanceDisplay => $"£{Balance:F2}";
    
    [JsonIgnore]
    public string StatusDisplay => Status?.ToLower() switch
    {
        "active" => "🟢 Active",
        "used" => "❌ Fully Used",
        "expired" => "🔴 Expired",
        _ => Status ?? "Unknown"
    };
    
    [JsonIgnore]
    public bool IsActive => Status?.ToLower() == "active" && Balance > 0;
    
    [JsonIgnore]
    public bool IsExpired => ExpiryDate.HasValue && ExpiryDate.Value < DateTime.Now;
    
    [JsonIgnore]
    public string ExpiryDisplay => ExpiryDate.HasValue 
        ? $"Expires: {ExpiryDate.Value:MMM dd, yyyy}" 
        : "No expiry";
    
    [JsonIgnore]
    public string CardTypeDisplay => CardType?.ToLower() switch
    {
        "digital" => "💳 Digital",
        "physical" => "🎴 Physical",
        _ => CardType ?? "Unknown"
    };
}

/// <summary>
/// API response for gift card lookup
/// </summary>
public class GiftCardLookupResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    
    [JsonPropertyName("message")]
    public string? Message { get; set; }
    
    [JsonPropertyName("gift_card")]
    public GiftCard? GiftCard { get; set; }
}

/// <summary>
/// Request model for gift card lookup
/// </summary>
public class GiftCardLookupRequest
{
    [JsonPropertyName("card_number")]
    public string CardNumber { get; set; } = string.Empty;
}

/// <summary>
/// Request model for gift card redemption
/// </summary>
public class GiftCardRedeemRequest
{
    [JsonPropertyName("card_number")]
    public string CardNumber { get; set; } = string.Empty;
    
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Response for gift card redemption
/// </summary>
public class GiftCardRedeemResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    
    [JsonPropertyName("message")]
    public string? Message { get; set; }
    
    [JsonPropertyName("remaining_balance")]
    [JsonConverter(typeof(FlexibleNullableDecimalConverter))] // Handle string or number
    public decimal? RemainingBalance { get; set; }
    
    [JsonPropertyName("amount_redeemed")]
    [JsonConverter(typeof(FlexibleNullableDecimalConverter))] // Handle string or number
    public decimal? AmountRedeemed { get; set; }
    
    [JsonIgnore]
    public string RemainingBalanceDisplay => RemainingBalance.HasValue ? $"£{RemainingBalance.Value:F2}" : "N/A";
    
    [JsonIgnore]
    public string AmountRedeemedDisplay => AmountRedeemed.HasValue ? $"£{AmountRedeemed.Value:F2}" : "N/A";
}

/// <summary>
/// WebSocket event for gift card purchase notification
/// </summary>
public class GiftCardPurchaseEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "gift_card_purchased";
    
    [JsonPropertyName("tenant")]
    public string Tenant { get; set; } = string.Empty;
    
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
    
    [JsonPropertyName("data")]
    public GiftCardPurchaseData? Data { get; set; }
}

/// <summary>
/// Gift card purchase data from WebSocket
/// </summary>
public class GiftCardPurchaseData
{
    [JsonPropertyName("cardNumber")]
    public string CardNumber { get; set; } = string.Empty;
    
    [JsonPropertyName("initialBalance")]
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal InitialBalance { get; set; }
    
    [JsonPropertyName("currentBalance")]
    [JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal CurrentBalance { get; set; }
    
    [JsonPropertyName("purchasedBy")]
    public string PurchasedBy { get; set; } = string.Empty;
    
    [JsonPropertyName("recipientName")]
    public string? RecipientName { get; set; }
    
    [JsonPropertyName("recipientEmail")]
    public string? RecipientEmail { get; set; }
    
    [JsonPropertyName("expiryDate")]
    public DateTime? ExpiryDate { get; set; }
    
    [JsonPropertyName("purchasedAt")]
    public DateTime PurchasedAt { get; set; }
}
