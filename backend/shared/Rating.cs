using System.Text.Json.Serialization;

namespace Reviews.Infrastructure.Entities;

// Ordinals match the wire/db format: One=1..Five=5, so `(short)rating`
// round-trips with the smallint column with no conversion.
[JsonConverter(typeof(RatingJsonConverter))]
public enum Rating : short
{
    One = 1,
    Two = 2,
    Three = 3,
    Four = 4,
    Five = 5,
}
