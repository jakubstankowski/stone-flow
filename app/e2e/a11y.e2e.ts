// E-9 (spec F pkt 29, 31): axe-core (WCAG 2.2 AA) bez naruszeń i brak poziomego przewijania przy
// 375 px na stronach w powłoce, w obu widokach Pulpitu i z otwartym dialogiem potwierdzenia.
// Jeden test na stronę, żeby raport wskazywał stronę. Bez wyłączeń reguł.
import { Page } from '@playwright/test';
import { expectNoAxeViolations, expectNoHorizontalScroll } from './support/a11y';
import { expect, test } from './support/fixtures';
import { createExpense, createOrder } from './support/ui';

async function loaded(page: Page): Promise<void> {
  await expect(page.locator('h1').first()).toBeVisible();
  await expect(page.locator('app-loading-state')).toHaveCount(0);
}

async function check(page: Page): Promise<void> {
  await loaded(page);
  await expectNoAxeViolations(page);
  await expectNoHorizontalScroll(page);
  await loaded(page);
  await expectNoAxeViolations(page);
}

const PAGES = [
  { name: 'Pulpit — Sprzedaż', path: '/pulpit' },
  { name: 'Zamówienia', path: '/zamowienia' },
  { name: 'Nowe zamówienie', path: '/zamowienia/nowe' },
  { name: 'Wydatki', path: '/wydatki' },
  { name: 'Nowy wydatek', path: '/wydatki/nowy' },
  { name: 'Wydatki cykliczne', path: '/wydatki/cykliczne' },
  { name: 'Operacje', path: '/operacje' },
  { name: 'Nowa wypłata', path: '/operacje/wyplaty/nowa' },
  { name: 'Historia zmian', path: '/historia-zmian' },
  { name: 'Integracja', path: '/integracja' },
];

for (const { name, path } of PAGES) {
  test(`dostępność i reflow: ${name}`, async ({ page }) => {
    await page.goto(path);
    await check(page);
  });
}

test('dostępność i reflow: Pulpit — Przepływy pieniężne', async ({ page }) => {
  await page.goto('/pulpit');
  await loaded(page);
  await page.getByRole('radio', { name: 'Przepływy pieniężne' }).check();
  await expect(page.locator('[data-kpi="inflows"]')).toBeVisible();
  await check(page);
});

test('dostępność i reflow: szczegóły zamówienia', async ({ page, tag, today }) => {
  await createOrder(page, { customer: `Klient ${tag}`, date: today, value: '10,00' });
  await check(page);
});

test('dostępność i reflow: szczegóły wydatku i dialog usunięcia', async ({ page, tag, today }) => {
  const description = `Wydatek ${tag}`;
  await createExpense(page, { description, amount: '10,00', costDate: today });
  await check(page);

  await page.setViewportSize({ width: 1280, height: 720 });
  await page.getByRole('button', { name: 'Usuń', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Usunąć wydatek?' });
  await expect(dialog).toBeVisible();
  await expectNoAxeViolations(page);
  await expectNoHorizontalScroll(page);
  await dialog.getByRole('button', { name: 'Anuluj' }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByRole('heading', { level: 1, name: description })).toBeVisible();
});
