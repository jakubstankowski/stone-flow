# SF-9 — Wpłaty, zwroty i wyliczany stan płatności

| | |
|---|---|
| **Source** | [SF-9](https://standev.youtrack.cloud/issue/SF-9) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Poważny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:53 |
| **Updated** | 2026-11-02 20:15:39 |
| **Fetched** | 2026-11-05 |

> **Uwagi do zgłoszenia (tylko sygnalizacja):**
> - Numery „zadanie 10 / 19 / 20” i „Depends on: 8” to lokalne numery z breakdownu, czyli SF-10, SF-19, SF-20 i SF-8.
> - Specyfikacja SF-8 przeniosła do SF-9 kolumny „Zapłacono” i „Stan płatności” na liście zamówień oraz filtr po stanie płatności. Zgłoszenie SF-9 ich nie wymienia.
> - Ryzyko high: przed merge wymagany jest przegląd przez człowieka.

---

## Context
Wpłaty są jedynym zapisem wpływu, a stan płatności wynika wyłącznie z nich. Dodawane ręcznie także dla zamówień z WooCommerce. PRD sekcje 5 „Rejestracja płatności”, „Anulowanie i zwrot”, PLAT-01, 04, 05, 06.

## Scope
- Wpłata: kwota, data operacji, metoda, opcjonalne „Przyjął” przy gotówce; wiele wpłat na zamówieniu.
- Zwrot: wymagany rodzaj bez wartości domyślnej („Zwrot sprzedaży” / „Zwrot pieniędzy”), kwota, data, metoda, powód.
- Backendowe wyliczanie kwoty Zapłacono i stanu: Nieopłacone, Częściowo opłacone, Opłacone.
- Ostrzeżenia: „Nadpłata X zł”, „Do zwrotu X zł”, wpłata do anulowanego zamówienia, anulowanie zamówienia z wpłatami.
- Poprawa i usunięcie wpisów z potwierdzeniem i audytem.
- Do rozstrzygnięcia w specyfikacji: daty przyszłe i wcześniejsze niż data zamówienia, zmiana rodzaju zwrotu po zapisie, współbieżne wpisy przekraczające limit, kwota do zapłaty.

## Acceptance criteria
- [ ] Kilka wpłat sumuje się, a stan przechodzi z Nieopłacone przez Częściowo opłacone na Opłacone.
- [ ] Zwrot obniża Zapłacono i przelicza stan.
- [ ] Nadpłata pozostawia Opłacone i pokazuje ostrzeżenie.
- [ ] Zamówienie anulowane nie ma stanu i pokazuje kwotę do zwrotu.
- [ ] Zamówienie o wartości 0 zł ma stan Opłacone.
- [ ] Wpłata i zwrot muszą być większe od 0 zł, a zwrot nie może przekroczyć Zapłacono.
- [ ] Zmiana, po której Zapłacono byłoby ujemne, jest odrzucana.
- [ ] Pobranie bez dodanej wpłaty pozostaje nieopłacone.
- [ ] Reguły mają testy jednostkowe; kwoty używają `decimal(18,2)`.

## Out of scope
- Limit obniżeń sprzedaży (zadanie 10).
- Wpływ na raporty (zadania 19 i 20).
- Automatyczne wpłaty i zwroty z WooCommerce.

## Metadata
- Breakdown ID: 9
- Risk: high — human review required before merge
- Depends on: 8
- PRD sections: 5 (Rejestracja płatności, Anulowanie i zwrot, Błędny wpis), 6 (PLAT-01, 04, 05, 06, KWO-01), 7, 8 (Reguły kwot)
