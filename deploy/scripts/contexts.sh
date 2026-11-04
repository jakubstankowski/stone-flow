# shellcheck shell=bash
# Konteksty EF Core w kolejności migracji: "<projekt Infrastructure>|<kontekst>".
# Kolejność jest stała i nie wyraża zależności między modułami (każdy ma własny schemat).
# Nowy moduł z bazą danych dopisuje się na końcu.
STONEFLOW_STARTUP_PROJECT="api/src/StoneFlow.Api"
STONEFLOW_CONTEXTS=(
  "api/src/Modules/Access/StoneFlow.Access.Infrastructure|AccessDbContext"
  "api/src/Modules/Orders/StoneFlow.Orders.Infrastructure|OrdersDbContext"
  "api/src/Modules/Expenses/StoneFlow.Expenses.Infrastructure|ExpensesDbContext"
  "api/src/Modules/Reporting/StoneFlow.Reporting.Infrastructure|ReportingDbContext"
  "api/src/Modules/Integrations/StoneFlow.Integrations.Infrastructure|IntegrationsDbContext"
)
