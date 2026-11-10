# Implementation done — SF-20

## Summary

- `GET /api/reports/cash-flow` (Reporting): okres, przycięcie do daty uruchomienia, porównanie i stan „przed uruchomieniem” jak w SF-19 (`ReportPeriod`, `LaunchCutoff`, `Comparison`). Odpowiedź zawiera:
  - karty Wpływy, Wypływy i Zmiana środków, każdą z porównaniem;
  - szereg wpływów i wypływów w czasie;
  - podział wypływów: opłacone wydatki, wypłaty właścicieli, zwroty;
  - 10 ostatnich operacji.
- Wspólny odczyt operacji (`CashOperationReader`: źródła, bezpiecznik 10 000, kolejność) wydzielony 1:1 z `ListOperationsHandler`. Lista „Operacje” i raport liczą z tych samych źródeł i reguł (`CashFlowTotals`), więc karty równają się liście przy tym samym okresie.
- Pulpit: widok „Przepływy pieniężne” zastępuje zaślepkę.
  - Wybór okresu i nagłówek są wspólne dla obu widoków.
  - Wczytuje się tylko aktywny widok; błędy i żądania są osobne dla każdego widoku.
  - Wykres Chart.js z tabelą danych i lista podziału.
  - Karty prowadzą do `/operacje` z okresem i rodzajem.
- Lista „Operacje” czyta `od`, `do` i `rodzaj` przy wejściu z Pulpitu, a potem usuwa je z adresu (`replaceUrl`), więc „Wstecz” ich nie nakłada ponownie.
- Typy i funkcje operacji (`OperationItem`, etykiety, `sourceLink`) przeniesione do `shared/dictionaries/operations.ts`. `operation-models.ts` eksportuje je dalej pod tymi samymi nazwami.
- Bez migracji i bez nowych kontraktów między modułami.
- Specyfikacja: PASS WITH ISSUES. Przegląd kodu: PASS WITH ISSUES, bez uwag krytycznych. Poprawione uwagi 2–4: długość komentarza, nieużywana stała `OUTFLOW_KINDS` oraz `aria-label` „Wartości są też na liście przy wykresie.” (także w widoku „Sprzedaż”).

## Tests

| Test | Result |
|---|---|
| Reporting.Tests | 266/266 |
| Architecture.Tests | 56/56 |
| CashFlowReportApiTests (LocalDB) | 18/18 |
| OperationsApiTests, SalesReportApiTests, EndpointSecurityTests, OpenApiTests (LocalDB) | 18/18, 24/24, 9/9, 2/2 |
| Frontend: typecheck, lint, build | Passed |
| Frontend `ng test` | 948/950 w pełnym przebiegu; 2 niestabilne testy, inne za każdym razem (limit czasu pod obciążeniem). Osobno: 99/99. |
| `dotnet build -c Release -m:1`, `dotnet format --verify-no-changes` | Passed |
| Pełny zestaw integracyjny | Not run (CI) |

## Deviations

- `sourceLink` przeniesione do `shared` zamiast kopii (plan, uwaga 1 przeglądu specyfikacji).
- Próg układu to `xl` (1280 px), a nie 1200 px ze specyfikacji, tak samo jak w widoku „Sprzedaż”.
- `shared/dictionaries/operations.spec.ts` importuje `features/operations/operation-models`, żeby sprawdzić, że re-eksport zwraca te same obiekty. Dotyczy tylko testu.

## Open Items

- **Human review required before merge** (ryzyko high).
- Kolejność merge: najpierw #20 (SF-18 do `main`), potem #19 (SF-19), na końcu ten PR.
- Decyzje wykonawcy z 10.11.2026 do potwierdzenia przez właściciela (`02-spec.md`, Assumptions). Najważniejsze:
  - karty prowadzą do „Operacji” z efektywnym okresem;
  - pozycja „Zwroty” łączy oba rodzaje zwrotu;
  - „Ostatnie operacje” to 10 pozycji;
  - zmiana środków jest w tabeli wykresu, a nie jako trzecia seria.
- Import `features/integration/incomplete-data-notice` w `dashboard-page.ts` (sprzed SF-20) łamie zasadę braku importów między obszarami. Komponent zależy od `IntegrationStatus`, więc przeniesienie do `shared` to osobna zmiana.
- Niestabilne testy frontendu pod obciążeniem pamięci warto obserwować w CI.

## Scenariusze testów ręcznych (UI)

Warunki: zalogowany właściciel, dane z kilku miesięcy po dacie uruchomienia.

| # | Kroki | Oczekiwany wynik |
|---|---|---|
| 1 | Pulpit → przełącznik **Przepływy pieniężne** | Karty Wpływy, Wypływy, Zmiana środków; wykres wpływów i wypływów; podział wypływów; „Ostatnie operacje”. Okres i „Stan na” bez zmian. |
| 2 | Porównaj karty z listą **Operacje** dla tego samego okresu (sumy nad tabelą) | Wpływy, Wypływy i Zmiana środków są równe sumom listy co do grosza. |
| 3 | Kliknij kartę **Wpływy** | `/operacje` z okresem i filtrem „Wpłata”; po chwili adres bez parametrów. Suma wpływów równa karcie. |
| 4 | Kliknij **Wypływy**, zmień filtr na liście, kliknij **Wstecz** i wejdź ponownie przez kartę | Filtr: zwroty, opłacone wydatki, wypłaty. Po „Wstecz” wracasz na Pulpit; filtr zmieniony ręcznie nie jest nadpisywany przez stary adres. |
| 5 | Dodaj zamówienie bez wpłaty z dzisiejszą datą | Wpływy bez zmian (widok „Sprzedaż” rośnie). |
| 6 | Dodaj do niego wpłatę 500 zł | Wpływy +500 zł; sprzedaż bez zmian (brak drugiej sprzedaży). |
| 7 | Dodaj wydatek bez daty zapłaty, potem oznacz go jako opłacony dziś | Najpierw wypływy bez zmian; po opłaceniu rosną o kwotę wydatku. |
| 8 | Opłacony wydatek `-50` z dzisiejszą datą | Wypływy i pozycja „Opłacone wydatki” maleją o 50 zł. |
| 9 | Zamówienie z datą sprzed uruchomienia, wpłata po uruchomieniu, zwrot pieniędzy | Wpłata zwiększa wpływy, zwrot zwiększa wypływy (pozycja „Zwroty”). |
| 10 | Dodaj wypłatę właściciela 1000 zł | Wypływy i „Wypłaty właścicieli” +1000 zł; w widoku „Sprzedaż” wynik bez zmian. |
| 11 | Tabela danych pod wykresem i lista podziału | Każda wartość jest dostępna tekstowo; suma pozycji podziału równa się Wypływom. |
| 12 | „Ostatnie operacje” → **Pokaż wszystkie operacje** | 10 najnowszych w kolejności listy; link otwiera `/operacje` z okresem. Kliknięcie opisu prowadzi do źródła. |
| 13 | Przełączaj widoki po zmianie okresu na poprzedni miesiąc i rok | Oba widoki pokazują ten sam okres; karty, wykresy i podpisy zmieniają podstawę razem. |
| 14 | Okres w całości przed datą uruchomienia | Komunikat zamiast kart z zerami. |
| 15 | Wyszukaj na stronie słowa „zysk” i „saldo” | Brak wystąpień w widoku przepływów. |
| 16 | Tab po wykresie, strzałki, Escape; czytnik ekranu | Tooltip działa z klawiatury, czytnik ogłasza okres i kwotę. |
| 17 | Widok telefonu (~390 px) | Karty w jednej kolumnie, wykres i lista pod sobą, bez przewijania w poziomie. |
