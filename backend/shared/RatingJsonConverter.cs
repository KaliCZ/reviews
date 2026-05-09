using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reviews.Infrastructure.Entities;

// Wire format is integer 1..5; out-of-range fails at parse time so callers
// never need to range-check a Rating-typed parameter.
public sealed class RatingJsonConverter : JsonConverter<Rating>
{
    public override Rating Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
            throw new JsonException($"Rating must be a JSON number (1..5), got {reader.TokenType}.");

        var value = reader.GetInt32();
        if (value is < 1 or > 5)
            throw new JsonException($"Rating must be between 1 and 5, got {value}.");

        return (Rating)value;
    }

    public override void Write(Utf8JsonWriter writer, Rating value, JsonSerializerOptions options) =>
        writer.WriteNumberValue((int)value);
}
