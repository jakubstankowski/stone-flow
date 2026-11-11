# shellcheck shell=bash
# Wspólne funkcje skryptów próbnych SQL (test-migrate.sh, verify-restore.sh, test-verify-restore.sh).
# Plik dołącza się przez `source`; nie ustawia opcji powłoki i nie czyta zmiennych przy dołączeniu.
# Funkcje używają przy wywołaniu:
#   SQLCMD — polecenie sqlcmd z uwierzytelnieniem do serwera, np.
#            "sqlcmd -S (localdb)\MSSQLLocalDB -E" albo
#            "docker exec -i <id> /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P ...";
#   STONEFLOW_TEST_SQLSERVER — connection string do tego serwera (bez bazy lub z master).

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

drop_database() { # drop_database <baza> → usuwa bazę, jeśli istnieje (zamyka otwarte połączenia)
  sql master "IF DB_ID('${1}') IS NOT NULL BEGIN ALTER DATABASE [${1}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${1}]; END" >/dev/null
}
