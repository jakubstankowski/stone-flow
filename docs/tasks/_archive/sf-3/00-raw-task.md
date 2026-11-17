# SF-3 — Monorepo, CI i szkielet modułów backendu

| | |
|---|---|
| **Source** | [SF-3](https://standev.youtrack.cloud/issue/SF-3) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | W realizacji |
| **Priority** | Normalny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:09:03 |
| **Updated** | 2026-11-03 14:02:28 |
| **Fetched** | 2026-11-03 |

> **Uwagi do zgłoszenia (tylko sygnalizacja):**
> - Druga ścieżka przykładowego projektu jest zniekształcona: `C:\projects\inne\sitC:\projects\inne\sit` (prawdopodobnie zdublowane `C:\projects\inne\sit`).
> - Zależność SF-1 (Breakdown ID 1) nie jest zakończona — `docs/tasks/sf-1/01-weryfikacja.md` opisuje częściową weryfikację, D-08 pozostaje otwarte. Zakres zadania obejmuje przypięcie wersji SDK, runtime i Node, które zależą od wyniku SF-1.

---

## Context
Wszystkie kolejne zadania potrzebują repozytorium, przypiętych wersji, CI i granic modułów. PRD sekcja 10.

## Scope
- stwórz projekt w visual studio sln nazwij go stone-flow
- Struktura monorepo: frontend, backend, testy, dokumentacja, konfiguracja wdrożeń; uzupełnienie TODO w `.sdd/context/conventions.md` i `role.md`.
- Przypięcie SDK, runtime i Node, lockfile, `global.json`, centralne wersje pakietów .NET.
- Angular strict i ASP.NET Core z OpenAPI, Problem Details i korelacją logów.
- Modułowy monolit: zamówienia, wydatki, raportowanie, integracje; warstwy Onion; EF Core z SQL Server i wersjonowane migracje.
- Health endpoint.
- Odczyt daty uruchomienia z konfiguracji wdrożenia, dostępny dla modułów, bez zmiany z poziomu aplikacji.
- CI: kompilacja obu aplikacji, typy, formatowanie, testy.
- Instrukcja startu i przykładowa konfiguracja bez sekretów.

Dla przykładu można zajrzeć w inne moje rpojektuy z CQRS np tutaj C:\projects\inne\autoecom albo tutaj  C:\projects\inne\sitC:\projects\inne\sit 

## Acceptance criteria
- [ ] Projekt da się odtworzyć i uruchomić według instrukcji, z zapisanymi wersjami.
- [ ] CI przechodzi dla frontendu i backendu i blokuje błąd typów, formatowania lub testu.
- [ ] Domena nie zależy od HTTP, EF Core ani hostingu, a moduły nie sięgają do swoich encji.
- [ ] Health endpoint odpowiada.
- [ ] Data uruchomienia jest dostępna z konfiguracji.
- [ ] Repozytorium nie zawiera sekretów.

## Out of scope
- Publikacja na hostingi (zadanie 4).
- Motyw i powłoka UI (zadanie 5).

## Metadata
- Breakdown ID: 3
- Risk: medium
- Depends on: 1
- PRD sections: 4 (Przełom uruchomienia), 10
