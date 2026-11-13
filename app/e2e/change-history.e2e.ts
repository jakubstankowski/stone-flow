// E-7 (spec F pkt 27.6): zmiana kwoty zamówienia z poprzedniego miesiąca zmienia Pulpit, a lista
// „Historia zmian” (filtr daty biznesowej) pokazuje starą i nową wartość.
import { previousMonthDay } from './support/dates';
import { expect, test } from './support/fixtures';
import { createOrder, dashboardCards, field, rowWith } from './support/ui';

test('Historia zmian wyjaśnia zmianę sprzedaży minionego okresu', async ({ page, tag, today }) => {
  const day = previousMonthDay(today, 14);
  const before = (await dashboardCards(page, 'Sprzedaż', day)).get('Sprzedaż') as number;

  const order = await createOrder(page, { customer: `Klient ${tag}`, date: day, value: '500,00' });
  expect((await dashboardCards(page, 'Sprzedaż', day)).get('Sprzedaż')).toBe(before + 50000);

  await page.goto(`/zamowienia/${order.id}`);
  await page.getByRole('button', { name: 'Edytuj' }).click();
  await field(page, 'Wartość brutto (zł)').fill('650,00');
  await page.getByRole('button', { name: 'Zapisz zmiany' }).click();
  await expect(page.getByRole('button', { name: 'Edytuj' })).toBeVisible();
  expect((await dashboardCards(page, 'Sprzedaż', day)).get('Sprzedaż')).toBe(before + 65000);

  await page.goto('/historia-zmian');
  await expect(page.getByRole('heading', { level: 1, name: 'Historia zmian' })).toBeVisible();
  await field(page, 'Data biznesowa od').fill(day);
  await field(page, 'Data biznesowa do').fill(day);
  const change = rowWith(page, order.number).filter({ hasText: '→' });
  await expect(change).toHaveCount(1);
  await expect(change).toContainText(/Wartość brutto: 500,00\s*zł → 650,00\s*zł/);
});
