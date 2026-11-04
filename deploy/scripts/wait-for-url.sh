#!/usr/bin/env bash
# Czeka, aż adres odpowie 200 (po publikacji). Użycie: wait-for-url.sh <url> [próby] [odstęp s]
set -euo pipefail

url="${1:?Użycie: wait-for-url.sh <url> [próby] [odstęp s]}"
attempts="${2:-10}"
delay="${3:-15}"

for ((i = 1; i <= attempts; i++)); do
  status="$(curl --silent --output /dev/null --write-out '%{http_code}' --max-time 30 "$url" || true)"
  if [[ "$status" == "200" ]]; then
    echo "OK 200: $url"
    exit 0
  fi
  echo "Próba ${i}/${attempts}: $url → ${status:-brak odpowiedzi}"
  if ((i < attempts)); then sleep "$delay"; fi
done
echo "::error::$url nie odpowiada 200."
exit 1
