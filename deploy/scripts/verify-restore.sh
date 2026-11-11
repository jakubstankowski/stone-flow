#!/usr/bin/env bash
# Sprawdzenie kopii bazy (.bak) bez dotykania istniejących baz (SF-21, docs/operations/kopie-i-odtworzenie.md):
#   1. „Odczyt kopii”   — RESTORE FILELISTONLY i RESTORE VERIFYONLY;
#   2. „Odtworzenie”    — RESTORE DATABASE do nowej bazy roboczej StoneFlowRestoreCheck_<id>
#                         (pliki w domyślnych katalogach serwera, nigdy WITH REPLACE);
#   3. „Spójność”       — DBCC CHECKDB;
#   4. „Stan kopii”     — ostatnia migracja każdego schematu z contexts.sh;
#   5. „Migracje”       — deploy/scripts/migrate.sh na bazie roboczej (kopia może być starsza niż kod);
#   6. „Kontrola”       — 6 tabel historii migracji i liczby wierszy tabel głównych.
# Baza robocza jest usuwana na końcu (także po błędzie), chyba że KEEP_DB=1.
#
# Użycie: verify-restore.sh <ścieżka pliku .bak widziana przez SQL Server>
#   np. /var/opt/mssql/backup/stoneflow.bak (kontener) albo C:\kopie\stoneflow.bak (LocalDB).
# Zmienne: STONEFLOW_TEST_SQLSERVER i SQLCMD jak w test-migrate.sh (opis w sql-common.sh);
#          KEEP_DB=1 — zostawia bazę roboczą do ręcznego przeglądu (nazwa w wyjściu).
# Wymaga zbudowanego rozwiązania (Release) i dotnet-ef, jak migrate.sh.
# Skrypt nie wypisuje connection stringu ani danych z tabel — tylko liczby i nazwy migracji.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
# shellcheck source=contexts.sh
source deploy/scripts/contexts.sh
# shellcheck source=sql-common.sh
source deploy/scripts/sql-common.sh

# Tabele główne: zamówienia, wpisy pieniężne zamówień, wydatki, definicje cykliczne, wypłaty,
# wpisy audytu. Liczby wierszy porównuje się z aplikacją (i w test-verify-restore.sh z bazą źródłową).
MAIN_TABLES=(
  orders.Orders
  orders.Payments
  orders.Refunds
  orders.SalesCorrections
  expenses.Expenses
  expenses.RecurringExpenseDefinitions
  expenses.OwnerWithdrawals
  reporting.AuditEntries
)

fail() { echo "::error::$1"; exit 1; }

if [[ $# -ne 1 || -z "$1" ]]; then
  echo "::error::Użycie: deploy/scripts/verify-restore.sh <ścieżka pliku .bak widziana przez SQL Server>"
  exit 2
fi
: "${STONEFLOW_TEST_SQLSERVER:?Brak STONEFLOW_TEST_SQLSERVER}"
: "${SQLCMD:?Brak SQLCMD}"

backup="${1//\'/\'\'}" # literał T-SQL
db="StoneFlowRestoreCheck_$(date +%s)$$"
restore_started=0

cleanup() {
  [[ "$restore_started" == "1" ]] || return 0
  if [[ "${KEEP_DB:-}" == "1" ]]; then
    echo "KEEP_DB=1: baza robocza ${db} została na serwerze — usuń ją ręcznie po przeglądzie."
  else
    drop_database "$db" || echo "::warning::Nie udało się usunąć bazy roboczej ${db}."
  fi
}
trap cleanup EXIT

schema_of() { # AccessDbContext → access
  local context="${1##*|}"
  printf '%s' "${context%DbContext}" | tr '[:upper:]' '[:lower:]'
}

echo "== Odczyt kopii"
if ! filelist="$($SQLCMD -d master -h -1 -W -s '|' -b -Q "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'${backup}';" 2>&1 | tr -d '\r')"; then
  printf '%s\n' "$filelist" >&2
  fail "Krok „Odczyt kopii”: plik nie istnieje albo nie jest kopią SQL Server."
fi
moves=""
n=0
while IFS='|' read -r logical _physical type _rest; do
  [[ -z "$logical" ]] && continue
  case "$type" in
    D) dir_property="InstanceDefaultDataPath"; ext="mdf" ;;
    L) dir_property="InstanceDefaultLogPath"; ext="ldf" ;;
    *) fail "Krok „Odczyt kopii”: nieobsługiwany typ pliku w kopii (${type})." ;;
  esac
  dir="$(sql master "SELECT CAST(SERVERPROPERTY('${dir_property}') AS nvarchar(4000));")"     || fail "Krok „Odtworzenie”: nie udało się odczytać ${dir_property}."
  [[ -n "$dir" && "$dir" != "NULL" ]] || fail "Krok „Odtworzenie”: serwer nie podaje katalogu ${dir_property}."
  n=$((n + 1))
  moves+="${moves:+, }MOVE N'${logical//\'/\'\'}' TO N'${dir}${db}_${n}.${ext}'"
done <<<"$filelist"
[[ "$n" -gt 0 ]] || fail "Krok „Odczyt kopii”: plik nie istnieje albo nie jest kopią SQL Server."
if ! output="$($SQLCMD -d master -b -Q "RESTORE VERIFYONLY FROM DISK = N'${backup}';" 2>&1 | tr -d '\r')"; then
  printf '%s\n' "$output" >&2
  fail "Krok „Odczyt kopii”: kopia jest nieczytelna (RESTORE VERIFYONLY)."
fi

echo "== Odtworzenie do bazy roboczej ${db}"
[[ "$(sql master "SELECT CASE WHEN DB_ID('${db}') IS NULL THEN 0 ELSE 1 END;")" == "0" ]] \
  || { echo "::error::Krok „Odtworzenie”: baza ${db} już istnieje — skrypt jej nie nadpisuje."; exit 1; }
restore_started=1
if ! output="$($SQLCMD -d master -b -Q "RESTORE DATABASE [${db}] FROM DISK = N'${backup}' WITH ${moves};" 2>&1 | tr -d '\r')"; then
  printf '%s\n' "$output" >&2
  if grep -q 'Msg 3169,' <<<"$output"; then
    fail "Krok „Odtworzenie”: kopia pochodzi z nowszej wersji SQL Server niż serwer próby — użyj zgodnego obrazu (patrz docs/operations/kopie-i-odtworzenie.md)."
  fi
  fail "Krok „Odtworzenie”: RESTORE DATABASE nie powiódł się."
fi

echo "== Spójność"
sql master "SET QUOTED_IDENTIFIER ON; DBCC CHECKDB ([${db}]) WITH NO_INFOMSGS;" \
  || fail "Krok „Spójność”: DBCC CHECKDB zgłosił błędy."

echo "== Stan kopii"
for entry in "${STONEFLOW_CONTEXTS[@]}"; do
  schema="$(schema_of "$entry")"
  last="$(sql "$db" "IF OBJECT_ID(N'[${schema}].[__EFMigrationsHistory]') IS NULL SELECT N'brak' ELSE EXEC(N'SELECT ISNULL(MAX(MigrationId), N''brak'') FROM [${schema}].[__EFMigrationsHistory]');")" \
    || fail "Krok „Stan kopii”: nie udało się odczytać historii migracji ${schema}."
  echo "Ostatnia migracja ${schema}: ${last}"
done

echo "== Migracje"
ConnectionStrings__StoneFlow="$(connection_for "$db")" deploy/scripts/migrate.sh \
  || fail "Krok „Migracje”: migrate.sh nie zmigrował kopii."

echo "== Kontrola"
history="$(sql "$db" "SELECT COUNT(*) FROM sys.tables WHERE name = '__EFMigrationsHistory';")"
[[ "$history" == "6" ]] || fail "Krok „Kontrola”: oczekiwano 6 tabel historii migracji, jest ${history}."
for table in "${MAIN_TABLES[@]}"; do
  count="$(sql "$db" "IF OBJECT_ID(N'${table}') IS NULL SELECT -1 ELSE EXEC(N'SELECT COUNT_BIG(*) FROM ${table}');")" \
    || fail "Krok „Kontrola”: nie udało się policzyć wierszy ${table}."
  [[ "$count" != "-1" ]] || fail "Krok „Kontrola”: brak tabeli ${table} po migracjach."
  echo "Wiersze ${table}: ${count}"
done

echo "Kopia sprawdzona: odtworzona, spójna, zmigrowana."
