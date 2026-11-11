# Kopie zapasowe i odtworzenie bazy

Procedura kopii bazy SQL Stone Flow (SF-21, PRD §12 D-08). Część kroków wykonuje tylko
właściciel albo administrator na jego zlecenie (oznaczone **[właściciel]**); dowody trafiają do
[`odbior-mvp.md`](odbior-mvp.md).

Kopia zawiera dane osobowe klientów. **Nigdy** nie trafia do repozytorium, zgłoszeń, CI, artefaktów
GitHub, czatów ani poczty. Przykłady w tym dokumencie używają placeholderów (`<adres-api>`,
`<nazwa-kopii>`).

## 1. Cele (D-08)

| Cel | Wartość |
|---|---|
| Częstotliwość kopii | codziennie |
| Retencja | 30 dni |
| Miejsce | niezależne od hostingu, dostęp tylko właściciela |
| RPO (maksymalna utrata danych) | 24 h |
| RTO (maksymalny czas powrotu do działania) | 8 h |

To są **cele do potwierdzenia próbą** (sekcja 6), a nie gwarancja dostawcy hostingu.

## 2. Konfiguracja kopii na hostingu **[właściciel]**

1. W panelu SmarterASP.NET ustaw automatyczną kopię bazy Stone Flow.
2. Rozbieżność do sprawdzenia (SF-1, `docs/tasks/sf-1/01-weryfikacja.md`): według artykułu dostawcy
   (2021) standardowa kopia jest robiona co drugi dzień, a płatny CustomBackup ma retencję tygodnia.
   Jeśli panel nie zapewnia kopii codziennej z retencją 30 dni, właściciel wybiera rozwiązanie
   (np. codzienne pobieranie kopii do niezależnego miejsca z własną retencją 30 dni) i zapisuje
   decyzję w `odbior-mvp.md`.
3. Dowód do checklisty: częstotliwość, godzina, retencja i miejsce kopii (opis, bez zrzutów z danymi).

## 3. Niezależne miejsce kopii **[właściciel]**

- Kopię pobiera się z hostingu do miejsca wybranego przez właściciela (D-08 pkt 4 — otwarte),
  z dostępem ograniczonym do właściciela.
- Retencja w tym miejscu: 30 dni, starsze kopie usuwa się.
- Kopia **nie** jest robiona przez GitHub Actions ani inne CI — przeniosłoby to dane klientów do CI.

## 4. Kopia przed wdrożeniem z migracją

Workflow tego nie wymusza (panel nie ma potwierdzonego API do kopii), dlatego punkt jest na liście
kontrolnej PR.

**Lista kontrolna PR zawierającego nową migrację** (katalog `Migrations/` dowolnego modułu):

- [ ] Przed merge do `main` (merge uruchamia publikację) administrator wykonał kopię ręczną bazy
  produkcyjnej w panelu hostingu.
- [ ] Nazwa kopii (`<nazwa-kopii>`, bez danych) jest w opisie PR.
- [ ] Migracja jest zgodna z poprzednią wersją API (expand/contract, `wdrozenia.md` §6).

To samo dotyczy **pierwszej publikacji** na produkcję.

## 5. Sprawdzenie kopii skryptem

`deploy/scripts/verify-restore.sh` odtwarza kopię do **nowej** bazy roboczej
`StoneFlowRestoreCheck_<id>` (nigdy `WITH REPLACE`), sprawdza spójność (`DBCC CHECKDB`), wypisuje
ostatnią migrację każdego schematu, uruchamia bieżące migracje (`migrate.sh`) i liczy wiersze tabel
głównych. Na końcu usuwa bazę roboczą. Nie wypisuje connection stringu ani danych z tabel.

Wymagania: bash (na Windows Git Bash), .NET SDK z `global.json`, `dotnet tool restore`,
`dotnet build stone-flow.slnx -c Release`, `sqlcmd`. Plik w archiwum (`.zip`) najpierw rozpakuj —
skrypt przyjmuje tylko `.bak`.

**Wariant A — Docker** (`deploy/docker-compose.yml`, kontener `stone-flow-sqlserver`):

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
docker exec stone-flow-sqlserver mkdir -p /var/opt/mssql/backup
docker cp <ścieżka-lokalna>/<nazwa-kopii>.bak stone-flow-sqlserver:/var/opt/mssql/backup/stoneflow.bak
export STONEFLOW_TEST_SQLSERVER='Server=localhost,1433;User Id=sa;Password=<hasło-sa>;TrustServerCertificate=True'
export SQLCMD='docker exec -i stone-flow-sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P <hasło-sa>'
deploy/scripts/verify-restore.sh /var/opt/mssql/backup/stoneflow.bak
```

**Wariant B — Windows z LocalDB** (Git Bash):

```bash
export STONEFLOW_TEST_SQLSERVER='Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True'
export SQLCMD='sqlcmd -S (localdb)\MSSQLLocalDB -E'
deploy/scripts/verify-restore.sh 'C:\kopie\stoneflow.bak'
```

Wynik: kod 0 i wiersz `Kopia sprawdzona: odtworzona, spójna, zmigrowana.` Każdy błąd kończy się
komunikatem `::error::` z nazwą kroku. Liczby `Wiersze <tabela>: <n>` porównaj z aplikacją
(np. liczba zamówień na liście „Zamówienia” bez filtrów).

`KEEP_DB=1` zostawia bazę roboczą do ręcznego przeglądu — usuń ją potem ręcznie.

**Sprzątanie po próbie:** usuń lokalną kopię pliku (`docker exec stone-flow-sqlserver rm
/var/opt/mssql/backup/stoneflow.bak` i plik na dysku) oraz, przy `KEEP_DB=1`, bazę roboczą.

Test skryptu (`deploy/scripts/test-verify-restore.sh`) biegnie w CI w zadaniu `deploy-dry-run` na
fikcyjnych danych: kopia pełna, kopia częściowo zmigrowana, plik obcięty, brak pliku.

### Kopia z nowszej wersji SQL Server

Kopii z nowszej wersji serwera nie da się odtworzyć na starszym (`Msg 3169`). Skrypt kończy się
wtedy komunikatem o wersji. Sprawdź wersję SQL Server na hostingu (`SELECT @@VERSION` w panelu) i
uruchom próbę na zgodnym obrazie (zmień obraz w `deploy/docker-compose.yml` lokalnie, bez commitu).

### Kopia z migracjami nowszymi niż kod

Po powrocie kodu (`powrot-do-wersji.md`) kopia może mieć migracje, których bieżący kod nie zna.
`migrate.sh` niczego wtedy nie stosuje, a skrypt wypisuje ostatnią migrację każdego schematu. To stan
oczekiwany przy expand/contract — poprzednia wersja API działa na nowszym schemacie.

## 6. Próba odtworzenia na hostingu **[właściciel]** (kryterium SF-21)

Wykonuje się przed startem produkcyjnym.

1. Wybierz kopię i zapisz jej wiek (czas od wykonania kopii do rozpoczęcia próby) — porównanie z RPO.
2. Zapisz czas rozpoczęcia (decyzja o odtworzeniu).
3. Odtwórz kopię przez panel **do osobnej bazy**, jeśli plan na to pozwala. Jeśli nie — do bazy
   produkcyjnej, ale tylko przed startem (gdy nie ma w niej prawdziwych danych) i po świeżej kopii.
   Sprawdź dwa razy nazwę bazy docelowej.
4. Migracje: workflow (Actions → Deploy) albo wariant awaryjny z `wdrozenia.md` §7.
5. Kontrola: `GET https://<adres-api>/health` = 200, logowanie, odczyt Pulpitu, lista
   „Historia zmian”.
6. Zapisz czas zakończenia — RTO = koniec − początek (cel 8 h).
7. Wpisz do `odbior-mvp.md`: datę, osobę, wiek kopii, RTO, wynik i opis (bez zrzutów z danymi).

## 7. Odtworzenie po awarii

Decyzję o odtworzeniu podejmuje właściciel. Odtworzenie **cofa dane do chwili kopii**.

1. Decyzja właściciela i wybór kopii (najświeższa przed awarią).
2. Zatrzymanie API: wgraj `deploy/app_offline.htm` do katalogu witryny przez FTP.
3. Odtworzenie kopii w panelu (sekcja 6 pkt 3 — sprawdź nazwę bazy docelowej).
4. Migracje (kopia może być starsza niż kod): workflow albo wariant awaryjny z `wdrozenia.md` §7.
5. Usunięcie `app_offline.htm`.
6. `GET https://<adres-api>/health` = 200, logowanie, Pulpit i lista „Historia zmian”.
7. Skutki do przekazania użytkownikom:
   - dane zapisane po wykonaniu kopii przepadają;
   - zamówienia ze sklepu z tego okresu wróci dzienne uzupełnianie (SF-13) od ostatniego sukcesu
     zapisanego w odtworzonej bazie (`wdrozenia.md` §11);
   - wpisy ręczne (zamówienia, wpłaty, wydatki, wypłaty) trzeba wprowadzić ponownie;
   - „Historia zmian” też wraca do chwili kopii — odnotuj odtworzenie (data, kopia, powód) w
     `odbior-mvp.md` albo w dzienniku operacji właściciela, żeby brak wpisów był wyjaśniony.
