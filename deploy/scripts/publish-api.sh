#!/usr/bin/env bash
# Paczka API do wysłania na hosting: framework-dependent (net10.0), ANCM in-process.
# Użycie: publish-api.sh <katalog wyjściowy>. Wymaga zbudowanego rozwiązania (Release).
set -euo pipefail

out="${1:?Użycie: publish-api.sh <katalog wyjściowy>}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

rm -rf "$out"
dotnet publish "$root/api/src/StoneFlow.Api" --configuration Release --no-build --output "$out"

# Konfiguracja środowiska żyje tylko na serwerze; paczka nie może jej zawierać ani nadpisać.
for forbidden in appsettings.Development.json appsettings.Production.json; do
  if [[ -e "$out/$forbidden" ]]; then
    echo "::error::Paczka API zawiera ${forbidden}."
    exit 1
  fi
done
for required in StoneFlow.Api.dll web.config appsettings.json; do
  if [[ ! -e "$out/$required" ]]; then
    echo "::error::W paczce API brakuje ${required}."
    exit 1
  fi
done
echo "Paczka API: $out"
