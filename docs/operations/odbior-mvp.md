# Odbiór MVP — checklista z dowodami

Checklista odbioru Stone Flow (SF-21): każde kryterium zgłoszenia SF-21, PRD §11 i design guide §10
z rodzajem dowodu i miejscem na wynik. Uzupełnia ją właściciel (lub osoba przez niego wskazana).

**Rodzaje dowodu:**

- `automatyczny` — test lub zadanie CI (nazwa testu lub zadania); link do zielonego przebiegu CI
  z PR SF-21 uzupełnia właściciel po merge;
- `hosting` — krok właściciela na hostingu produkcyjnym (procedury w `docs/operations/`);
- `ręczny` — scenariusz testu ręcznego UI (`docs/tasks/sf-21/06-implementation-done.md`, sekcja
  „Scenariusze testów ręcznych (UI)”) albo ocena właściciela.

**Zasady wpisów:** bez danych klientów, sekretów, nazwy handlowej i domeny sklepu; bez zrzutów
ekranu z danymi klientów — tylko opis i link (przebieg CI, wdrożenie Vercel, notatka właściciela).
Placeholder `<link do przebiegu CI — uzupełnia właściciel po merge>` oznacza wiersz automatyczny
czekający na link.

Kolumny: **Stan** (`otwarte` / `spełnione` / `niespełnione`), **Data**, **Kto**, **Wynik**, **Link**.

## 1. Kryteria zgłoszenia SF-21

| # | Kryterium | Rodzaj | Dowód | Stan | Data | Kto | Wynik | Link |
|---|---|---|---|---|---|---|---|---|
| Z1 | Odtworzenie kopii i migracja schematu zostały sprawdzone na hostingu. | automatyczny + hosting | CI `deploy-dry-run`: `test-migrate.sh`, `test-verify-restore.sh` (kopia pełna, częściowa, uszkodzona, brak pliku). Hosting: próba odtworzenia z pomiarem RTO/RPO (`kopie-i-odtworzenie.md` §6) i log kroku „Migracje” pierwszej publikacji | otwarte | | | | `<link do przebiegu CI — uzupełnia właściciel po merge>` |
| Z2 | Sprawdzono procedurę powrotu do działającej wersji. | hosting | Próba z `powrot-do-wersji.md` §5: linki do przebiegów workflow, identyfikatory wdrożeń Vercel, czas | otwarte | | | | |
| Z3 | Repozytorium i pakiet frontendu nie zawierają sekretów. | automatyczny | CI `secrets` (gitleaks, historia, reguła `woocommerce-rest-key` z kontrolą „Reguła kluczy WooCommerce wykrywa wzorzec”); CI `frontend`: gitleaks na `dist` i „Brak wzorców sekretów w pakiecie (skrypt)”; `check-bundle-secrets.test.mjs` | otwarte | | | | `<link do przebiegu CI — uzupełnia właściciel po merge>` |
| Z4 | Wszystkie scenariusze z sekcji 11 przechodzą, w tym zamówienie i wydatek otwarte w dniu uruchomienia. | automatyczny | CI `backend`: `Acceptance/MvpFinancialScenariosTests` (13 scenariuszy) | otwarte | | | | `<link do przebiegu CI — uzupełnia właściciel po merge>` |
| Z5 | Oba widoki uzgadniają się z rekordami źródłowymi, także karta „Koszty”. | automatyczny | CI `backend`: `MvpFinancialScenariosTests.Both_views_reconcile_with_source_records_for_month_year_and_range_across_launch` | otwarte | | | | `<link do przebiegu CI — uzupełnia właściciel po merge>` |
| Z6 | Kluczowe procesy mają testy E2E, a CI przechodzi. | automatyczny | CI `e2e` (Playwright: `session`, `orders`, `expenses`, `withdrawals`, `dashboard`, `change-history`, `spa-refresh`) oraz zielone `backend`, `frontend`, `secrets`, `deploy-dry-run` | otwarte | | | | `<link do przebiegu CI — uzupełnia właściciel po merge>` |
| Z7 | Spełnione są kontrast, obsługa klawiatury i prezentacja niekompletności danych. | automatyczny + ręczny | CI `e2e`: `a11y.e2e.ts`, `a11y-auth.e2e.ts` (axe WCAG 2.2 AA, reflow 375 px), `keyboard.e2e.ts`; scenariusze ręczne (stany hover/focus, niekompletna integracja, telefon) | otwarte | | | | `<link do przebiegu CI — uzupełnia właściciel po merge>` |

## 2. PRD §11 — warunki odbioru MVP

| # | Kryterium | Rodzaj | Dowód | Stan | Data | Kto | Wynik | Link |
|---|---|---|---|---|---|---|---|---|
| P1 | Codzienna obsługa uzgodnionego zakresu nie wymaga równoległego prowadzenia obu arkuszy. | ręczny | Ocena właściciela po okresie użytkowania (data rozpoczęcia, czas, wniosek) | otwarte | | | | |
| P2 | Zamówienie ręczne i z WooCommerce przechodzą pełny proces; ponowienia nie duplikują danych i nie nadpisują bez reguł pól ERP. | automatyczny | CI `e2e`: `orders.e2e.ts` (podwójne kliknięcie = jedno zamówienie, wpłata); CI `backend`: `WooCommerceWebhookTests`, `WooCommerceBackfillTests`, testy idempotencji zamówień | otwarte | | | | `<link do przebiegu CI — uzupełnia właściciel po merge>` |
| P3 | Jednorazowy i miesięczny wydatek są widoczne we właściwym raporcie; ponowienie zadania, restart oraz zakończenie cyklu działają poprawnie. | automatyczny | CI `e2e`: `expenses.e2e.ts`; CI `backend`: `MvpFinancialScenariosTests` (36.11, 36.12), `RecurringExpensesJobTests`, `RecurringExpensesApiTests` | otwarte | | | | `<link do przebiegu CI — uzupełnia właściciel po merge>` |
| P4 | Oba widoki finansowe mają zatwierdzone definicje i uzgadniają się z rekordami źródłowymi (scenariusze: niezapłacone zamówienie, wpłata w innym miesiącu, korekta, zwrot sprzedaży, zwrot nadpłaty, oddanie towaru bez zwrotu pieniędzy, korekta od dostawcy, anulowanie opłaconego zamówienia ze zwrotem, zamówienie i wydatek otwarte w dniu uruchomienia, wydatek planowany). | automatyczny | CI `backend`: `MvpFinancialScenariosTests` (36.1–36.13 i uzgodnienie 37); definicje — PRD §8 (D-01) | otwarte | | | | `<link do przebiegu CI — uzupełnia właściciel po merge>` |
| P5 | Zmianę raportu za miniony okres można wyjaśnić na liście „Historia zmian”. | automatyczny | CI `backend`: `MvpFinancialScenariosTests.Change_of_a_past_period_is_explained_by_change_history`; CI `e2e`: `change-history.e2e.ts` | otwarte | | | | `<link do przebiegu CI — uzupełnia właściciel po merge>` |
| P6 | API odrzuca nieuprawniony odczyt i zapis; wygaśnięcie sesji nie ujawnia danych. | automatyczny | CI `backend`: `EndpointSecurityTests`, `*ApiTests.Anonymous_request_is_rejected`, `LoginAndSessionTests`; CI `e2e`: `session.e2e.ts` | otwarte | | | | `<link do przebiegu CI — uzupełnia właściciel po merge>` |
| P7 | Kluczowe procesy mają testy E2E; reguły domenowe i finansowe testy jednostkowe, a zapis, autoryzacja i unikalność testy integracyjne. | automatyczny | CI `e2e`, `backend` (testy jednostkowe modułów i integracyjne), `frontend` (`npm test`) | otwarte | | | | `<link do przebiegu CI — uzupełnia właściciel po merge>` |
| P8a | CI przechodzi. | automatyczny | Zadania `backend`, `frontend`, `secrets`, `deploy-dry-run`, `e2e` zielone na PR SF-21 | otwarte | | | | `<link do przebiegu CI — uzupełnia właściciel po merge>` |
| P8b | Oba hostingi są zweryfikowane. | hosting | Pierwsza publikacja przez `deploy.yml` (kontrole `/health`, `/logowanie`, `/api/auth/csrf`), punkty `wdrozenia.md` §12 | otwarte | | | | |
| P8c | SPA działa po odświeżeniu podstrony. | automatyczny + hosting | CI `e2e`: `spa-refresh.e2e.ts`; hosting: odświeżenie podstrony na Vercel (`wdrozenia.md` §12) | otwarte | | | | |
| P8d | Migracja schematu bazy i odtworzenie kopii zostały sprawdzone. | automatyczny + hosting | Jak Z1 | otwarte | | | | |
| P9 | UI spełnia design guide, w tym kontrast, obsługę klawiatury i prezentację niekompletności danych. | automatyczny + ręczny | Jak Z7 i sekcja 3 | otwarte | | | | |

## 3. Design guide §10 — odbiór projektu UI

| # | Kryterium | Rodzaj | Dowód | Stan | Data | Kto | Wynik | Link |
|---|---|---|---|---|---|---|---|---|
| U1 | Obowiązkowe cztery kolory są zapisane centralnie i zgodne z wymaganiami. | automatyczny | CI `frontend`: `npm run lint` (`lint:colors` — brak lokalnych kodów kolorów); tokeny `--sf-*` w `app/src/styles.css` | otwarte | | | | `<link do przebiegu CI — uzupełnia właściciel po merge>` |
| U2 | Układ nawiązuje do referencji przez hierarchię, karty i wykresy, zachowując zakres Stone Flow. | ręczny | Ocena właściciela: Pulpit w obu widokach a referencja z `documents/` | otwarte | | | | |
| U3 | Widoki sprzedaży i przepływów wyraźnie różnią etykiety, podstawę oraz znaczenie kwot. | automatyczny | CI `e2e`: `dashboard.e2e.ts` (podpisy kart obu widoków); CI `frontend`: testy komponentów SF-20 (`cash-flow-view.spec.ts` — brak „zysk”/„saldo”) | otwarte | | | | `<link do przebiegu CI — uzupełnia właściciel po merge>` |
| U4 | Kontrast wszystkich rzeczywiście użytych par spełnia wymagania; stany nie zależą wyłącznie od koloru. | automatyczny + ręczny | CI `e2e`: axe (`color-contrast`) na wszystkich stronach; tabela kontrastów tokenów w design guide §2; scenariusz ręczny stanów hover/focus/selected i błędów | otwarte | | | | |
| U5 | Na telefonie i tablecie można odczytać kwoty, filtrować dane oraz zapisać formularz. | automatyczny + ręczny | CI `e2e`: reflow 375 px na wszystkich stronach; scenariusz ręczny zapisu zamówienia i wydatku na telefonie i tablecie | otwarte | | | | |
| U6 | Każdy widok obsługuje ładowanie, pustkę, błąd, wygaśnięcie sesji i niekompletność danych, jeśli mogą wystąpić. | automatyczny + ręczny | CI `frontend`: testy komponentów (SF-14, SF-19, SF-20); CI `e2e`: `session.e2e.ts` (wygaśnięcie sesji); scenariusz ręczny niekompletnej integracji na Pulpicie | otwarte | | | | |
| U7 | Podwójne kliknięcie zapisu nie tworzy dwóch rekordów; walidacja i błędy nie usuwają wpisanych danych. | automatyczny | CI `e2e`: `orders.e2e.ts` (podwójne kliknięcie), `keyboard.e2e.ts` (błąd walidacji zostawia opis) | otwarte | | | | `<link do przebiegu CI — uzupełnia właściciel po merge>` |
| U8 | Klawiatura, fokus, dialogi, etykiety i tekstowy odczyt wykresów są sprawdzone. | automatyczny + ręczny | CI `e2e`: `keyboard.e2e.ts`, axe (etykiety); CI `frontend`: testy komponentów wykresów (SF-19, SF-20); scenariusz ręczny czytnika ekranu | otwarte | | | | |
| U9 | Przykłady i zrzuty nie ujawniają danych klientów, sekretów, nazwy handlowej ani domeny sklepu. | ręczny | Przegląd dokumentów zadania (`05-code-review.md`), dane E2E fikcyjne (`owner@example.com`, `Klient e2e-…`) | otwarte | | | | |

## 4. Pozycje właściciela (hosting i decyzje)

| # | Pozycja | Procedura | Stan | Data | Kto | Wynik | Link |
|---|---|---|---|---|---|---|---|
| W1 | Konfiguracja kopii w panelu: częstotliwość, godzina, retencja, miejsce (rozbieżność SF-1 rozstrzygnięta) | `kopie-i-odtworzenie.md` §2–3 | otwarte | | | | |
| W2 | Próba odtworzenia z pomiarem RTO (cel 8 h) i wieku kopii (RPO 24 h) | `kopie-i-odtworzenie.md` §6 | otwarte | | | | |
| W3 | Migracja na hostingu — log kroku „Migracje” pierwszej publikacji | `wdrozenia.md` §2 | otwarte | | | | |
| W4 | Próba powrotu do działającej wersji (revert + Promote w Vercel) | `powrot-do-wersji.md` §5 | otwarte | | | | |
| W5 | Ustawienie prawdziwej daty uruchomienia i kontrola (`launchDate`, „Raport liczony od …”) | `data-uruchomienia.md` §1–3 | otwarte | | | | |
| W6 | Weryfikacja obu hostingów i odświeżenia podstrony SPA na Vercel | `wdrozenia.md` §12 | otwarte | | | | |
| W7 | Zamknięcie D-05 (dostawca e-mail) w PRD | PRD §12 | otwarte | | | | |
| W8 | Zamknięcie D-08 (konto, harmonogram, próba odtworzenia) w PRD | PRD §12, `docs/tasks/sf-1/01-weryfikacja.md` | otwarte | | | | |
| W9 | Ocena „codzienna obsługa nie wymaga równoległego prowadzenia arkuszy” | jak P1 | otwarte | | | | |
| W10 | Ochrona gałęzi `main`: `e2e` jako wymagana kontrola PR | `wdrozenia.md` §3 | otwarte | | | | |
| W11 | Odtworzenia kopii po starcie (data, kopia, powód) — dziennik | `kopie-i-odtworzenie.md` §7 | — | | | | |
