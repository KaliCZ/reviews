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

# Pre-create the `reviews` schema and the EF migrations history table inside
# the reviews database. The connection string sets `Search Path=reviews`, so
# EF Core's first action — writing its `__EFMigrationsHistory` table — would
# fail with "no schema has been selected to create in" if the schema didn't
# exist yet.
#
# We also pre-create __EFMigrationsHistory itself so EF's startup probe
# (`SELECT FROM __EFMigrationsHistory`) finds an empty table instead of
# throwing "relation does not exist". The throw is benign — EF catches it
# and applies migrations — but it shows up as a `CommandError` in the api
# logs on every fresh boot, which is noisy. Schema mirrors what
# Npgsql.EntityFrameworkCore.PostgreSQL would have created itself.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname reviews <<-EOSQL
  CREATE SCHEMA IF NOT EXISTS reviews;
  CREATE TABLE IF NOT EXISTS reviews."__EFMigrationsHistory" (
      "MigrationId" character varying(150) NOT NULL,
      "ProductVersion" character varying(32) NOT NULL,
      CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
  );
EOSQL
