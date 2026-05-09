#!/bin/bash
set -e

# Create the additional databases listed in POSTGRES_MULTIPLE_DATABASES.
# This file runs on first start of an empty postgres data volume; subsequent
# starts are no-ops thanks to PG's docker-entrypoint behaviour.
if [ -n "$POSTGRES_MULTIPLE_DATABASES" ]; then
  echo "Creating databases: $POSTGRES_MULTIPLE_DATABASES"
  for db in $(echo "$POSTGRES_MULTIPLE_DATABASES" | tr ',' ' '); do
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<-EOSQL
      SELECT 'CREATE DATABASE "$db"'
        WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '$db')\gexec
EOSQL
  done
fi

# Pre-create the `reviews` schema inside the reviews database. The connection
# string sets `Search Path=reviews`, so EF Core's first action — writing its
# `__EFMigrationsHistory` table — would fail with "no schema has been
# selected to create in" if the schema didn't exist yet.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname reviews \
  -c 'CREATE SCHEMA IF NOT EXISTS reviews;'
