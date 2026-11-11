// E-5 (spec F pkt 27.5): karty „Sprzedaż” i „Wpływy” rosną o dane testu (własny zakres jednego
// dnia poprzedniego miesiąca, asercje na różnicach), kwoty w formacie polskim, a widoki różnią
// się podpisami kart (design guide §10).
import { previousMonthDay } from './support/dates';
import { expect, test } from './support/fixtures';
import { PLN_FORMAT } from './support/money';
import { addPayment, createOrder, dashboardCards } from './support/ui';

const SALES_LABELS = ['Sprzedaż', 'Koszty', 'Wynik przed wypłatami', 'Liczba zamówień'];
const CASH_LABELS = ['Wpływy', 'Wypływy', 'Zmiana środków'];

test('Pulpit: sprzedaż i wpływy z danych testu, różne podpisy widoków', async ({
  page,
  tag,
  today,
}) => {
  const day = previousMonthDay(today, 12);
  const salesBefore = await dashboardCards(page, 'Sprzedaż', day);
  const cashBefore = await dashboardCards(page, 'Przepływy pieniężne', day);

  await createOrder(page, { customer: `Klient ${tag}`, date: day, value: '321,00' });
  await addPayment(page, '21,00', day);

  const sales = await dashboardCards(page, 'Sprzedaż', day);
  expect([...sales.keys()]).toEqual(SALES_LABELS);
  expect(sales.get('Sprzedaż')).toBe((salesBefore.get('Sprzedaż') as number) + 32100);
  for (const value of await page.locator('[data-kpi]').getByTestId('wartosc').allInnerTexts()) {
    expect(value.trim()).toMatch(/^\d+$|zł$/);
    if (value.includes('zł')) {
      expect(value.trim()).toMatch(PLN_FORMAT);
    }
  }

  const cash = await dashboardCards(page, 'Przepływy pieniężne', day);
  expect([...cash.keys()]).toEqual(CASH_LABELS);
  expect(cash.get('Wpływy')).toBe((cashBefore.get('Wpływy') as number) + 2100);
  expect([...cash.keys()].some((label) => SALES_LABELS.includes(label))).toBe(false);
});
