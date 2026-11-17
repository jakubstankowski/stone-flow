# SF-20 — Widok „Przepływy pieniężne”

| | |
|---|---|
| **Source** | [SF-20](https://standev.youtrack.cloud/issue/SF-20) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Poważny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:54 |
| **Updated** | 2026-11-02 20:15:54 |
| **Fetched** | 2026-11-10 |

---

## Context
Drugi widok pokazuje rzeczywiste pieniądze, niezależnie od wartości zamówień. PRD sekcje 5 „Zawartość widoków”, 8, FIN-01–07, URU-01.

## Scope
- Karty wpływów, wypływów i zmiany środków prowadzące do listy „Operacje”.
- Wykres wpływów i wypływów w czasie, podział wypływów według rodzaju operacji, ostatnie operacje.
- Przełącznik widoków: wszystkie karty, wykresy i podpisy zmieniają podstawę jednocześnie.
- Reguły okresów i komunikat o niekompletnych danych z zadań 19 i 14.

## Acceptance criteria
- [ ] Nieopłacone zamówienie nie zwiększa wpływów, a wpłata nie tworzy drugiej sprzedaży.
- [ ] Suma wpływów i wypływów równa się liście „Operacje” przy tym samym okresie.
- [ ] Wydatek bez daty zapłaty nie zwiększa wypływów.
- [ ] Opłacony wydatek ujemny pomniejsza wypływy.
- [ ] Wpłata po uruchomieniu do zamówienia sprzed uruchomienia zwiększa wpływy; zwrot zwiększa wypływy.
- [ ] Widok nie używa słowa „zysk” i nie pokazuje salda rachunku.
- [ ] Wartości są dostępne również tekstowo.

## Out of scope
- Saldo rachunku i saldo początkowe.
- Wpływy niezwiązane z zamówieniem.

## Metadata
- Breakdown ID: 20
- Risk: high — human review required before merge
- Depends on: 17, 19
- PRD sections: 5 (Zawartość widoków), 6 (FIN-01–07, URU-01), 8; design guide 4, 7
