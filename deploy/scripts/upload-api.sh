#!/usr/bin/env bash
# Wysyła paczkę API na hosting przez FTPS (TLS wymagany, certyfikat weryfikowany).
# Kolejność: app_offline.htm (ANCM zatrzymuje aplikację) → synchronizacja z usuwaniem
# nieaktualnych plików → usunięcie app_offline.htm (start nowej wersji).
# Nie dotyka appsettings.Production.json (konfiguracja i sekrety serwera) ani logs/.
#
# Użycie: upload-api.sh <katalog paczki>
# Zmienne: API_FTP_HOST, API_FTP_USER, API_FTP_PASSWORD, API_FTP_PATH (katalog witryny).
set -euo pipefail

package="${1:?Użycie: upload-api.sh <katalog paczki>}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
: "${API_FTP_HOST:?Brak API_FTP_HOST}" "${API_FTP_USER:?Brak API_FTP_USER}"
: "${API_FTP_PASSWORD:?Brak API_FTP_PASSWORD}" "${API_FTP_PATH:?Brak API_FTP_PATH}"

# Hasło przekazywane zmienną środowiskową (--env-password), nie w argumentach procesu.
export LFTP_PASSWORD="$API_FTP_PASSWORD"

lftp --env-password -u "$API_FTP_USER" "ftp://${API_FTP_HOST}" <<LFTP
set cmd:fail-exit true
set ftp:ssl-force true
set ftp:ssl-protect-data true
set ssl:verify-certificate true
set net:max-retries 3
set net:timeout 30
cd "${API_FTP_PATH}"
mkdir -pf logs
put "${root}/deploy/app_offline.htm" -o app_offline.htm
mirror --reverse --delete --verbose=1 --exclude-glob appsettings.Production.json --exclude-glob app_offline.htm --exclude ^logs/ "${package}" .
rm app_offline.htm
bye
LFTP
echo "API wysłane."
