using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace POS_in_NET.Converters;

/// <summary>
/// Deserializes integers from null, strings, or decimal JSON numbers (common in OrderWeb APIs).
/// </summary>
public sealed class FlexibleIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return 0;
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var intValue))
                {
                    return intValue;
                }

                if (reader.TryGetInt64(out var longValue) &&
                    longValue is >= int.MinValue and <= int.MaxValue)
                {
                    return (int)longValue;
                }

                if (reader.TryGetDecimal(out var decimalValue))
                {
                    return (int)Math.Round(decimalValue, MidpointRounding.AwayFromZero);
                }

                return 0;
            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return 0;
                }

                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                {
                    return parsedInt;
                }

                if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedDecimal))
                {
                    return (int)Math.Round(parsedDecimal, MidpointRounding.AwayFromZero);
                }

                return 0;
            default:
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
