#!/usr/bin/env bash
# Stosuje migracje wszystkich kontekstów w stałej kolejności (deploy/scripts/contexts.sh).
# Pierwszy błąd kończy skrypt kodem ≠ 0; kolejne konteksty nie są uruchamiane, a publikacja
# kodu nie powinna nastąpić. Wymaga zbudowanego rozwiązania (Release) i narzędzia dotnet-ef.
#
# Połączenie: zmienna środowiskowa ConnectionStrings__StoneFlow (host czyta ją jak każdą
# konfigurację), nigdy argument wiersza poleceń. Skrypt nie wypisuje jej wartości.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
# shellcheck source=contexts.sh
source deploy/scripts/contexts.sh

# dotnet ef bez ustawionego środowiska przyjmuje Development (user secrets,
# appsettings.Development.json). Migracje produkcyjne budują host jak w Production.
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export DOTNET_ENVIRONMENT="${DOTNET_ENVIRONMENT:-Production}"

if [[ -z "${ConnectionStrings__StoneFlow:-}" ]]; then
  echo "::error::Brak zmiennej ConnectionStrings__StoneFlow — migracje nie zostały uruchomione."
  exit 2
fi

for entry in "${STONEFLOW_CONTEXTS[@]}"; do
  project="${entry%%|*}"
  context="${entry##*|}"
  echo "Migracje: ${context}"
  if ! dotnet ef database update \
    --no-build --configuration Release \
    --project "$project" --startup-project "$STONEFLOW_STARTUP_PROJECT" \
    --context "$context"; then
    echo "::error::Migracja ${context} nie powiodła się. Kolejne konteksty i publikacja zatrzymane — patrz docs/wdrozenia.md."
    exit 1
  fi
  echo "Migracje: ${context} — OK"
done
