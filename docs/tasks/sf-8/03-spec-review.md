# Spec review — SF-8

Druga runda przeglądu, po poprawkach autora. Pierwsza runda jest w `03-spec-review-v1.md`.

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Oba problemy krytyczne z v1 są rozwiązane. Kwota ma `decimal(18,2)` zgodnie
  z PRD §8, a edycja zamówienia anulowanego ma spójną regułę z kryterium akceptacji. Uwagi
  N1–N10 i niejasności z v1 też zostały zamknięte. Zostały cztery drobne uwagi i jedna nowa
  niespójność: kolejność sprawdzeń w `PUT` waliduje pola zależne od źródła, zanim zamówienie
  zostanie wczytane. Można je rozstrzygnąć w planie. Żaden niezmiennik nie jest naruszony.

## Findings

### Critical Issues

None.

### Non-Critical Issues

**N1 (nowa). Walidacja zależna od źródła stoi przed wczytaniem zamówienia.** „Szczegóły
i edycja”, pkt 3, każe najpierw walidować (`400`), a potem sprawdzić istnienie (`404`). Krok 1
mówi jednak, że pola sklepu w zamówieniu WooCommerce nie są walidowane. Pkt 6 dodaje, że
w zamówieniu ręcznym te same pola są wymagane. Obie reguły zależą od `Source`, a ten jest znany
dopiero po wczytaniu zamówienia. *Poprawka:* rozdzielić krok 1:
1. walidacja niezależna od źródła (format, długości, słowniki);
2. wczytanie (`404`);
3. walidacja zależna od źródła: wymagalność pól w zamówieniu ręcznym (`400`) albo porównanie
   pól sklepu (`422 field_owned_by_shop`);
4. `version`;
5. reguły statusu.

Kolejność `422 field_owned_by_shop` względem `409 order_conflict` też trzeba podać wprost.

**N2 (nowa). Niespójny wynik duplikatu `Number`.** Storage mówi, że naruszenie indeksu `Number`
„kończy się `503`”. Behavior, pkt 6, mówi, że port zamienia `2601` na `DuplicateRequest` bez
wyjątku, a gdy po wycofaniu nie ma zamówienia z tym kluczem, handler „traktuje sytuację jak
błąd programu”. Rzucony wtedy nowy wyjątek bez `DbException` w łańcuchu da `500`, nie `503`.
*Poprawka:* zapisać, że port przekazuje oryginalny `SqlException` w wyniku (albo jako
`InnerException`), a handler rzuca go ponownie. Wtedy zgodnie ze Storage wynik to `503`.
Druga możliwość: ujednolicić Storage na `500`.

**N3. Domyślny status a skrót idempotencji.** `fulfillmentStatus` jest w `POST` opcjonalny
(domyślnie `inProgress`). Reguły normalizacji w pkt 4 nie mówią, czy wartość domyślną wstawia
się przed liczeniem skrótu. Jeśli nie, żądanie bez pola i ponowienie z `inProgress` dadzą
fałszywe `409 idempotency_key_reused`. *Poprawka:* dopisać do normalizacji „wartości domyślne
są uzupełniane przed skrótem”, a do testu skrótu dodać ten wariant.

**N4. Ścieżka `2627` licznika jest praktycznie nieosiągalna, a Edge Cases opisują ją jako
zwykły przebieg.** `UPDATE … WITH (UPDLOCK, HOLDLOCK)` na nieistniejącym wierszu zakłada blokadę
zakresu klucza. Drugie żądanie czeka więc do zatwierdzenia pierwszego i znajduje już wiersz
licznika. Ponowienie po `2627` jest poprawnym zabezpieczeniem, ale opis „drugie po `2627`
powtarza `UPDATE`” sugeruje test, którego nie da się deterministycznie wywołać. *Poprawka:*
w Edge Cases opisać oczekiwany wynik (różne kolejne numery, w tym `0001` i `0002` w nowym roku)
bez wskazywania ścieżki. Ponowienie po `2627` pokryć testem portu z wymuszonym wstawieniem
wiersza albo zostawić jako kod obronny.

**N5. Kryterium akceptacji pól sklepu pomija kanał.** Scope i Behavior pkt 6 włączają `channel`
do pól tylko do odczytu w zamówieniu WooCommerce. AC „W zamówieniu ze sklepu zmiana wartości,
daty lub danych klienta daje `422 field_owned_by_shop`” kanału nie wymienia. *Poprawka:* dopisać
kanał do AC i do testu integracyjnego na zasianym zamówieniu WooCommerce.

### Unclear or Ambiguous Sections

- **Assumptions → Numer / Edge Cases:** nadal brak zachowania po 9999 zamówieniach w roku
  (`NNNN` rośnie do 5 cyfr?). Przy 20–30 zamówieniach miesięcznie (D-08) jest to tylko
  formalność, ale formatowanie numeru warto zapisać, np. „co najmniej 4 cyfry”.
- **UI → Stany:** „pusty stan z filtrami … przycisk leży w filtrach”. `app-data-table` sama
  pokazuje „Wyczyść filtry” w pustym stanie z filtrami i w pasku aktywnych filtrów. Warto
  zapisać, że strona nie dodaje własnego przycisku, żeby nie powstał duplikat.

### Invented or Unsupported Requirements

None.

## Rozstrzygnięcie uwag z v1

| Uwaga v1 | Status | Gdzie |
|---|---|---|
| C1. `decimal(12,2)` sprzeczne z PRD | Rozwiązana | Storage: `Value decimal(18,2)`; Walidacja: zakres `decimal(18,2)`; Invariant impact 2 |
| C2. Edycja zamówienia anulowanego niewykonalna | Rozwiązana | Behavior „Szczegóły i edycja” pkt 5 i 8; AC „Zamówienie anulowane można edytować”; UI pokazuje status tylko do odczytu; `cancelled` w `POST` → `400` |
| N1. `Suppliers nvarchar(1000)` za krótka | Rozwiązana | `nvarchar(4000)` / `nvarchar(2000)`, limity w walidacji, enkoder jak w SF-7 |
| N2. Ścieżka duplikatu `CreationRequestId` | Rozwiązana | Behavior pkt 5–7: `commitWhen: result is Created`, `DuplicateRequest`, odczyt po wycofaniu, porównanie skrótu. Pozostała drobna niespójność statusu (N2 nowa) |
| N3. Porównanie `version` | Rozwiązana | Behavior pkt 4: `OriginalValue` = wersja klienta; AC z dwiema sesjami |
| N4. Normalizacja skrótu | Rozwiązana | Behavior pkt 4; brakuje tylko wartości domyślnej (N3) |
| N5. UI po `409 idempotency_key_reused` | Rozwiązana | Behavior pkt 8: `orderId`/`orderNumber`, link, nowy klucz tylko przez „Dodaj kolejne zamówienie” |
| N6. `PUT` WooCommerce i pola sklepu; kanał | Rozwiązana | Behavior pkt 6; kanał tylko do odczytu. Nowa niespójność kolejności (N1 nowa), AC bez kanału (N5) |
| N7. Pozostałe błędy bazy → `503` | Rozwiązana | Storage (numeracja `UPDATE … OUTPUT`, ponowienie po `2627`, `503` dla `Number` i check constraints); Assumptions |
| N8. Format audytu dla wartości logicznych i list | Rozwiązana | Audyt: `true`/`false`, tablica JSON, reguły `Created` i `Updated` |
| N9. Komendy Verification | Rozwiązana | `stone-flow.slnx`, `--solution`, `typecheck`, `format:check`, projekt dodany do solucji |
| N10. Pusty stan, podsumowanie błędów, wariant przycisku, klucze błędów | Rozwiązana | UI → Stany i Akcje; Types → klucze błędów walidacji |
| Kolejność sprawdzeń | Rozwiązana | Behavior „Szczegóły i edycja” pkt 3; „Anulowanie” pkt 4 (z zastrzeżeniem N1 nowej) |
| Luki w numeracji | Rozwiązana | Storage; Edge Cases; AC „Wycofany zapis nie zużywa numeru” |
| Brak `Idempotency-Key`, `version` przy powtórzeniu | Rozwiązana | `400 idempotency_key_required`; `200` z aktualną `version` |
| Parametry listy i `total` | Rozwiązana | Lista pkt 3 |
| Kody `customer.kind`, `fulfillmentStatus` w `POST` | Rozwiązana | Types crossing a boundary |
| Warunki `DELETE` → `405` | Rozwiązana | Anulowanie i przywrócenie pkt 5 |
| Nazwa migracji | Rozwiązana | `OrdersAndNumbering` |
| Rok numeru, przyszłe daty jako założenia | Rozwiązana | Assumptions |
| Numer po 9999 w roku | Otwarta (drobna) | Unclear or Ambiguous Sections |

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Data / API → Storage, Walidacja | PRD §8 „Reguły kwot” | Brak konfliktu: `decimal(18,2)` |
| Data / API (tłumaczenia błędów), Assumptions | `conventions.md` „Errors and validation” (rozbieżność `DbException` → `503`) | Brak konfliktu: rozbieżność świadomie zachowana i opisana; drobna niespójność `500`/`503` (N2) |
| Lista → Kolumny bez płatności | Design guide 6 „Tabele” | Brak konfliktu: SF-9, decyzja użytkownika z 05.11.2026 |
| Pole statusu bez „Anulowane” | Design guide 6 „Formularze” | Brak konfliktu: decyzja użytkownika, zgodna z PRD |
| Korekta wartości bez limitu obniżeń | PRD §5 „Limit obniżeń sprzedaży” | Brak konfliktu: SF-10 według breakdownu |
| UI → pusty stan bez filtrów | `shared/ui/data-table.html` | Brak konfliktu: akcja na stronie, komponent bez zmian |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | OK | Brak wpłat i pola „opłacone”; test braku rekordu pieniężnego |
| 2. Kwoty decimal na backendzie | OK | `decimal(18,2)` w domenie i bazie, string w JSON, konwersja tekstowa w UI |
| 3. Dostęp egzekwowany przez API | OK | Sesja na każdym endpoincie, XSRF globalnie, testy `401` |
| 4. Ponowienia nie duplikują | OK | Unikalny `CreationRequestId`, skrót znormalizowanej komendy, UI bez automatycznego nowego klucza; `(Source, ExternalId)` pod integrację |
| 5. Audyt finansowy | OK | `IAuditLog` w tym samym `IUnitOfWork`, autor z sesji, pełny format wartości, `IsPersonalData`, brak usuwania |
| 6. Sekrety po stronie serwera | OK | Brak nowych sekretów; logi tylko z `id` i numerem; `409` zwraca numer, nie dane klienta |
| 7. Granice modułów i warstw | OK | Schemat `orders`, port `IOrderNumberAllocator`, audyt przez `Shared.Application`, DTO z portów |
| 8. Brak danych identyfikujących w dokumentach | OK | Fikcyjni klienci, przykładowi dostawcy („Dostawca A”), bez nazwy sklepu |

## Risk

- Recorded: high
- Assessed: high. Wartość zamówienia jest podstawą sprzedaży, zadanie obejmuje dane osobowe
  klienta, idempotencję i kontrolę współbieżności. Przed merge wymagany jest przegląd człowieka.

## Assumptions Detected

| Założenie | Czy spec je podaje |
|---|---|
| Odpowiedzi użytkownika z 05.11.2026 (wartość ≥ 0, data, „Anulowane” przez akcje, słowniki, płatności w SF-9, kanał „sklep”, numer, dane klienta) | Tak |
| Granica z SF-10 i zakres PLAT-06 | Tak |
| Edycja anulowanego zamówienia z `fulfillmentStatus: cancelled` | Tak (Behavior pkt 5, AC) |
| Klucz idempotencji tylko dla tworzenia, bezterminowo | Tak |
| Skrót SHA-256 znormalizowanej komendy | Tak; bez uwzględnienia wartości domyślnej (N3) |
| Rok numeru z czasu utworzenia (Europe/Warsaw) | Tak |
| Przyszła data i data sprzed uruchomienia dozwolone | Tak |
| Kanał zamówienia ze sklepu tylko do odczytu | Tak |
| `409 idempotency_key_reused` z `orderId`, `orderNumber` | Tak |
| Rozbieżność `DbException → 503` bez zmian | Tak |
| Dostawcy i ZO jako JSON; wyszukiwanie po numerze i nazwie; domyślna lista z anulowanymi; autor z sesji; `COUNT` bez kursora | Tak |
| Wyszukiwanie bez rozróżniania wielkości liter według collation bazy | Tak (Lista pkt 3); zależy od collation bazy produkcyjnej, której spec nie sprawdza |
| Format numeru powyżej 9999 | Nie: drobne |

## Recommendation

- Proceed as-is. Do `sdd-plan` można przejść. Uwagi N1–N5 rozstrzyga plan, najlepiej jako
  jawne decyzje w jego pierwszych krokach (kolejność sprawdzeń w `PUT`, wynik duplikatu
  `Number`, wartości domyślne w skrócie). Najlepiej jednak, żeby autor dopisał je do spec.
