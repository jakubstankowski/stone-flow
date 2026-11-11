// `test` z danymi przebiegu: unikalny znacznik danych testu i „dzisiaj” w Europe/Warsaw.
import { test as base } from '@playwright/test';
import { uniqueTag } from './tag';

export const test = base.extend<{ tag: string; today: string }>({
  // eslint-disable-next-line no-empty-pattern
  tag: async ({}, use) => use(uniqueTag()),
  // eslint-disable-next-line no-empty-pattern
  today: async ({}, use) => {
    const today = process.env['STONEFLOW_E2E_TODAY'];
    if (!today) {
      throw new Error('Brak STONEFLOW_E2E_TODAY — global setup nie został uruchomiony.');
    }
    await use(today);
  },
});

export { expect } from '@playwright/test';
