// Daty biznesowe E2E w strefie Europe/Warsaw jako `yyyy-MM-dd`. „Dzisiaj” liczy się raz na
// przebieg (global setup → STONEFLOW_E2E_TODAY), więc przebieg o północy nie zmienia okresów.

const warsawDate = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Europe/Warsaw',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
});

export function warsawToday(now: Date = new Date()): string {
  return warsawDate.format(now);
}

function parts(date: string): { year: number; month: number } {
  const match = /^(\d{4})-(\d{2})-\d{2}$/.exec(date);
  if (!match) {
    throw new Error(`Nieprawidłowa data: ${date}`);
  }
  return { year: Number(match[1]), month: Number(match[2]) };
}

/** Pierwszy dzień miesiąca przesuniętego o `offset` miesięcy względem `date`. */
function firstOfMonth(date: string, offset: number): { year: number; month: number } {
  const { year, month } = parts(date);
  const index = year * 12 + (month - 1) + offset;
  return { year: Math.floor(index / 12), month: (index % 12) + 1 };
}

function iso(year: number, month: number, day: number): string {
  return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

/** Data uruchomienia E2E: 1. dzień miesiąca sprzed dwóch miesięcy. */
export function launchDateFor(today: string): string {
  const { year, month } = firstOfMonth(today, -2);
  return iso(year, month, 1);
}

/** Dzień `day` poprzedniego miesiąca (testy kwot mają własne, rozłączne dni). */
export function previousMonthDay(today: string, day: number): string {
  const { year, month } = firstOfMonth(today, -1);
  return iso(year, month, day);
}

/** 1. dzień bieżącego miesiąca. */
export function currentMonthFirst(today: string): string {
  const { year, month } = firstOfMonth(today, 0);
  return iso(year, month, 1);
}

/** `yyyy-MM-dd` → `dd.MM.yyyy` (format dat w interfejsie). */
export function plDate(date: string): string {
  const [year, month, day] = date.split('-');
  return `${day}.${month}.${year}`;
}
