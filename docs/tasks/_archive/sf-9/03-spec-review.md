# Spec review — SF-9

## Summary

- Overall assessment: PASS WITH ISSUES
- Justification: Specyfikacja pokrywa wszystkie kryteria zgłoszenia i reguły PRD dla stanu
  płatności: 0 zł, nadpłatę, anulowanie bez stanu, „Do zwrotu”, zwrot ≤ Zapłacono i zakaz
  ujemnego Zapłacono. Granica z SF-10, SF-19 i SF-20 jest wyznaczona poprawnie. Projekt
  serializacji przez `UPDLOCK` da się zrealizować na istniejącym `UnitOfWork` z SF-7, bo
  `ModuleTransactionInterceptor.CommandInitialized` dołącza każde polecenie do transakcji.
  Żaden niezmiennik nie jest naruszony. Do poprawienia zostają: klucz idempotencji
  niepowiązany z zamówieniem, nieuzasadnione i niedoprecyzowane przekazywanie `version`
  w zapytaniu `DELETE`, test SF-8, który przestanie przechodzić, oraz kilka luk w UI
  i kodach błędów.

## Findings

### Critical Issues

None.

### Non-Critical Issues

- **N1 (średnia): klucz idempotencji nie jest związany z zamówieniem.** Sekcja „Dodanie
  wpłaty”, punkt 5, mówi, że normalizacja i skrót „działają jak w SF-8”. W SF-8 skrót
  obejmuje tylko treść żądania, a `CreationRequestId` jest unikalny globalnie. Gdy klient
  wyśle ten sam klucz i tę samą treść do innego `orderId`, handler znajdzie wpłatę innego
  zamówienia i zwróci `200` z jej `id`. Zamówienie docelowe zostanie bez wpłaty, a UI
  pokaże sukces. Poprawka: dodać `orderId` do skrótu albo traktować dopasowanie z innym
  `OrderId` jako `409 idempotency_key_reused`. Dopisać do kolejności kroków, że przy
  zwrocie klucz sprawdza się przed limitem: powtórzenie po zapisanym zwrocie daje `200`,
  a nie `422`. Dopisać testy: ten sam klucz do dwóch zamówień oraz powtórzenie zwrotu,
  który wyczerpał Zapłacono.
- **N2 (średnia): `version` w zapytaniu `DELETE`.** Założenie mówi, że klient frontendu
  „nie wysyła ciała w `DELETE` w ustalonym wzorcu”. W kodzie nie ma żadnego wywołania ani
  endpointu `DELETE`, więc takiego wzorca nie ma. Angular `HttpClient.delete` przyjmuje też
  opcję `body`. Samo przekazywanie w zapytaniu jest wykonalne: `CsrfValidationMiddleware`
  sprawdza `DELETE`, a `EndpointSecurityTests` obejmie nowe trasy automatycznie. Wersja to
  jednak base64 z `rowversion`, więc może zawierać `+`, `/` i `=`, a nieprzekodowany `+`
  ASP.NET odczyta jako spację. Poprawka: poprawić uzasadnienie w Assumptions. Określić
  kodowanie: `HttpParams` albo `encodeURIComponent` po stronie frontendu, ewentualnie
  base64url. Określić odpowiedź na niepoprawny format (`400` pod kluczem `version`).
  Dodać test z wersją zawierającą `+` albo `/`. Alternatywą jest ciało `{ version }` jak
  w `cancel` i `restore`.
- **N3 (średnia): istniejący test SF-8 przestanie przechodzić.**
  `OrdersApiTests.Create_does_not_create_money_records_or_payment_state` sprawdza, że
  szczegóły i pozycja listy nie mają właściwości zawierających `paid` ani `payment`.
  Rozszerzone `GET` z SF-9 złamie ten test. Specyfikacja go nie wymienia. Poprawka:
  w Verification albo w Invariant impact (niezmiennik 1) napisać, czym test zastąpić. Na
  przykład: utworzenie zamówienia nie dodaje wierszy w `orders.Payments` ani
  `orders.Refunds`, a stan to Nieopłacone, albo Opłacone przy 0 zł. Zaktualizować też
  komentarze „Usuwania nie ma” w `order-details-page.ts` i `OrdersEndpoints.cs`, bo
  usuwanie wpisów teraz istnieje.
- **N4 (niska): port blokady i jego zakres.** Sekcja „Serializacja i współbieżność”
  opisuje zapytanie SQL, ale nie port Application, przez który handler je wywołuje. Nie
  mówi też, co blokada zwraca. Audyt potrzebuje numeru zamówienia do etykiety „Wpłata do
  {numer}”. Poprawka: nazwać port, np. `IOrderEntryLock.LockAsync(orderId)`, zwracający
  `null` albo numer zamówienia. Zapisać, że każdy przyszły zapis wpisów zamówienia (SF-10:
  korekty sprzedaży i limit obniżeń) musi brać tę samą blokadę. Inaczej serializacja
  przestanie chronić limit.
- **N5 (niska): podwójna implementacja reguły stanu.** Stan na liście liczy SQL, a
  w szczegółach `PaymentSummary`. Test zgodności jest przewidziany, ale reguła żyje w dwóch
  miejscach, a konwencje stosują DRY do reguł biznesowych. Poprawka: kolumny listy liczyć
  przez `PaymentSummary` z sum zrzutowanych w SQL (`V`, status, `W`, `Z`). Wyrażenie SQL
  zostawić tylko dla filtra `paymentState`. Dopisać `COALESCE(SUM(...), 0)` dla zamówień
  bez wpisów i przypadki brzegowe testu zgodności: `V = 0`, anulowane z `P = 0`, `P = V`.
- **N6 (niska): formularz edycji zamówienia a formularze wpisów.** `OrderDetailsPage.load`
  po każdym odświeżeniu nadpisuje `formInitial` i czyści `alert`. Specyfikacja każe
  pobierać szczegóły ponownie po każdym zapisie wpisu. Nie mówi jednak, czy „Dodaj
  wpłatę”, „Popraw” i „Usuń” są dostępne przy otwartej edycji zamówienia. Zapis wpisu może
  wtedy wyczyścić niezapisane zmiany zamówienia. Poprawka: ustalić, że akcje wpisów są
  niedostępne podczas edycji zamówienia i odwrotnie, albo że odświeżenie nie resetuje
  otwartego formularza. Warto też zalecić wydzielenie sekcji „Płatności” do osobnego
  komponentu, bo `order-details-page.ts` ma już 232 linie.
- **N7 (niska): kiedy dialog anulowania ostrzega o wpłatach.** PRD („Anulowanie i zwrot”)
  i design guide mówią o ostrzeżeniu, „jeżeli zamówienie ma wpłaty”. Specyfikacja
  ostrzega przy Zapłacono > 0, więc zamówienie z wpłatą 500 zł i zwrotem 500 zł ostrzeżenia
  nie dostanie. Interpretacja jest rozsądna, ale nie ma jej w Assumptions. Nie wiadomo też,
  jak UI ustali „> 0” bez liczenia na kwotach (niezmiennik 2). Poprawka: dopisać założenie
  i wskazać kryterium bez arytmetyki, np. `paid !== '0.00'` albo flagę z backendu.
- **N8 (niska): przypadek PLAT-05 należący do SF-10.** PLAT-05 zawiera przypadek „zwrot
  sprzedaży 1200 zł przy zamówieniu 1000 zł i wpłatach 1200 zł jest odrzucany”. W SF-9
  taki zwrot przejdzie, bo mieści się w Zapłacono. To zgodne z granicą zgłoszenia, ale
  Out of scope powinno wprost przypisać ten przypadek SF-10. Ktoś testujący PLAT-05 po
  SF-9 inaczej uzna go za błąd.
- **N9 (niska): tony stanów płatności.** Sekcja UI odsyła do „tonów stanów płatności według
  design guide”, a design guide (tabela statusów, wiersz „Płatność”) tonów nie podaje.
  Poprawka: wskazać tony `StatusTone` dla Nieopłacone, Częściowo opłacone i Opłacone, np.
  `warning`, `info`, `success`, albo `neutral` dla wszystkich.

### Unclear or Ambiguous Sections

- **Data / API → Endpoints:** brakuje kodu `404` dla `PUT` i `DELETE`, gdy nie istnieje
  zamówienie (`order_not_found` czy `entry_not_found`). Dla `POST` brakuje
  `400 idempotency_key_required` przy braku nagłówka, choć ten wzorzec jest w SF-8.
- **Behavior → Poprawa wpisu / Usunięcie wpisu:** nie jest powiedziane wprost, że poprawa
  i usunięcie wpisu działają też na zamówieniu anulowanym. Wynika to z braku zakazu.
- **Data / API → Types crossing a boundary:** format `version` wpisu nie jest podany.
  Zakładam base64 jak w SF-8, co ma znaczenie dla N2.
- **Data / API → Audyt:** `receivedBy` ma `IsPersonalData = true`. PRD („Anonimizacja danych
  klienta”) anonimizuje dane klienta, a „Przyjął” to osoba po stronie firmy. Trzeba
  ustalić, czy anonimizacja zamówienia czyści też to pole w audycie i w `orders.Payments`.
- **Behavior → Dodanie wpłaty / Dodanie zwrotu:** ten sam klucz wysłany do endpointu wpłat
  i zwrotów utworzy dwa wpisy, bo tabele są osobne. To prawdopodobnie akceptowalne, ale
  nie jest zapisane.
- **Assumptions → usunięcie fizyczne:** usunięcie kasuje `CreationRequestId`, więc spóźnione
  ponowienie pierwotnego `POST` po usunięciu wpisu utworzy go jeszcze raz. Ryzyko jest
  małe, ale granicę ochrony warto zapisać.

### Invented or Unsupported Requirements

None. „Do zapłaty”, odrzucanie dat przyszłych, zmiana rodzaju zwrotu oraz kolumny i filtr
listy wynikają z odpowiedzi użytkownika z 05.11.2026 i z przeniesienia z SF-8. Metody
zwrotu bez pobrania i limit 100 znaków dla „Przyjął” są jawnie opisanymi założeniami
autora.

## Docs & Decision Conflicts

| Spec section | Conflicts with | Nature of conflict |
|---|---|---|
| Anulowanie zamówienia z wpłatami | PRD §5 „Anulowanie i zwrot”; DESIGN-GUID (szczegóły zamówienia) | Warunek ostrzeżenia to Zapłacono > 0, a dokumenty mówią „ma wpłaty”. Rozbieżność drobna i nieopisana (N7). |
| Assumptions (`version` w `DELETE`) | Kod frontendu i API po SF-8 | Uzasadnienie powołuje się na wzorzec, którego w kodzie nie ma (N2). |
| Data / API → Endpoints (rozszerzone `GET`) | SF-8 02-spec, kryterium „odpowiedzi nie zawierają stanu płatności”, i test `Create_does_not_create_money_records_or_payment_state` | Zmiana zamierzona (SF-8 przekazał to do SF-9), ale specyfikacja nie wymienia testu do zastąpienia (N3). |
| UI (tony stanów) | DESIGN-GUID, tabela statusów | Odwołanie do tonów, których dokument nie definiuje (N9). |

## Project Invariant Check

| Invariant | Status | Note |
|---|---|---|
| 1. Sprzedaż oddzielona od pieniędzy | OK | Wpłata nie zmienia wartości ani statusu realizacji. Pobranie bez wpłaty daje Nieopłacone. Wpływ na sprzedaż należy do SF-19. Test SF-8 trzeba zastąpić (N3). |
| 2. Kwoty decimal na backendzie | OK | `decimal(18,2)` i `PaymentSummary`, UI tylko wyświetla. Kryterium ostrzeżenia w dialogu anulowania trzeba ustalić bez arytmetyki (N7). |
| 3. Dostęp egzekwowany przez API | OK | Sesja na wszystkich trasach, XSRF globalnie także dla `DELETE`. Testy `401` i XSRF przewidziane, `EndpointSecurityTests` obejmie nowe trasy. |
| 4. Ponowienia nie duplikują | OK | Unikalny `CreationRequestId` i blokada zamówienia. Powiązanie klucza z zamówieniem do uzupełnienia (N1). |
| 5. Audyt finansowy | OK | `Created`, `Updated` i `Deleted` w tej samej jednostce pracy, rodzic Order zgodny z `AuditRecord.RequireAllowedParent`. Usunięcie zostawia wszystkie pola. |
| 6. Sekrety po stronie serwera | OK | Bez nowych sekretów, logi tylko z identyfikatorami. |
| 7. Granice modułów i warstw | OK | Tabele `orders.*`, audyt przez `IAuditLog`, domena bez EF i HTTP. Port blokady do nazwania (N4). |
| 8. Brak danych identyfikujących w dokumentach | OK | Dane przykładowe są fikcyjne. |

## Risk

- Recorded: high
- Assessed: high. Zadanie dotyczy pieniędzy (Zapłacono, limity zwrotów), fizycznego
  usuwania wpisów finansowych i współbieżnych zapisów. Przed merge wymagany jest przegląd
  przez człowieka.

## Assumptions Detected

- Przyszła data odrzucana, data sprzed daty zamówienia dozwolona: zapisane (odpowiedź
  użytkownika).
- Rodzaj zwrotu można zmienić poprawką, limit obniżeń dodaje SF-10: zapisane.
- „Do zapłaty” tylko w szczegółach, przy Nieopłacone i Częściowo opłacone: zapisane.
- Kolumny i filtr listy w SF-9: zapisane.
- Metody zwrotu bez pobrania: zapisane (założenie autora).
- „Przyjął” jako tekst do 100 znaków i dane osobowe: zapisane. Skutek dla anonimizacji
  niejasny.
- Wpłaty i zwroty jako osobne encje z blokadą wiersza zamówienia: zapisane.
- Usunięcie fizyczne ze śladem w audycie: zapisane.
- `version` w zapytaniu `DELETE`: zapisane, z błędnym uzasadnieniem (N2).
- Limit liczony jako „Zapłacono po zmianie ≥ 0”: zapisane.
- Ostrzeżenie o wpłacie do anulowanego zamówienia tylko w UI: zapisane.
- Wpisy bez paginacji: zapisane.
- Filtr „Bez stanu” oznacza anulowane, łączenie przez OR: zapisane.
- Ostrzeżenie w dialogu anulowania przy Zapłacono > 0 zamiast „ma wpłaty”: **niezapisane**
  (N7).
- Skrót idempotencji bez `orderId`: **niezapisane, ukryte** (N1).
- Klucz sprawdzany przed limitem zwrotu: **niezapisane wprost** (N1).
- Lock zwraca numer zamówienia do etykiety audytu: **niezapisane** (N4).
- Akcje wpisów dostępne lub nie podczas edycji zamówienia: **niezapisane** (N6).
- Poprawa i usunięcie wpisów dozwolone na zamówieniu anulowanym: **niezapisane**
  (wynika z braku zakazu).

## Recommendation

- Proceed as-is. Ustalenia N1–N3 warto rozstrzygnąć w specyfikacji albo najpóźniej
  w planie (`sdd-plan SF-9`). Pozostałe uwagi plan może przyjąć jako doprecyzowania.
