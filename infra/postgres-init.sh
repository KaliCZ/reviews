#!/bin/bash
set -e

# Create the additional databases listed in POSTGRES_MULTIPLE_DATABASES.
# This file runs on first start of an empty postgres data volume; subsequent
# starts are no-ops thanks to PG's docker-entrypoint behaviour.
#
# That's all this script does now. The reviews schema, tables, and seed data
# are owned by EF Core migrations + a startup seeder in Reviews.Infrastructure
# (run from the API at boot, lock-protected so it's safe under multiple
# replicas). Putting it there means schema changes are typed, versioned, and
# don't depend on the postgres volume being torn down to take effect.
if [ -n "$POSTGRES_MULTIPLE_DATABASES" ]; then
  echo "Creating databases: $POSTGRES_MULTIPLE_DATABASES"
  for db in $(echo "$POSTGRES_MULTIPLE_DATABASES" | tr ',' ' '); do
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<-EOSQL
      SELECT 'CREATE DATABASE "$db"'
        WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '$db')\gexec
EOSQL
  done
fi
