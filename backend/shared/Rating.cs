using System.Text.Json.Serialization;

namespace Reviews.Infrastructure.Entities;

// Lives in the shared assembly so workflow inputs (in Reviews.Shared) and
// entities (in Reviews.Infrastructure) both consume the same type. Namespace
// stays `Reviews.Infrastructure.Entities` for entity-side ergonomics — most
// references come from Review.cs and the API DTOs, where the entities
// namespace is already imported.
//
// Members named so 1:1 with the underlying integer ordinal — One stores as 1,
// Five as 5 — which keeps `(short)rating` round-tripping with the smallint
// column without any conversion math.
//
// JsonConverter on the type means anywhere a Rating crosses the wire (API
// DTOs, Temporal payloads), the converter validates 1..5 at parse time. No
// caller needs to range-check before constructing one.
[JsonConverter(typeof(RatingJsonConverter))]
public enum Rating : short
{
    One = 1,
    Two = 2,
    Three = 3,
    Four = 4,
    Five = 5,
}
