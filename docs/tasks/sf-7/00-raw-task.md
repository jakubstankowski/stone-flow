# SF-7 — Zapis audytu zmian finansowych

| | |
|---|---|
| **Source** | [SF-7](https://standev.youtrack.cloud/issue/SF-7) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Poważny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:53 |
| **Updated** | 2026-11-02 20:15:36 |
| **Fetched** | 2026-11-04 |

> **Uwagi do zgłoszenia (tylko sygnalizacja):**
> - „Out of scope” odwołuje się do „zadanie 22” (anonimizacja wpisów), a `task-breakdown.md` kończy się na zadaniu 21. Nie wiadomo, które zgłoszenie obejmuje anonimizację, a w „Scope” specyfikacja i tak ma ją uwzględnić.
> - Numery „zadanie 18” i „Depends on: 3, 6” to lokalne numery z breakdownu. Według `task-breakdown.md` odpowiadają SF-18, SF-3 i SF-6.
> - Ryzyko high: przed merge wymagany jest przegląd przez człowieka.

---

## Context
Każdy moduł finansowy zapisuje audyt, a zamykanie okresów jest poza MVP, więc audyt wyjaśnia zmiany minionych raportów. Mechanizm musi powstać przed modułami, które go używają. PRD sekcja 5 „Historia zmian”, AUD-01.

## Scope
- Wspólny mechanizm zapisu: autor (konto albo autor systemowy „Synchronizacja WooCommerce” / „Zadanie automatyczne”), czas UTC, obiekt z datą biznesową, operacja, zakres zmiany.
- Zapis w tej samej transakcji co zmiana danych.
- Ślad usunięcia widoczny po zniknięciu obiektu.
- Do rozstrzygnięcia w specyfikacji: moduł będący właścicielem danych audytu i interfejs zapisu zgodny z niezmiennikiem 7, z uwzględnieniem anonimizacji.

## Acceptance criteria
- [ ] Moduł może zapisać wpis przez jawny interfejs, bez dostępu do tabel innego modułu.
- [ ] Nieudany zapis audytu cofa zmianę danych.
- [ ] Wpis zawiera autora, czas UTC, obiekt, operację i zakres zmiany.
- [ ] Autor systemowy jest odróżniony od konta.

## Out of scope
- Lista „Historia zmian” (zadanie 18).
- Anonimizacja wpisów (zadanie 22).

## Metadata
- Breakdown ID: 7
- Risk: high — human review required before merge
- Depends on: 3, 6
- PRD sections: 5 (Historia zmian), 6 (AUD-01), 7, 10
