# SF-14 — Stan integracji w UI

| | |
|---|---|
| **Source** | [SF-14](https://standev.youtrack.cloud/issue/SF-14) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Normalny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:54 |
| **Updated** | 2026-11-02 20:15:47 |
| **Fetched** | 2026-11-06 |

> **Uwagi do zgłoszenia (tylko sygnalizacja):**
> - „Zadania 19 i 20” i „Depends on: 13” to lokalne numery z breakdownu, czyli SF-19, SF-20 i SF-13.
> - SF-13 (PR #12, w przeglądzie) dostarcza API stanu i błędów; `lastSuccessAtUtc` to kursor danych (start przebiegu − 10 min) — patrz `docs/tasks/sf-13/06-implementation-done.md` (S2).

---

## Context
Użytkownik nie może sądzić, że dane są kompletne, gdy nie są. PRD sekcja 5 „Stany synchronizacji”, INT-01, design guide sekcje 3, 6, 8.

## Scope
- Stany w ustalonej kolejności: „Synchronizacja trwa”, „Błąd”, „Dane niekompletne”, „Aktualne”.
- „Ostatnia pełna kontrola” z czasem ostatniego sukcesu.
- Stan w nagłówku i ekran integracji z historią błędów oraz akcjami „Ponów” i „Synchronizuj teraz”.
- Wielokrotnego użytku komunikat o niekompletnych danych dla widoków raportowych.

## Acceptance criteria
- [ ] Stan wynika z warunków PRD, w tym „Dane niekompletne” przy otwartych błędach lub po ponad 25 godzinach od ostatniego sukcesu.
- [ ] UI pokazuje ostatnią pełną kontrolę, błąd i dostępną akcję.
- [ ] UI nie sugeruje kompletności przy awarii.
- [ ] Stan nie zależy wyłącznie od koloru.

## Out of scope
- Umieszczenie komunikatu w widokach (zadania 19 i 20).

## Metadata
- Breakdown ID: 14
- Risk: medium
- Depends on: 13
- PRD sections: 5 (Stany synchronizacji), 6 (INT-01); design guide 3, 6, 8
