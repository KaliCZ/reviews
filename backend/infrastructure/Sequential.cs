using UUIDNext;

namespace Reviews.Infrastructure;

// UUIDv7 for entity PKs — time-ordered so inserts append to the right of the
// btree index instead of scattering pages. Use anywhere a Guid lands in the DB.
public static class Sequential
{
    public static Guid NewGuid() => Uuid.NewDatabaseFriendly(Database.PostgreSql);
}
