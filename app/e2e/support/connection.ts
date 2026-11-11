// Connection string do bazy E2E: usuwa Database / Initial Catalog z połączenia do serwera i
// dopisuje wskazaną bazę (ta sama logika co connection_for w deploy/scripts/sql-common.sh).

export function databaseConnection(server: string, database: string): string {
  const parts = server.split(';').filter((part) => {
    const key = part.split('=')[0].replace(/\s/g, '').toLowerCase();
    return key !== '' && key !== 'database' && key !== 'initialcatalog';
  });
  return [...parts, `Database=${database}`].join(';');
}
