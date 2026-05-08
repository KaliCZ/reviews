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

# Provision the application schema inside the reviews database. The app keeps
# its tables here so they're cleanly separated from public/extensions, and
# every connection to this database starts with reviews as the default schema.
echo "Provisioning reviews schema"
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname reviews <<-EOSQL
  CREATE SCHEMA IF NOT EXISTS reviews;
  ALTER DATABASE reviews SET search_path TO reviews, public;
EOSQL
