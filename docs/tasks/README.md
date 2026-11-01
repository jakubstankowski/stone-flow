# Zadania

Artefakty poszczególnych zadań tworzone przez pipeline
[standev-sdd](https://github.com/jakubstankowski/standev-sdd). Każde zadanie ma jeden folder
nazwany identyfikatorem zgłoszenia z YouTrack (projekt `SF`).

```
docs/tasks/
├── _project/
│   ├── project-review.md           bramka PASS/FAIL dla całego PRD
│   └── task-breakdown.md           PRD → backlog gotowy do wklejenia do YouTrack
├── _archive/                       zakończone zadania
└── sf-12/
    ├── task.md                     status, ryzyko, zależności
    ├── 00-raw-task.md              zgłoszenie, dosłownie
    ├── 02-spec.md                  specyfikacja techniczna
    ├── 03-spec-review.md           bramka: czy specyfikacja jest poprawna?
    ├── 04-implementation-plan.md   wykonywalna lista kroków
    ├── 05-code-review.md           bramka: czy kod jest poprawny?
    ├── 06-implementation-done.md   manifest dostarczenia
    └── ui/                         opcjonalne zrzuty i makiety czytane przez sdd-spec
```

Nie ma pliku `01-`. Numeracja pochodzi z pierwotnego szablonu, dzięki czemu artefakty
układają się w kolejności pipeline'u.

## Prowadzenie zadania

```
sdd-fetch-tasks sf-12       zgłoszenie → 00-raw-task.md
sdd-spec sf-12              odpowiedzi na pytania, powstaje 02-spec.md
sdd-review-spec sf-12       bramka — przed planowaniem wynik nie może być FAIL
sdd-plan sf-12              plan jednego obszaru albo fullstack
... implementacja ...
sdd-review-code sf-12       bramka
sdd-done sf-12              manifest dostarczenia
sdd-archive sf-12           przeniesienie do _archive/ po scaleniu
```

Konfiguracja znajduje się w `.sdd/config.yml`.

## Co tu trafia

Te foldery dokumentują, jak zadanie zostało ustalone i dostarczone. Nie są dokumentacją
projektu. To, co przetrwa zadanie — decyzja architektoniczna, kontrakt modułu, reguła
produktowa — trafia do dokumentacji projektu, a artefakty zadania tylko do niej linkują.
