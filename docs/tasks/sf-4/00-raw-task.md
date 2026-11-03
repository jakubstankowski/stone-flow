# SF-4 — Automatyczne wdrożenia i kontrolowane migracje

| | |
|---|---|
| **Source** | [SF-4](https://standev.youtrack.cloud/issue/SF-4) |
| **Project** | SF |
| **Type** | Zadanie |
| **State** | Otwarta |
| **Priority** | Poważny |
| **Sprint** | |
| **Assignee** | standev |
| **Reporter** | standev |
| **Estimate** | |
| **Created** | 2026-11-02 20:10:52 |
| **Updated** | 2026-11-02 20:15:30 |
| **Fetched** | 2026-11-03 |

> **Uwagi do zgłoszenia (tylko sygnalizacja):**
> - Zależność SF-1 (Breakdown ID 1) nie jest zakończona — `docs/tasks/sf-1/01-weryfikacja.md` opisuje częściową weryfikację, a D-08 w PRD jest „częściowo rozstrzygnięte” i „blokuje ostateczną konfigurację środowisk”. Zakres zadania (publikacja na hosting, migracje produkcyjne) leży w obszarze D-08; `rules.md` zakazuje implementacji obszaru blokowanego przez nierozstrzygniętą decyzję.
> - Zgłoszenie używa nazwy „SMART ASP.NET”; `docs/tasks/sf-1/01-weryfikacja.md` wskazuje, że chodzi o SmarterASP.NET.
> - Zgłoszenie nie podaje narzędzia monitoringu dostępności ani sposobu pilnowania kolejności wdrożeń względem CI (przejęte z SF-3).

---

## Context
Webhook i sesję między domenami można sprawdzić tylko na prawdziwym hostingu, a przegląd projektu oznaczył migracje produkcyjne jako operacje nieodwracalne. Wdrożenia powstają wcześnie, by wykryć problemy hostingu przed budową funkcji. PRD sekcje 10 i 11.

## Scope
- Publikacja frontendu na Vercel z routingiem SPA i konfigurowalnym adresem API.
- Publikacja backendu na SMART ASP.NET po przejściu wymaganych kontroli.
- Kontrolowana kolejność migracji, obsługa błędu, sekrety poza repozytorium.
- Ograniczone uprawnienia do produkcji i sekretów wdrożeniowych.
- Izolacja preview od produkcyjnej bazy i integracji.
- HTTPS i monitoring dostępności.

## Acceptance criteria
- [ ] Oba wdrożenia uruchamiają się automatycznie po przejściu kontroli.
- [ ] Odświeżenie podstrony SPA nie powoduje błędu routingu.
- [ ] Nieudana migracja zatrzymuje publikację, a procedura postępowania jest opisana.
- [ ] Preview nie ma dostępu do produkcyjnej bazy ani integracji.
- [ ] Pakiet frontendu nie zawiera sekretów.

## Out of scope
- Kopie zapasowe i próba odtworzenia (zadanie 21).

## Metadata
- Breakdown ID: 4
- Risk: high — human review required before merge
- Depends on: 1, 3
- PRD sections: 10, 11
