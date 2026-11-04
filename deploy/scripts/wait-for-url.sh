#!/usr/bin/env bash
# Czeka, aż adres odpowie oczekiwanym statusem (po publikacji).
# Użycie: wait-for-url.sh <url> [status, domyślnie 200] [próby] [odstęp s]
set -euo pipefail

url="${1:?Użycie: wait-for-url.sh <url> [status] [próby] [odstęp s]}"
expected="${2:-200}"
attempts="${3:-10}"
delay="${4:-15}"

for ((i = 1; i <= attempts; i++)); do
  status="$(curl --silent --output /dev/null --write-out '%{http_code}' --max-time 30 "$url" || true)"
  if [[ "$status" == "$expected" ]]; then
    echo "OK ${expected}: $url"
    exit 0
  fi
  echo "Próba ${i}/${attempts}: $url → ${status:-brak odpowiedzi}"
  if ((i < attempts)); then sleep "$delay"; fi
done
echo "::error::$url nie odpowiada statusem ${expected}."
exit 1
