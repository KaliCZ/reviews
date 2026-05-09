using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reviews.Api.Services;

// Keyset pagination cursor. Carries every column the four sort orders need
// (created_at + score + rating + id tiebreaker) so the client doesn't have to
// know which fields the server uses for which sort — it just round-trips the
// opaque base64 string.
public record ReviewCursor(
    [property: JsonPropertyName("c")] DateTime CreatedAt,
    [property: JsonPropertyName("s")] int Score,
    [property: JsonPropertyName("r")] short Rating,
    [property: JsonPropertyName("i")] Guid Id)
{
    public string Encode()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(this);
        return Base64Url.EncodeToString(json);
    }

    public static ReviewCursor? TryDecode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var bytes = Base64Url.DecodeFromChars(raw);
            return JsonSerializer.Deserialize<ReviewCursor>(bytes);
        }
        catch
        {
            // Malformed cursor → treat as no cursor (start from the top).
            // Sending a 400 here would be more pedantic but less forgiving;
            // the cursor is opaque and clients shouldn't be fabricating them.
            return null;
        }
    }
}
