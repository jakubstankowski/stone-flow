# SF-21 — Kopie zapasowe, procedura powrotu i odbiór MVP

| | |
|---|---|
| **Source** | [SF-21](https://standev.youtrack.cloud/issue/SF-21) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Poważny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:55 |
| **Updated** | 2026-11-02 20:15:55 |
| **Fetched** | 2026-11-11 |

> Część kryteriów wymaga działań na hostingu produkcyjnym (odtworzenie kopii, migracja schematu, procedura powrotu) oraz daty uruchomienia podanej przez właściciela. Zadanie nie podaje tej daty.

---

## Context
Uruchomienie produkcyjne wymaga odbioru scenariuszy z sekcji 11 i sprawdzonego odtwarzania. PRD sekcje 10 „Odzyskiwanie”, 11, 13; wymagania technologiczne sekcja 13.

## Scope
- Harmonogram i retencja kopii z zadania 1; próba odtworzenia.
- Procedura powrotu do działającej wersji frontendu i backendu.
- Testy E2E kluczowych procesów.
- Przejście scenariuszy finansowych z sekcji 11, w tym zmiany raportu wyjaśnionej listą „Historia zmian”.
- Sprawdzenie przełomu uruchomienia i ustawienie daty uruchomienia podanej przez właściciela.
- Odbiór UI według design guide sekcja 10.

## Acceptance criteria
- [ ] Odtworzenie kopii i migracja schematu zostały sprawdzone na hostingu.
- [ ] Sprawdzono procedurę powrotu do działającej wersji.
- [ ] Repozytorium i pakiet frontendu nie zawierają sekretów.
- [ ] Wszystkie scenariusze z sekcji 11 przechodzą, w tym zamówienie i wydatek otwarte w dniu uruchomienia.
- [ ] Oba widoki uzgadniają się z rekordami źródłowymi, także karta „Koszty”.
- [ ] Kluczowe procesy mają testy E2E, a CI przechodzi.
- [ ] Spełnione są kontrast, obsługa klawiatury i prezentacja niekompletności danych.

## Out of scope
- Migracja historii z Excela.
- Anonimizacja (zadanie 22).

## Metadata
- Breakdown ID: 21
- Risk: high — human review required before merge
- Depends on: 4, 18, 20
- PRD sections: 4 (Przełom uruchomienia), 10 (Odzyskiwanie), 11, 13
