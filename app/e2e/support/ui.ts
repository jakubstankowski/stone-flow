// Kroki procesów wykonywane przez interfejs (etykiety pól i przycisków jak widzi je użytkownik).
import { expect, Locator, Page } from '@playwright/test';
import { plnToGrosze } from './money';

export interface CreatedOrder {
  readonly id: string;
  readonly number: string;
}

/** Pole formularza po początku etykiety (etykiety wymaganych pól kończą się „(wymagane)”). */
export function field(scope: Page | Locator, label: string): Locator {
  const control = scope.locator('input, select, textarea');
  return scope.getByLabel(new RegExp(`^\\s*${escape(label)}(\\s|$)`)).and(control);
}

function escape(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

export interface OrderInput {
  readonly customer: string;
  readonly date: string;
  readonly value: string;
}

/** Wypełnia formularz nowego zamówienia ręcznego (bez zapisu). */
export async function fillNewOrder(page: Page, order: OrderInput): Promise<void> {
  await page.goto('/zamowienia/nowe');
  await expect(page.getByRole('heading', { level: 1, name: 'Dodaj zamówienie' })).toBeVisible();
  await field(page, 'Imię i nazwisko lub nazwa firmy').fill(order.customer);
  await field(page, 'Data zamówienia').fill(order.date);
  await field(page, 'Kanał').selectOption({ label: 'OLX' });
  await field(page, 'Wartość brutto (zł)').fill(order.value);
}

/** Szczegóły zamówienia po zapisie: id z adresu i numer z nagłówka. */
export async function openedOrder(page: Page): Promise<CreatedOrder> {
  await expect(page).toHaveURL(/\/zamowienia\/[0-9a-f-]{36}$/);
  const heading = page.getByRole('heading', { level: 1, name: /^Zamówienie \S+/ });
  await expect(heading).toBeVisible();
  const number = (await heading.innerText()).replace(/^Zamówienie\s+/, '').trim();
  const id = page.url().split('/').pop() as string;
  return { id, number };
}

export async function createOrder(page: Page, order: OrderInput): Promise<CreatedOrder> {
  await fillNewOrder(page, order);
  await page.getByRole('button', { name: 'Zapisz', exact: true }).click();
  return openedOrder(page);
}

/** Wpłata w szczegółach otwartego zamówienia. */
export async function addPayment(page: Page, amount: string, date: string): Promise<void> {
  await page.getByRole('button', { name: 'Dodaj wpłatę' }).click();
  await field(page, 'Kwota (zł)').fill(amount);
  await field(page, 'Data wpłaty').fill(date);
  await field(page, 'Metoda').selectOption({ label: 'Przelew' });
  await page.getByRole('button', { name: 'Zapisz wpłatę' }).click();
  await expect(page.getByRole('button', { name: 'Zapisz wpłatę' })).toBeHidden();
  await expect(
    page.getByRole('row').filter({ hasText: date.split('-').reverse().join('.') }),
  ).not.toHaveCount(0);
}

export interface ExpenseInput {
  readonly description: string;
  readonly amount: string;
  readonly costDate: string;
}

/** Wydatek jednorazowy bez daty zapłaty; zwraca id z adresu szczegółów. */
export async function createExpense(page: Page, expense: ExpenseInput): Promise<string> {
  await page.goto('/wydatki/nowy');
  await expect(page.getByRole('heading', { level: 1, name: 'Dodaj wydatek' })).toBeVisible();
  await field(page, 'Opis').fill(expense.description);
  await field(page, 'Kategoria').selectOption({ label: 'Cięcie' });
  await field(page, 'Kwota brutto (zł)').fill(expense.amount);
  await field(page, 'Data kosztu').fill(expense.costDate);
  await page.getByRole('button', { name: 'Zapisz', exact: true }).click();
  await expect(page).toHaveURL(/\/wydatki\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('heading', { level: 1, name: expense.description })).toBeVisible();
  return page.url().split('/').pop() as string;
}

/**
 * Pulpit z własnym zakresem jednego dnia w wybranym widoku; czeka na odpowiedź raportu dla tego
 * zakresu i zwraca wartości kart jako grosze, po podpisie karty.
 */
export async function dashboardCards(
  page: Page,
  view: 'Sprzedaż' | 'Przepływy pieniężne',
  day: string,
): Promise<Map<string, number>> {
  const endpoint = view === 'Sprzedaż' ? '/api/reports/sales?' : '/api/reports/cash-flow?';
  await page.goto('/pulpit');
  await expect(page.getByRole('heading', { level: 1, name: 'Pulpit' })).toBeVisible();
  await page.getByRole('radio', { name: view }).check();
  await field(page, 'Okres').selectOption({ label: 'Własny zakres' });
  await field(page, 'Data od').fill(day);
  const report = page.waitForResponse(
    (response) =>
      response.url().includes(endpoint) &&
      response.url().includes(`from=${day}`) &&
      response.url().includes(`to=${day}`) &&
      response.ok(),
  );
  await field(page, 'Data do').fill(day);
  await report;
  const cards = page.locator('[data-kpi]');
  await expect(cards.first().getByTestId('wartosc')).toHaveText(/zł|\d/);
  const values = new Map<string, number>();
  for (const card of await cards.all()) {
    const label = (await card.locator('.font-medium').first().innerText()).trim();
    const value = (await card.getByTestId('wartosc').innerText()).trim();
    values.set(label, /zł/.test(value) ? plnToGrosze(value) : Number.NaN);
  }
  return values;
}

/** Wiersz tabeli zawierający tekst. */
export function rowWith(page: Page, text: string | RegExp): Locator {
  return page.getByRole('row').filter({ hasText: text });
}
