using UUIDNext;

namespace Reviews.Infrastructure;

// Single chokepoint for entity-PK Guid generation. UUIDv7 is time-ordered, so
// new rows append to the right of PG btree indexes instead of scattering across
// pages — the difference matters for `reviews(Id)` lookups under load.
//
// Use everywhere we'd otherwise call `Guid.NewGuid()` for a value that lands
// in the database. Blob keys / log correlation ids / one-shot identifiers can
// keep using `Guid.NewGuid()` since their distribution doesn't matter.
public static class Sequential
{
    public static Guid NewGuid() => Uuid.NewDatabaseFriendly(Database.PostgreSql);
}
