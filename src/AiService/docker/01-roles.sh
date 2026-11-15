#!/bin/sh
# Runs once, on first start of an empty data volume, as the bootstrap superuser.
# Creates the non-owner, non-BYPASSRLS application role plus the dev and test databases.
# The Alembic migration (run as the owner via DATABASE_URL_MIGRATE) grants table privileges.
set -eu
: "${MEDVYA_AI_APP_PASSWORD:?MEDVYA_AI_APP_PASSWORD must be set}"
psql -v ON_ERROR_STOP=1 -v app_pw="$MEDVYA_AI_APP_PASSWORD" --username "$POSTGRES_USER" --dbname postgres <<'SQL'
CREATE ROLE medvya_ai_app LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD :'app_pw';
CREATE DATABASE medvya_ai;
CREATE DATABASE medvya_ai_test;
SQL
