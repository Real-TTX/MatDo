#!/usr/bin/env sh
# Baut den MatDo-Container neu und startet den lokalen Stack. "--clean" löscht vorher die Volumes.
set -e
cd "$(dirname "$0")/.."
if [ "$1" = "--clean" ]; then
  docker compose -f docker-compose.local.yml down -v --remove-orphans
fi
docker compose -f docker-compose.local.yml up -d --build --remove-orphans
echo "MatDo läuft auf http://localhost:6006 (pgAdmin: http://localhost:6007)"
