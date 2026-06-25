using System.Text.Json.Serialization;

namespace POS_in_NET.Models;

public sealed class OrderWebAddressLookupResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("postcode")]
    public string? Postcode { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("from_cache")]
    public bool FromCache { get; set; }

    [JsonPropertyName("stale_cache")]
    public bool StaleCache { get; set; }

    [JsonPropertyName("addresses")]
    public List<OrderWebAddressItem> Addresses { get; set; } = new();

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("suggestions")]
    public List<string>? Suggestions { get; set; }
}

public sealed class OrderWebAddressItem
{
    [JsonPropertyName("line1")]
    public string Line1 { get; set; } = string.Empty;

    [JsonPropertyName("line2")]
    public string Line2 { get; set; } = string.Empty;

    [JsonPropertyName("line3")]
    public string Line3 { get; set; } = string.Empty;

    [JsonPropertyName("post_town")]
    public string PostTown { get; set; } = string.Empty;

    [JsonPropertyName("county")]
    public string County { get; set; } = string.Empty;

    [JsonPropertyName("postcode")]
    public string Postcode { get; set; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; set; } = "GB";

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }

    [JsonPropertyName("uprn")]
    public string? Uprn { get; set; }

    [JsonPropertyName("udprn")]
    public long? Udprn { get; set; }

    [JsonPropertyName("formatted")]
    public string Formatted { get; set; } = string.Empty;
}
