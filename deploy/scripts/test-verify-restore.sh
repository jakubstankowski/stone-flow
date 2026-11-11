#!/usr/bin/env bash
# Próba sprawdzenia kopii bez hostingu (CI: zadanie deploy-dry-run). Sprawdza deploy/scripts/verify-restore.sh:
#   1. kopia pełna (wszystkie migracje, fikcyjne wypłaty) → sukces, liczby wierszy jak w bazie źródłowej;
#   2. kopia zmigrowana tylko do pierwszego kontekstu → sukces, skrypt dociąga migracje (6 tabel historii);
#   3. plik obcięty → kod ≠ 0;
#   4. brak pliku → kod ≠ 0;
#   5. po każdym przypadku nie zostaje żadna baza robocza StoneFlowRestoreCheck_*.
#
# Zmienne: STONEFLOW_TEST_SQLSERVER i SQLCMD jak w test-migrate.sh (opis w sql-common.sh) oraz:
#   RESTORE_TEST_DIR — katalog na pliki .bak widziany przez SQL Server, zakończony separatorem
#                      (domyślnie InstanceDefaultBackupPath, a gdy go nie ma — InstanceDefaultDataPath);
#   SQL_HOST_EXEC    — przedrostek poleceń wykonywanych na maszynie serwera SQL (obcięcie i usunięcie
#                      plików), w CI "docker exec -i <kontener>"; lokalnie pusty.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
# shellcheck source=contexts.sh
source deploy/scripts/contexts.sh
# shellcheck source=sql-common.sh
source deploy/scripts/sql-common.sh
: "${STONEFLOW_TEST_SQLSERVER:?Brak STONEFLOW_TEST_SQLSERVER}"
: "${SQLCMD:?Brak SQLCMD}"
host_exec="${SQL_HOST_EXEC:-}"

run_id="$(date +%s)$$"
db_full="StoneFlowRestoreTest_full_${run_id}"
db_partial="StoneFlowRestoreTest_partial_${run_id}"

fail() { echo "::error::$1"; exit 1; }

dir="${RESTORE_TEST_DIR:-}"
if [[ -z "$dir" ]]; then
  dir="$(sql master "SELECT CAST(COALESCE(SERVERPROPERTY('InstanceDefaultBackupPath'), SERVERPROPERTY('InstanceDefaultDataPath')) AS nvarchar(4000));")"
fi
[[ -n "$dir" && "$dir" != "NULL" ]] || fail "Brak katalogu kopii — ustaw RESTORE_TEST_DIR."
if [[ "$dir" != */ && "$dir" != *\\ ]]; then
  if [[ "$dir" == *\\* ]]; then dir+="\\"; else dir+="/"; fi
fi
bak_full="${dir}full_${run_id}.bak"
bak_partial="${dir}partial_${run_id}.bak"
bak_broken="${dir}broken_${run_id}.bak"
bak_missing="${dir}missing_${run_id}.bak"

host_path() { # ścieżka pliku dla poleceń na maszynie serwera (lokalnie na Windows: postać uniksowa)
  if [[ -z "$host_exec" ]] && command -v cygpath >/dev/null 2>&1; then
    cygpath -u "$1"
  else
    printf '%s' "$1"
  fi
}

cleanup() {
  drop_database "$db_full" || true
  drop_database "$db_partial" || true
  for file in "$bak_full" "$bak_partial" "$bak_broken"; do
    $host_exec rm -f "$(host_path "$file")" || true
  done
  rm -f "${log:-}"
}

work_databases() { # liczba baz roboczych verify-restore.sh na serwerze
  sql master "SELECT COUNT(*) FROM sys.databases WHERE name LIKE 'StoneFlowRestoreCheck[_]%';"
}

backup() { # backup <baza> <plik>
  sql master "BACKUP DATABASE [$1] TO DISK = N'$2' WITH INIT, COPY_ONLY;" >/dev/null
}

expect_no_work_database() {
  [[ "$(work_databases)" == "$before" ]] || fail "Po przypadku „$1” została baza robocza StoneFlowRestoreCheck_*."
}

before="$(work_databases)"
log="$(mktemp)"
trap cleanup EXIT

echo "== 1. Kopia pełna"
sql master "CREATE DATABASE [${db_full}];" >/dev/null
ConnectionStrings__StoneFlow="$(connection_for "$db_full")" deploy/scripts/migrate.sh >/dev/null \
  || fail "Migracje bazy źródłowej nie powiodły się."
sql "$db_full" "SET QUOTED_IDENTIFIER ON; INSERT INTO expenses.OwnerWithdrawals (Id, Person, Amount, WithdrawnOn, Method, CreatedAtUtc, UpdatedAtUtc)
  VALUES (NEWID(), N'Właściciel A', 100.00, '2026-11-05', N'Cash', SYSUTCDATETIME(), SYSUTCDATETIME()),
         (NEWID(), N'Właściciel B', 200.00, '2026-11-06', N'Transfer', SYSUTCDATETIME(), SYSUTCDATETIME());"
backup "$db_full" "$bak_full"
deploy/scripts/verify-restore.sh "$bak_full" | tee "$log" || fail "verify-restore.sh nie przyjął poprawnej kopii pełnej."
rows=0
while read -r table count; do
  expected="$(sql "$db_full" "SELECT COUNT_BIG(*) FROM ${table};")"
  [[ "$count" == "$expected" ]] || fail "Liczba wierszy ${table}: ${count}, w bazie źródłowej ${expected}."
  rows=$((rows + 1))
done < <(sed -n 's/^Wiersze \([^:]*\): \([0-9]*\)$/\1 \2/p' "$log")
[[ "$rows" -gt 0 ]] || fail "verify-restore.sh nie wypisał liczby wierszy tabel głównych."
grep -qx 'Wiersze expenses.OwnerWithdrawals: 2' "$log" || fail "Brak 2 wierszy expenses.OwnerWithdrawals w odtworzonej kopii."
expect_no_work_database "kopia pełna"

echo "== 2. Kopia częściowo zmigrowana"
first="${STONEFLOW_CONTEXTS[0]}"
sql master "CREATE DATABASE [${db_partial}];" >/dev/null
ConnectionStrings__StoneFlow="$(connection_for "$db_partial")" ASPNETCORE_ENVIRONMENT=Production DOTNET_ENVIRONMENT=Production \
  dotnet ef database update --no-build --configuration Release \
  --project "${first%%|*}" --startup-project "$STONEFLOW_STARTUP_PROJECT" --context "${first##*|}" >/dev/null \
  || fail "Migracja pierwszego kontekstu bazy źródłowej nie powiodła się."
backup "$db_partial" "$bak_partial"
deploy/scripts/verify-restore.sh "$bak_partial" | tee "$log" || fail "verify-restore.sh nie zmigrował kopii częściowej."
grep -qx 'Ostatnia migracja orders: brak' "$log" || fail "Kopia częściowa powinna nie mieć migracji orders."
grep -qx 'Kopia sprawdzona: odtworzona, spójna, zmigrowana.' "$log" || fail "Brak potwierdzenia kontroli kopii częściowej."
expect_no_work_database "kopia częściowa"

echo "== 3. Plik uszkodzony"
# shellcheck disable=SC2016 # $0 i $1 rozwija sh na maszynie serwera
$host_exec sh -c 'head -c 65536 "$0" > "$1"' "$(host_path "$bak_full")" "$(host_path "$bak_broken")"
if deploy/scripts/verify-restore.sh "$bak_broken"; then
  fail "verify-restore.sh przyjął obcięty plik kopii."
fi
expect_no_work_database "plik uszkodzony"

echo "== 4. Brak pliku"
if deploy/scripts/verify-restore.sh "$bak_missing"; then
  fail "verify-restore.sh przyjął nieistniejący plik."
fi
expect_no_work_database "brak pliku"

echo "Próba odtworzenia: wszystkie scenariusze zgodne z oczekiwaniami."
