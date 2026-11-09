# SF-17 — Wypłaty właścicieli i lista „Operacje”

| | |
|---|---|
| **Source** | [SF-17](https://standev.youtrack.cloud/issue/SF-17) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Poważny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:54 |
| **Updated** | 2026-11-02 20:15:51 |
| **Fetched** | 2026-11-09 |

---

## Context
Wypłaty są odrębne od kosztów, a lista „Operacje” jest punktem uzgodnienia przepływów pieniężnych. PRD sekcja 5 „Wypłata właściciela”, „Lista Operacje”, PLAT-03, FIN-05, 06.

## Scope
- Wypłata właściciela: osoba, kwota, data, metoda; poprawa i usunięcie z potwierdzeniem i audytem.
- Lista „Operacje”: wpłaty, zwroty, opłacone wydatki, wypłaty, z filtrem okresu i rodzaju oraz przejściem do źródła.
- Odczyt danych innych modułów przez jawne interfejsy.
- Do rozstrzygnięcia w specyfikacji: pomijanie kwot sprzed daty uruchomienia, gdy okres obejmuje tę datę; walidacja kwoty wypłaty; słownik osób.

## Acceptance criteria
- [ ] Wypłata zwiększa wypływy, nie zmienia kosztów ani sprzedaży.
- [ ] Poprawa i usunięcie zostawiają audyt; usunięcie wymaga potwierdzenia.
- [ ] Korekty sprzedaży nie pojawiają się na liście.
- [ ] Opłacony wydatek ujemny jest wypływem z kwotą ujemną.
- [ ] Lista przy okresie obejmującym datę uruchomienia nie obejmuje kwot wyłączonych z raportów.

## Out of scope
- Karty przepływów (zadanie 20).

## Metadata
- Breakdown ID: 17
- Risk: high — human review required before merge
- Depends on: 9, 15
- PRD sections: 5 (Wypłata właściciela, Lista Operacje), 6 (PLAT-03, FIN-05, 06), 7, 10 (Architektura)
