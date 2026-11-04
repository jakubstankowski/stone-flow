#!/usr/bin/env bash
# Publikuje tylko bieżącą głowę main: ponowne uruchomienie starszego przebiegu nie może
# nadpisać nowszego kodu ani zastosować migracji starszej wersji po nowszej.
set -euo pipefail

: "${GITHUB_SHA:?Brak GITHUB_SHA}"
head="$(git ls-remote origin refs/heads/main | cut -f1)"
if [[ "$head" != "$GITHUB_SHA" ]]; then
  echo "::error::Commit ${GITHUB_SHA} nie jest bieżącą głową main (${head}). Publikacja pominięta — wdroży ją przebieg nowszego commita."
  exit 1
fi
echo "Commit ${GITHUB_SHA} jest bieżącą głową main."
