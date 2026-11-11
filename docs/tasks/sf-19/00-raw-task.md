# SF-19 — Reguły raportowe i widok „Sprzedaż”

| | |
|---|---|
| **Source** | [SF-19](https://standev.youtrack.cloud/issue/SF-19) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | W realizacji |
| **Priority** | Poważny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:54 |
| **Updated** | 2026-11-09 16:11:41 |
| **Fetched** | 2026-11-09 |

---

## Context
Widok łączy zamówienia, zwroty sprzedaży, korekty i wydatki według definicji z PRD. To serce produktu: wyniki muszą dać się prześledzić do rekordów. Zadanie dostarcza też wspólne reguły okresów. PRD sekcje 5 „Analiza finansów”, 8, FIN-01–06, ZAM-07, WYD-07, URU-01.

## Scope
- Wspólne reguły okresów: bieżący miesiąc, poprzedni miesiąc, rok, własny zakres, porównanie, wyłączenie sprzed uruchomienia.
- Sprzedaż, koszty, wynik przed wypłatami, liczba zamówień; obliczenia wyłącznie w backendzie.
- Wykresy, podział na kanały i kategorie kosztów, wartości dostępne tekstowo.
- Zestawienie składników sprzedaży z przejściem do zamówienia.
- Opis podstawy kwot, daty przypisania i aktualności danych; komunikat o niekompletnej integracji.
- Do rozstrzygnięcia w specyfikacji: uzgodnienie karty „Koszty” z listą wydatków; początek własnego zakresu względem daty uruchomienia; czy bieżący miesiąc kończy się dziś; czwarta karta KPI w układzie z design guide; opis porównania niepełnego miesiąca; przejścia z kategorii i kanałów; suma zestawienia dla całego okresu przy paginacji.

## Acceptance criteria
- [ ] Suma zestawienia równa się karcie „Sprzedaż” przy tym samym okresie.
- [ ] Korekta do zamówienia z wcześniejszego miesiąca jest widoczna w okresie swojej daty.
- [ ] Suma kanałów równa się sprzedaży.
- [ ] Zamówienia anulowane, pending, failed, szkice i sprzed uruchomienia nie liczą się do sprzedaży ani do liczby zamówień.
- [ ] Zwrot pieniędzy nie obniża sprzedaży; zwrot nadpłaty 200 zł przy zamówieniu 1000 zł pozostawia sprzedaż 1000 zł.
- [ ] Po anulowaniu zwroty i korekty zamówienia nie obniżają sprzedaży, a po przywróceniu znów ją obniżają.
- [ ] Koszty obejmują opłacone i nieopłacone wydatki według daty kosztu, a wydatek ujemny je pomniejsza.
- [ ] Przy bazie równej zero lub sprzed uruchomienia widok pokazuje „brak podstawy porównania”.
- [ ] Wypłaty właścicieli nie obniżają wyniku.
- [ ] Reguły mają testy jednostkowe, także dla scenariuszy z §11.

## Out of scope
- Widok przepływów (zadanie 20).
- Saldo rachunku, wynik zarządczy, liczba unikalnych klientów, prognozy.

## Metadata
- Breakdown ID: 19
- Risk: high — human review required before merge
- Depends on: 10, 12, 14, 16
- PRD sections: 5 (Analiza finansów, Podział sprzedaży na kanały), 6 (FIN-01–06, ZAM-07, WYD-07, URU-01, KWO-01), 8; design guide 4, 7
