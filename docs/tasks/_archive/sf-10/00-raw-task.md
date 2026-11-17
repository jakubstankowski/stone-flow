# SF-10 — Korekta sprzedaży i limit obniżeń

| | |
|---|---|
| **Source** | [SF-10](https://standev.youtrack.cloud/issue/SF-10) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Poważny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:53 |
| **Updated** | 2026-11-02 20:15:41 |
| **Fetched** | 2026-11-05 |

> **Uwagi do zgłoszenia (tylko sygnalizacja):**
> - „Zadanie 19” i „Depends on: 9” to lokalne numery z breakdownu, czyli SF-19 i SF-9.
> - SF-9 zapisało, że SF-10 musi używać tej samej blokady zamówienia (`IOrderLock`) przy korektach sprzedaży.
> - Ryzyko high: przed merge wymagany jest przegląd przez człowieka.

---

## Context
Oddanie towaru bez zwrotu pieniędzy obniża sprzedaż bez ruchu pieniędzy. Limit chroni przed ujemną sprzedażą zamówienia. PRD sekcja 5 „Korekta sprzedaży”, „Limit obniżeń sprzedaży”, PLAT-07.

## Scope
- Korekta sprzedaży: kwota większa od 0 zł, data, powód, bez metody; dla zamówień ręcznych i ze sklepu.
- Poprawa i usunięcie korekty z potwierdzeniem i audytem.
- Limit: suma zwrotów sprzedaży i korekt nie przekracza wartości; dotyczy wpisu, poprawki i ręcznej korekty wartości.
- Ostrzeżenie „Obniżenia sprzedaży przekraczają wartość o X zł”, wyliczane ze stanu zamówienia.
- Opisy w formularzach wyjaśniające różnicę między korektą wartości, zwrotem i korektą sprzedaży.

## Acceptance criteria
- [ ] Korekta 500 zł do zamówienia 1000 zł z zaliczką 300 zł nie zmienia Zapłacono ani stanu płatności.
- [ ] Zmiana przekraczająca limit jest odrzucana z komunikatem podającym dostępną kwotę.
- [ ] Zwrot sprzedaży 1200 zł przy zamówieniu 1000 zł i wpłatach 1200 zł jest odrzucany.
- [ ] Ostrzeżenie pokazuje się, gdy wartość spadnie poniżej sumy obniżeń.
- [ ] Korekta jest pokazana osobno od wpłat i zwrotów.

## Out of scope
- Obniżenie sprzedaży w raportach (zadanie 19).

## Metadata
- Breakdown ID: 10
- Risk: high — human review required before merge
- Depends on: 9
- PRD sections: 5 (Korekta sprzedaży, Limit obniżeń sprzedaży), 6 (PLAT-07)
