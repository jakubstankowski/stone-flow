# SF-6 — Logowanie, reset hasła, sesja i ochrona API

| | |
|---|---|
| **Source** | [SF-6](https://standev.youtrack.cloud/issue/SF-6) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Poważny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:53 |
| **Updated** | 2026-11-02 20:15:34 |
| **Fetched** | 2026-11-04 |

> **Uwagi do zgłoszenia (tylko sygnalizacja):**
> - Zależność SF-4 (Breakdown ID 4, wdrożenia) nie jest zakończona — SF-4 ma status `fetched` i została odłożona przez właściciela 03.11.2026. Kryterium „Sesja działa między domenami na wdrożonym środowisku” wymaga wdrożonych środowisk z SF-4.
> - D-05 w PRD: „Do potwierdzenia: dostawca wysyłki e-mail dla resetu hasła” — dostawca nie jest wybrany, a zgłoszenie każe go wybrać.
> - Ryzyko high — przed merge wymagany przegląd przez człowieka.

---

## Context
Jedno konto właściciela chroni dane finansowe i osobowe. Frontend i API są na osobnych domenach, więc sesję, CORS i CSRF trzeba zaprojektować jawnie. PRD sekcja 3, DOST-01.

## Scope
- Logowanie e-mailem i hasłem, wygaśnięcie sesji po 60 minutach bezczynności.
- Reset hasła jednorazowym linkiem e-mail; wybór dostawcy wysyłki (D-05).
- Egzekwowanie uwierzytelnienia w API, jawny CORS, CSRF przy cookies.
- Link z panelu administracyjnego sklepu do strony logowania ERP.
- Ekran logowania i komunikat wygaśnięcia sesji.
- Do rozstrzygnięcia w specyfikacji: utworzenie pierwszego konta, czas ważności linku, ochrona przed zgadywaniem hasła, wylogowanie, unieważnienie sesji po resecie, kto dodaje link w panelu, lista wyjątków od DOST-01 (webhook, logowanie, reset, health endpoint).

## Acceptance criteria
- [ ] Bez ważnej sesji API odmawia każdego odczytu i zapisu poza jawnie wymienionymi wyjątkami.
- [ ] Sesja wygasa po 60 minutach bezczynności, a wygaśnięcie nie ujawnia danych.
- [ ] Link resetu działa jednorazowo.
- [ ] Link z panelu sklepu prowadzi do logowania i nie uwierzytelnia.
- [ ] Sesja działa między domenami na wdrożonym środowisku.

## Out of scope
- Wiele kont i role.
- Wspólne logowanie z WooCommerce.

## Metadata
- Breakdown ID: 6
- Risk: high — human review required before merge
- Depends on: 3, 4, 5
- PRD sections: 3, 6 (DOST-01), 10, 11
