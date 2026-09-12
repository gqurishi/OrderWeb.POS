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
            System.Diagnostics.Debug.WriteLine($" FlexibleDecimalConverter: Parsing string '{stringValue}'");
            if (decimal.TryParse(stringValue, out var result))
            {
                System.Diagnostics.Debug.WriteLine($" FlexibleDecimalConverter: Successfully parsed to {result}");
                return result;
            }
            System.Diagnostics.Debug.WriteLine($" FlexibleDecimalConverter: Failed to parse '{stringValue}', returning 0");
            return 0;
        }
        // Handle number format: 100.00
        else if (reader.TokenType == JsonTokenType.Number)
        {
            var numValue = reader.GetDecimal();
            System.Diagnostics.Debug.WriteLine($" FlexibleDecimalConverter: Got number {numValue}");
            return numValue;
        }
        
        System.Diagnostics.Debug.WriteLine($" FlexibleDecimalConverter: Unexpected token type {reader.TokenType}, returning 0");
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
/// Flexible converter for OrderWeb suggested amount arrays where values may be
/// numbers or strings.
/// </summary>
public class FlexibleDecimalListConverter : JsonConverter<List<decimal>>
{
    public override List<decimal> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var values = new List<decimal>();
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return values;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var number))
            {
                values.Add(number);
            }
            else if (reader.TokenType == JsonTokenType.String &&
                     decimal.TryParse(reader.GetString(), out var parsed))
            {
                values.Add(parsed);
            }
            else
            {
                reader.Skip();
            }
        }

        return values;
    }

    public override void Write(Utf8JsonWriter writer, List<decimal> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteNumberValue(item);
        }
        writer.WriteEndArray();
    }
}

/// <summary>
/// OrderWeb receipt.lines may be plain strings or objects ({ text / line / content }).
/// Strict List&lt;string&gt; deserialization was failing top-up/sell after a successful cloud write.
/// </summary>
public class FlexibleReceiptLinesConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var lines = new List<string>();
        if (reader.TokenType == JsonTokenType.Null)
        {
            return lines;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            var single = ReadLineToken(ref reader);
            if (!string.IsNullOrWhiteSpace(single))
            {
                lines.Add(single);
            }

            return lines;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var line = ReadLineToken(ref reader);
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var line in value)
        {
            writer.WriteStringValue(line);
        }
        writer.WriteEndArray();
    }

    private static string? ReadLineToken(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                return reader.TryGetDecimal(out var number) ? number.ToString("0.##") : reader.GetDouble().ToString("0.##");
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            case JsonTokenType.StartObject:
                using (var doc = JsonDocument.ParseValue(ref reader))
                {
                    return ExtractLineFromObject(doc.RootElement);
                }
            case JsonTokenType.StartArray:
                reader.Skip();
                return null;
            default:
                reader.Skip();
                return null;
        }
    }

    private static string? ExtractLineFromObject(JsonElement element)
    {
        foreach (var name in new[] { "text", "line", "content", "value", "message", "label" })
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    return prop.Value.GetString();
                }

                if (prop.Value.ValueKind == JsonValueKind.Number)
                {
                    return prop.Value.ToString();
                }
            }
        }

        // Last resort: compact JSON so we never throw on unknown receipt shapes.
        return element.GetRawText();
    }
}

public static class GiftCardLookupPurpose
{
    public const string Activate = "activate";
    public const string TopUp = "topup";
    public const string Redeem = "redeem";

    public static string Normalize(string? purpose)
    {
        return purpose?.Trim().ToLowerInvariant() switch
        {
            Activate => Activate,
            TopUp => TopUp,
            "top-up" => TopUp,
            "top_up" => TopUp,
            Redeem => Redeem,
            _ => Redeem
        };
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
    [JsonConverter(typeof(FlexibleDecimalConverter))] //  CRITICAL: Handle string "100.00"
    public decimal Balance { get; set; }
    
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty; // active, used, expired
    
    [JsonPropertyName("card_type")]
    public string CardType { get; set; } = string.Empty; // digital, physical
    
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
    
    [JsonPropertyName("expiry_date")]
    public DateTime? ExpiryDate { get; set; }

    [JsonPropertyName("can_use")]
    public bool? CanUse { get; set; }

    [JsonPropertyName("is_expired")]
    public bool? IsExpiredFlag { get; set; }
    
    // UI Helper Properties
    [JsonIgnore]
    public string BalanceDisplay => $"£{Balance:F2}";
    
    [JsonIgnore]
    public string StatusDisplay => Status?.ToLower() switch
    {
        "active" => " Active",
        "used" => " Fully Used",
        "expired" => " Expired",
        _ => Status ?? "Unknown"
    };
    
    [JsonIgnore]
    public bool IsActive => IsUsable;
    
    [JsonIgnore]
    public bool IsExpired => IsExpiredFlag == true || (ExpiryDate.HasValue && ExpiryDate.Value < DateTime.Now);

    [JsonIgnore]
    public bool IsUsable =>
        CanUse != false
        && !IsExpired
        && string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase)
        && Balance > 0;
    
    [JsonIgnore]
    public string ExpiryDisplay => ExpiryDate.HasValue 
        ? $"Expires: {ExpiryDate.Value:MMM dd, yyyy}" 
        : "No expiry";
    
    [JsonIgnore]
    public string CardTypeDisplay => CardType?.ToLower() switch
    {
        "digital" => " Digital",
        "physical" => " Physical",
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

    [JsonPropertyName("found")]
    public bool? Found { get; set; }

    [JsonPropertyName("can_use")]
    public bool? CanUse { get; set; }

    [JsonPropertyName("is_expired")]
    public bool? IsExpired { get; set; }
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    
    [JsonPropertyName("message")]
    public string? Message { get; set; }
    
    [JsonPropertyName("gift_card")]
    public GiftCard? GiftCard { get; set; }

    [JsonPropertyName("till")]
    public GiftCardTillInstructions? Till { get; set; }

    [JsonIgnore]
    public bool CanProceed => Till?.CanProceed ?? Success;

    [JsonIgnore]
    public string? StatusMessage => Till?.StatusMessage ?? Message ?? Error;

    [JsonIgnore]
    public bool CanQueueForRetry { get; set; }
}

public class GiftCardTillInstructions
{
    [JsonPropertyName("can_proceed")]
    public bool? CanProceed { get; set; }

    [JsonPropertyName("status_message")]
    public string? StatusMessage { get; set; }

    [JsonPropertyName("suggested_amounts")]
    [JsonConverter(typeof(FlexibleDecimalListConverter))]
    public List<decimal> SuggestedAmounts { get; set; } = new();
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

    [JsonPropertyName("orderId")]
    public string? OrderId { get; set; }
}

public class GiftCardActivateRequest
{
    [JsonPropertyName("cardNumber")]
    public string CardNumber { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("paymentMethod")]
    public string PaymentMethod { get; set; } = string.Empty;

    [JsonPropertyName("orderId")]
    public string? OrderId { get; set; }

    [JsonPropertyName("tillOrderId")]
    public string? TillOrderId { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class GiftCardSellRequest
{
    [JsonPropertyName("cardNumber")]
    public string? CardNumber { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("paymentMethod")]
    public string PaymentMethod { get; set; } = string.Empty;

    [JsonPropertyName("orderId")]
    public string? OrderId { get; set; }

    [JsonPropertyName("tillOrderId")]
    public string? TillOrderId { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class GiftCardTopUpRequest
{
    [JsonPropertyName("cardNumber")]
    public string CardNumber { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("paymentMethod")]
    public string PaymentMethod { get; set; } = string.Empty;

    [JsonPropertyName("orderId")]
    public string? OrderId { get; set; }

    [JsonPropertyName("tillOrderId")]
    public string? TillOrderId { get; set; }

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

    [JsonPropertyName("new_balance")]
    [JsonConverter(typeof(FlexibleNullableDecimalConverter))]
    public decimal? NewBalance { get; set; }

    [JsonPropertyName("previous_balance")]
    [JsonConverter(typeof(FlexibleNullableDecimalConverter))]
    public decimal? PreviousBalance { get; set; }
    
    [JsonPropertyName("amount_redeemed")]
    [JsonConverter(typeof(FlexibleNullableDecimalConverter))] // Handle string or number
    public decimal? AmountRedeemed { get; set; }

    [JsonPropertyName("redeemed_amount")]
    [JsonConverter(typeof(FlexibleNullableDecimalConverter))]
    public decimal? RedeemedAmount { get; set; }

    [JsonPropertyName("is_fully_redeemed")]
    public bool? IsFullyRedeemed { get; set; }
    
    [JsonIgnore]
    public decimal? EffectiveRemainingBalance => NewBalance ?? RemainingBalance;
    
    [JsonIgnore]
    public decimal? EffectiveAmountRedeemed => RedeemedAmount ?? AmountRedeemed;
    
    [JsonIgnore]
    public string RemainingBalanceDisplay => EffectiveRemainingBalance.HasValue ? $"£{EffectiveRemainingBalance.Value:F2}" : "N/A";
    
    [JsonIgnore]
    public string AmountRedeemedDisplay => EffectiveAmountRedeemed.HasValue ? $"£{EffectiveAmountRedeemed.Value:F2}" : "N/A";
}

public class GiftCardTransactionResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("gift_card")]
    public GiftCard? GiftCard { get; set; }

    /// <summary>Parsed manually — OrderWeb may return object-shaped receipt lines.</summary>
    [JsonIgnore]
    public GiftCardReceipt? Receipt { get; set; }

    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("remaining_balance")]
    [JsonConverter(typeof(FlexibleNullableDecimalConverter))]
    public decimal? RemainingBalance { get; set; }

    [JsonPropertyName("new_balance")]
    [JsonConverter(typeof(FlexibleNullableDecimalConverter))]
    public decimal? NewBalance { get; set; }

    [JsonIgnore]
    public decimal? EffectiveBalance => NewBalance ?? RemainingBalance ?? GiftCard?.Balance;

    [JsonIgnore]
    public bool CanQueueForRetry { get; set; }
}

public class GiftCardReceipt
{
    [JsonPropertyName("lines")]
    [JsonConverter(typeof(FlexibleReceiptLinesConverter))]
    public List<string> Lines { get; set; } = new();
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
