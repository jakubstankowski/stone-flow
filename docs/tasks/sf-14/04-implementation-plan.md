# Plan implementacji — SF-14

Tryb: plan jednowarstwowy (frontend `app/`; backend bez zmian, spec v4). Bez podziału na części:
zmiana jest spójna i mieści się w jednym przeglądzie.

## 1. Scope

Plan dostarcza we frontendzie Angular (`app/src/app/`) wszystkie elementy specyfikacji v4:

- czystą funkcję stanu INT-01 i prezentację stanu;
- klienta API SF-13 z limitami czasu;
- serwis stanu z regułą aktywności i regułą wieku odczytu;
- wskaźnik w nagłówku powłoki;
- ekran „Integracja” (stan, ostatni przebieg, „Synchronizuj teraz”, lista błędów z „Ponów”);
- komponent `app-incomplete-data-notice` (bez osadzania);
- drobne rozszerzenia `app-status-badge`, `app-button` i `shared/format`;
- wpis w `conventions.md`;
- testy Vitest.

Backend, baza i API się nie zmieniają.

### Rozstrzygnięcia uwag przeglądu `03-spec-review.md` (PASS WITH ISSUES)

Decyzje koordynatora, wiążące dla implementacji:

- **NC1 — wariant `aged` w odznace i na ekranie.**
  - Odznaka traci ton `success` i ikonę „✓”: ton `neutral`, ikona zegara, a obok tekst „Stan z …”.
  - Sekcja stanu na ekranie „Integracja” pokazuje ten sam dopisek i zdanie „Stan może być
    nieaktualny — kliknij lub naciśnij klawisz, aby odświeżyć.”
- **NC2 — timer wieku odczytu.** Zamiast tyknięć co 5 s serwis ustawia timer na dokładną chwilę
  przekroczenia progu: `readAt + 2 × okres` (+1 ms, bo warunek „przekracza” jest ścisły). Zegar co
  60 s zostaje tylko dla progu 25 h.
- **NC3 — pozostałości w `02-spec.md`** poprawiono redakcyjnie (notka na górze specyfikacji).
- **NC4 — limit czasu.** `timeout` przerywa tylko oczekiwanie klienta (odsubskrybowanie anuluje
  żądanie `HttpClient`). Serwer może dokończyć operację, a duplikatów pilnuje backend (inwariant
  4). Po zdjęciu `busy` lista jest przeładowywana, bo wynik po stronie serwera jest nieznany. Przy
  „Synchronizuj teraz” stan ustala wymuszony odczyt.
- **NC5 — format dopisku.** „Stan z HH:mm”, a gdy odczyt nie jest z bieżącego dnia (Europe/Warsaw)
  — „Stan z dd.MM.yyyy, HH:mm” (`formatDateTime`).
- **Niejasności:**
  - w obsłudze zdarzenia najpierw `lastActivityAt = now`, potem sprawdzenie warunków;
  - ogłoszenie wejścia w `aged` po każdej przerwie jest zamierzone;
  - przejście `aged` → `unknown` zastępuje dopisek wariantem `unknown` („(ostatni znany
    odczyt)”).

## 2. Affected areas

| Layer | Module / feature | Why |
|---|---|---|
| Frontend | `features/integration/` | stan INT-01, klient API, serwisy, ekran, odznaka, komunikat |
| Frontend | `layout/` | wskaźnik w nagłówku powłoki (design guide sekcja 3) |
| Frontend | `shared/ui/` | slot ikony w `app-status-badge`, wejście `describedBy` w `app-button` |
| Frontend | `shared/format/` | `formatTime` i `formatReadTime` (dopisek „Stan z …”) |
| Frontend | testy istniejące (`app.spec`, `app.routes.spec`, `app-shell.spec`, `page-title-strategy.spec`) | powłoka wysyła teraz `GET /status` |
| Docs | `.sdd/context/conventions.md` | reguła żądań w tle (spec, Assumptions 27) |

## 3. Files

| File path | Action | Purpose |
|---|---|---|
| `app/src/app/features/integration/integration-models.ts` | create | DTO SF-13 i wyniki klienta |
| `app/src/app/features/integration/integration-api.ts` | create | `status`, `errors`, `requestSync`, `retry`; limity `timeout` |
| `app/src/app/features/integration/integration-state.ts` | create | `integrationStateOf`, etykiety, tony, ikony, teksty powodów, słowniki, `presentationOf` |
| `app/src/app/features/integration/integration-messages.ts` | create | komunikaty akcji: `syncMessage`, `retryMessage` |
| `app/src/app/features/integration/integration-status.ts` | create | serwis stanu (root): odczyty, znaczniki, timery, ogłoszenia |
| `app/src/app/features/integration/integration-errors.ts` | create | stan listy błędów i „Ponów” (provider strony) |
| `app/src/app/features/integration/integration-state-badge.ts` | create | `app-integration-state-badge` (ikona, etykieta, dopisek) |
| `app/src/app/features/integration/incomplete-data-notice.ts` | create | `app-incomplete-data-notice` (pięć wariantów) |
| `app/src/app/features/integration/integration-page.ts`, `.html` | modify | ekran zamiast placeholdera |
| `app/src/app/layout/integration-indicator.ts`, `.html` | create | wskaźnik w nagłówku, `start()`/`stop()`, `aria-live` |
| `app/src/app/layout/app-shell.ts`, `.html` | modify | osadzenie wskaźnika |
| `app/src/app/shared/ui/status-badge.ts` | modify | `<ng-content />` przed etykietą |
| `app/src/app/shared/ui/button.ts`, `.html` | modify | wejście `describedBy` → `aria-describedby` |
| `app/src/app/shared/format/pl-format.ts` | modify | `formatTime`, `formatReadTime` |
| `app/src/testing/integration-testing.ts` | create | fikcyjne dane i `discardIntegrationStatus()` |
| `*.spec.ts` dla każdego nowego pliku i zmian | create/modify | testy (sekcja 8) |
| `.sdd/context/conventions.md` | modify | wpis o żądaniach w tle |

## 4. Implementation steps

1. `shared/format/pl-format.ts`:
   - `formatTime(iso)` → `HH:mm` w Europe/Warsaw;
   - `formatReadTime(iso, nowMs)` → `HH:mm` dla tego samego dnia warszawskiego, w innym
     przypadku `formatDateTime`.

   Test w `pl-format.spec.ts`.
2. `shared/ui/status-badge.ts` — slot `<ng-content />`. `shared/ui/button.*` — `describedBy`.
   Testy.
3. `integration-models.ts` — typy (spec, sekcja „Types crossing a boundary”).
4. `integration-api.ts`:
   - Promise przez `firstValueFrom`, każdy z operatorem `timeout`: 15 s dla `status`, `errors` i
     `requestSync`, 45 s dla `retry`;
   - mapowanie `202`/`409` oraz `200`/`404`/`409`/`502`/`503`;
   - kody `5xx` czytane z `HttpErrorResponse`;
   - `TimeoutError` → `timeout`.
5. `integration-state.ts`:
   - `integrationStateOf` (kolejność kroków, powody, `dataComplete`, próg 25 h);
   - `presentationOf(view, nowMs)` → etykieta, ton, ikona, dopisek, nazwa dostępna.
6. `integration-messages.ts` — tabele komunikatów z Behavior 5 (tekst, `alert` albo `status`,
   `reload`, `refresh`).
7. `integration-status.ts`:
   - sygnały `phase`, `status`, `readAt`, `aged`, `now` i `announcement`;
   - `refresh({ force })` z numerem sekwencyjnym;
   - znaczniki `lastActivityAt` i `lastReadAt`;
   - nasłuch `keydown`/`pointerdown` (capture, passive) i `visibilitychange`;
   - zegar 60 s i timer `aged`;
   - `start()` i `stop()` (reset).
8. `integration-errors.ts` — filtr, strona, numer sekwencyjny listy, `retry` z `busy` na wiersz.
9. `integration-state-badge.ts`, `incomplete-data-notice.ts`.
10. `layout/integration-indicator.*`, osadzenie w `app-shell.html`.
11. `integration-page.*`.
12. `testing/integration-testing.ts`. Dostosowanie istniejących testów powłoki i tras:
    `discardIntegrationStatus()` przed `verify()`.
13. Wpis w `conventions.md` (dosłownie pkt 27 specyfikacji).
14. Weryfikacja (sekcja 9).

## 5. Data & contracts

- Storage: None.
- API (bez zmian, SF-13):
  - `GET /api/integrations/woocommerce/status` → `SyncStatus`;
  - `GET …/errors?status&page&pageSize` → `OrderErrorPage`;
  - `POST …/errors/{id}/retry` → `OrderError` albo Problem;
  - `POST …/sync` → `202` albo Problem.
- Typy: `integration-models.ts`, jak w specyfikacji.

## 6. Edge cases & null safety

| Edge case (spec) | Gdzie |
|---|---|
| kolejność stanów, `abandoned`, nieznany status, 25 h ścisłe, przyszła/nieczytelna data | `integrationStateOf` |
| `dataComplete` w `running` | `integrationStateOf` |
| `unknown` z ostatnią odpowiedzią i dopiskiem „(ostatni znany odczyt)” | `IntegrationStatus` (`status` nie jest czyszczony), `presentationOf`, komunikat |
| `aged` po 120 s / 20 s, bez żądań, pierwsza aktywność odświeża | timer `aged` w `IntegrationStatus` |
| bezczynna / ukryta karta → brak żądań | wysyłka tylko w obsłudze zdarzenia, warunek widoczności |
| `pointerdown` → odczyt n, akcja → odczyt n+1, spóźniona odpowiedź n ignorowana | numer sekwencyjny |
| nieudany odczyt nie kończy serii `running` | flaga `runningSeries` zmieniana tylko po sukcesie |
| limit 15 s / 45 s | `timeout` w `integration-api.ts`; komunikaty w `integration-messages.ts` |
| `csrf_invalid` bez odświeżenia | `integration-messages.ts` |
| pusta strona po przeładowaniu przy `page > 1` → `page − 1` | `IntegrationErrors.load()` |
| `orderId = null` | szablon kolumny „Zamówienie” |
| koniec synchronizacji → przeładowanie listy | `effect` strony na przejściu `isRunning` z `true` na `false` |
| `401` | globalny interceptor; serwis nie zmienia stanu |

Null safety: `lastRun`, `lastSuccessAtUtc`, `finishedAtUtc`, `orderId`, `orderNumber`, `closedAtUtc`
i `message` są nullowalne w typach i obsłużone w szablonach („Brak danych” albo pominięcie).

## 7. Invariants

| Invariant | Where it is enforced | Test that proves it |
|---|---|---|
| 3. API-enforced access | sesja i CSRF w API; odczyty w tle tylko przy aktywności i widocznej karcie | `integration-status.spec.ts`: brak żądań bez aktywności, przy ukrytej karcie i przy aktywności przed upływem okresu |
| 4. Retries never duplicate | `busy` na wiersz i dla „Synchronizuj teraz”; backend SF-13 | `integration-errors.spec.ts`: drugie kliknięcie w toku nie wysyła żądania; `integration-page.spec.ts` dla synchronizacji |
| 6. Secrets stay server-side | tylko pola z API, brak konfiguracji w bundlu | przegląd kodu (brak sekretów) |
| 7. Module and layer boundaries | logika w `integration-state.ts` i serwisach, nie w komponentach; tylko API Integrations | `integration-state.spec.ts` |
| 8. No identifying data in docs | fikcyjne dane w `testing/integration-testing.ts` | przegląd |

Invarianty 1, 2 i 5 nie są dotknięte.

## 8. Tests

| Test | Type | What it proves |
|---|---|---|
| `shared/format/pl-format.spec.ts` (rozszerzony) | unit | `formatTime`, `formatReadTime` (ten sam / inny dzień), brak danych |
| `shared/ui/states.spec.ts` / `status-badge` (rozszerzony) | unit | slot ikony nie zmienia etykiety |
| `shared/ui/button.spec.ts` (rozszerzony) | unit | `describedBy` → `aria-describedby`; brak → brak atrybutu |
| `features/integration/integration-state.spec.ts` | unit | kolejność, powody, granica 25 h, `dataComplete`, `presentationOf` (`aged` neutralny z zegarem, `unknown`) |
| `features/integration/integration-api.spec.ts` | unit (`HttpTestingController`) | adresy, parametry, mapowanie kodów, limity czasu (15 s / 45 s) |
| `features/integration/integration-status.spec.ts` | unit (sztuczne timery, fałszywe API) | pierwszy odczyt, reguła aktywności i znaczniki, ukryta karta, `force` i numer sekwencyjny, `unknown`, `aged` po 120 s i 20 s, seria `running`, ogłoszenia, `stop()` |
| `features/integration/integration-messages.spec.ts` | unit | komunikat i dalsze kroki dla każdego wyniku |
| `features/integration/integration-errors.spec.ts` | unit | filtr → strona 1, aktywny filtr, cofnięcie pustej strony, `busy` na wiersz, przeładowanie i odświeżenie, spóźnione przeładowanie |
| `features/integration/incomplete-data-notice.spec.ts` | component | pięć wariantów, teksty, czas, link |
| `features/integration/integration-page.spec.ts` | component | stany sekcji, format daty, ostatni przebieg, wyłączenie z powodem, wyniki akcji, link do zamówienia, nazwy „Ponów”, przeładowanie po końcu synchronizacji |
| `layout/integration-indicator.spec.ts` | component | tekst i ikona, link, nazwa dostępna bez `title`, `aria-live` |
| `layout/app-shell.spec.ts` i testy tras (dostosowane) | component | powłoka zawiera wskaźnik i nadal przechodzi |

## 9. Verification

Z `app/`, po kolei:

```
npm run lint
npm test -- --watch=false
npm run build
npm run typecheck
npx prettier --check src
```

Ręcznie (z API SF-13) według listy z `02-spec.md` → Verification → manual.

## 10. Risks

- Testy z globalnymi sztucznymi timerami mogą kolidować z `vi.waitFor` z `nextRequest`. Dlatego
  `integration-status.spec.ts` używa fałszywego `IntegrationApi`, a testy komponentów nie
  przyspieszają czasu.
- Powłoka wysyła `GET /status` w każdym teście, który ją renderuje. Testy muszą odrzucać to
  żądanie przed `verify()`.
- SF-13 jest w przeglądzie. Zmiana kontraktu wymaga zmian w `integration-models.ts` i
  `integration-api.ts`.
