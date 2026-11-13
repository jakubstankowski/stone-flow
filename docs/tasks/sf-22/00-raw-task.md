# SF-22 — Anonimizacja danych klienta

| | |
|---|---|
| **Source** | [SF-22](https://standev.youtrack.cloud/issue/SF-22) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Poważny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:55 |
| **Updated** | 2026-11-02 20:15:57 |
| **Fetched** | 2026-11-13 |

> Implementację blokuje otwarta decyzja D-09 (okres retencji danych zamówień), której rozstrzygnięcie jest poza zakresem zadania. Zadanie nie podaje długości okresu retencji.

---

## Context
Zamówień nie usuwa się, więc żądanie usunięcia danych obsługuje ręczna, nieodwracalna anonimizacja. Implementację blokuje otwarta decyzja D-09. PRD sekcja 5 „Anonimizacja danych klienta”, DANE-01.

## Scope
- Akcja „Anonimizuj dane klienta” z potwierdzeniem, dostępna po upływie okresu retencji.
- Usunięcie danych klienta z zamówienia i wpisów audytu, z zachowaniem kwot, dat, wpłat, zwrotów i śladu operacji.
- Blokada przywrócenia danych przez synchronizację.
- Do rozstrzygnięcia w specyfikacji: moment, od którego liczy się retencja; dane w historii błędów synchronizacji, logach, kopiach zapasowych i polach tekstowych (uwagi, powód zwrotu, powód korekty); pole na fakt anonimizacji; edycja zamówienia po anonimizacji.

## Acceptance criteria
- [ ] Przed upływem okresu retencji akcja jest niedostępna, także przez API.
- [ ] Po potwierdzeniu dane klienta znikają z zamówienia i audytu, a raporty się nie zmieniają.
- [ ] Synchronizacja nie przywraca danych.
- [ ] Operacja zostawia ślad audytu bez danych osobowych.

## Out of scope
- Rozstrzygnięcie D-09, które wymaga księgowego lub prawnika.

## Metadata
- Breakdown ID: 22
- Risk: high — human review required before merge
- Depends on: 12, decyzja D-09
- PRD sections: 5 (Anonimizacja danych klienta), 6 (DANE-01), 12 (D-09), 13
