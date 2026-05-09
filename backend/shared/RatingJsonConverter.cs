using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reviews.Infrastructure.Entities;

// Wire format for Rating is the integer 1..5 — natural for star-rating UIs,
// stable across translations, and matches what most catalog APIs publish.
// The enum has named members for those exact integers (One..Five), so the
// converter is a thin "int → cast" with a guard. An out-of-range payload
// fails at JSON parse time, so the controller can take a Rating-typed
// parameter without an "is the rating between 1 and 5" check anywhere.
//
// Annotated on the enum directly so anything that serialises/deserialises
// Rating picks the converter up automatically — STJ, Temporal payloads,
// custom callers.
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
