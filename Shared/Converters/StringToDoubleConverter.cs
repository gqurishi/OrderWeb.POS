using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace POS_in_NET.Converters;

/// <summary>
/// JSON converter that handles both string and numeric values for double fields
/// The OrderWeb.net API returns prices as strings ("0.00") instead of numbers (0.00)
/// This converter allows automatic deserialization of both formats
/// </summary>
public class StringToDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (string.IsNullOrWhiteSpace(stringValue))
            {
                return 0.0;
            }
            
            if (double.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }
            
            throw new JsonException($"Unable to convert \"{stringValue}\" to double.");
        }
        
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetDouble();
        }
        
        throw new JsonException($"Unexpected token type {reader.TokenType} when parsing double.");
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}
