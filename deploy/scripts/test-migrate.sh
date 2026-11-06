#!/usr/bin/env bash
# Próba migracji bez hostingu (CI: zadanie deploy-dry-run). Sprawdza deploy/scripts/migrate.sh:
#   1. pusta baza → wszystkie konteksty zmigrowane (tabela historii w każdym schemacie);
#   2. ponowne uruchomienie → sukces bez zmian;
#   3. błąd w środku sekwencji (kolizja w schemacie orders) → kod ≠ 0, access zmigrowany,
#      kolejne konteksty nieuruchomione;
#   4. nieosiągalny serwer → kod ≠ 0.
#
# Zmienne: STONEFLOW_TEST_SQLSERVER — connection string do serwera (bez bazy lub z master);
#          SQLCMD — polecenie sqlcmd z uwierzytelnieniem do tego serwera, np.
#          "sqlcmd -S (localdb)\MSSQLLocalDB -E" albo "docker exec -i <id> /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P ...".
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
: "${STONEFLOW_TEST_SQLSERVER:?Brak STONEFLOW_TEST_SQLSERVER}"
: "${SQLCMD:?Brak SQLCMD}"

run_id="$(date +%s)$$"
db_ok="StoneFlowDeployTest_ok_${run_id}"
db_fail="StoneFlowDeployTest_fail_${run_id}"

sql() { # sql <baza> <zapytanie> → wynik bez nagłówków
  $SQLCMD -d "$1" -h -1 -W -b -Q "SET NOCOUNT ON; $2" | tr -d '\r' | sed '/^$/d'
}

connection_for() { # connection string do wskazanej bazy (zastępuje/dodaje Database=)
  local part key result=""
  local -a parts
  IFS=';' read -ra parts <<<"$STONEFLOW_TEST_SQLSERVER"
  for part in "${parts[@]}"; do
    key="${part%%=*}"
    key="$(printf '%s' "$key" | tr -d '[:space:]' | tr '[:upper:]' '[:lower:]')"
    [[ -z "$key" || "$key" == "database" || "$key" == "initialcatalog" ]] && continue
    result+="${part};"
  done
  printf '%sDatabase=%s' "$result" "$1"
}

cleanup() {
  for db in "$db_ok" "$db_fail"; do
    sql master "IF DB_ID('${db}') IS NOT NULL BEGIN ALTER DATABASE [${db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${db}]; END" >/dev/null || true
  done
}
trap cleanup EXIT

fail() { echo "::error::$1"; exit 1; }

history_tables() { # liczba tabel __EFMigrationsHistory w bazie
  sql "$1" "SELECT COUNT(*) FROM sys.tables WHERE name = '__EFMigrationsHistory';"
}

echo "== 1. Pusta baza"
sql master "CREATE DATABASE [${db_ok}];" >/dev/null
ConnectionStrings__StoneFlow="$(connection_for "$db_ok")" deploy/scripts/migrate.sh \
  || fail "Migracje na pustej bazie nie powiodły się."
[[ "$(history_tables "$db_ok")" == "6" ]] || fail "Oczekiwano 6 tabel historii migracji."

echo "== 2. Ponowne uruchomienie"
ConnectionStrings__StoneFlow="$(connection_for "$db_ok")" deploy/scripts/migrate.sh \
  || fail "Ponowne uruchomienie migracji nie powiodło się."
[[ "$(history_tables "$db_ok")" == "6" ]] || fail "Ponowne uruchomienie zmieniło liczbę tabel historii."

echo "== 3. Błąd w środku sekwencji"
sql master "CREATE DATABASE [${db_fail}];" >/dev/null
sql "$db_fail" "EXEC('CREATE SCHEMA orders'); CREATE TABLE orders.__EFMigrationsHistory (Kolizja int NOT NULL);" >/dev/null
if ConnectionStrings__StoneFlow="$(connection_for "$db_fail")" deploy/scripts/migrate.sh; then
  fail "migrate.sh zakończył się sukcesem mimo błędu migracji orders."
fi
[[ "$(sql "$db_fail" "SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = 'access' AND t.name = '__EFMigrationsHistory';")" == "1" ]] \
  || fail "access powinien być zmigrowany przed błędem."
[[ "$(sql "$db_fail" "SELECT COUNT(*) FROM sys.schemas WHERE name IN ('expenses', 'reporting', 'integrations', 'scheduling');")" == "0" ]] \
  || fail "Konteksty po orders nie powinny zostać uruchomione."

echo "== 4. Nieosiągalny serwer"
if ConnectionStrings__StoneFlow="Server=tcp:127.0.0.1,1;Database=StoneFlow;User Id=sa;Password=unused;TrustServerCertificate=True;Connect Timeout=2" \
  deploy/scripts/migrate.sh; then
  fail "migrate.sh zakończył się sukcesem mimo nieosiągalnego serwera."
fi

echo "Próba migracji: wszystkie scenariusze zgodne z oczekiwaniami."
