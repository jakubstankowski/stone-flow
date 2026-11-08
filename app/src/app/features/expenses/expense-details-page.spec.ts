import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  nextRequest,
  provideTestHttp,
  respondNoContent,
  respondProblem,
} from '../../../testing/auth-testing';
import { expenseDetails } from '../../../testing/expenses-testing';
import { ExpenseDetailsPage } from './expense-details-page';
import { ExpenseDetails } from './expense-models';

function buttonByText(root: ParentNode, text: string): HTMLButtonElement {
  const button = Array.from(root.querySelectorAll('button')).find(
    (candidate) => candidate.textContent?.trim() === text,
  );
  if (!button) {
    throw new Error(`Brak przycisku „${text}”`);
  }
  return button;
}

function dialogWith(root: ParentNode, heading: string): HTMLDialogElement {
  const dialog = Array.from(root.querySelectorAll('dialog')).find((candidate) =>
    candidate.querySelector('h2')?.textContent?.includes(heading),
  );
  if (!dialog) {
    throw new Error(`Brak dialogu „${heading}”`);
  }
  return dialog;
}

describe('ExpenseDetailsPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'wydatki/:id', component: ExpenseDetailsPage },
          { path: 'wydatki', children: [] },
        ]),
        provideTestHttp(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    document.body.innerHTML = '';
  });

  async function open(expense: ExpenseDetails = expenseDetails()) {
    const harness = await RouterTestingHarness.create();
    document.body.appendChild(harness.fixture.nativeElement);
    const navigation = harness.navigateByUrl('/wydatki/w1');
    const element = () => harness.routeNativeElement as HTMLElement;
    const settle = async (check: () => void) =>
      vi.waitFor(async () => {
        await harness.fixture.whenStable();
        check();
      });
    (await nextRequest({ method: 'GET', url: '/api/expenses/w1' })).flush(expense);
    await navigation;
    await settle(() => expect(element().querySelector('.skeleton')).toBeNull());
    return { element, settle };
  }

  it('shows a planned expense with separate date labels and the paid action', async () => {
    const { element } = await open(expenseDetails({ overdue: true }));
    const text = element().textContent ?? '';

    expect(element().querySelector('h1')?.textContent?.trim()).toBe('Cięcie płyt');
    expect(text).toContain('Planowany');
    expect(text).toContain('Po terminie');
    expect(text).toContain('Data kosztu');
    expect(text).toContain('Termin płatności');
    expect(text).toContain('Data zapłaty');
    expect(() => buttonByText(element(), 'Oznacz jako opłacony')).not.toThrow();
    expect(() => buttonByText(element(), 'Cofnij oznaczenie zapłaty')).toThrow();
  });

  it('marks the expense as paid with today and the current method', async () => {
    const { element, settle } = await open();

    buttonByText(element(), 'Oznacz jako opłacony').click();
    await settle(() => expect(element().textContent).toContain('Wydatek trafi do wypływów'));
    const submit = Array.from(element().querySelectorAll('form button[type="submit"]')).find(
      (button) => button.textContent?.trim() === 'Oznacz jako opłacony',
    ) as HTMLButtonElement;
    submit.click();

    const request = await nextRequest('/api/expenses/w1/mark-paid');
    expect(request.request.body).toEqual(
      expect.objectContaining({ paymentMethod: 'transfer', version: 'AAAAAAAAB9E=' }),
    );
    expect(request.request.body.paidOn).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    request.flush({ version: 'v2' });
    (await nextRequest({ method: 'GET', url: '/api/expenses/w1' })).flush(
      expenseDetails({ paidOn: '2026-11-05', paymentStatus: 'paid', version: 'v2' }),
    );
    await settle(() => expect(element().textContent).toContain('Wydatek oznaczony jako opłacony.'));
    expect(element().textContent).toContain('Opłacony');
  });

  it('asks before unmarking and sends nothing when dismissed', async () => {
    const { element, settle } = await open(
      expenseDetails({ paidOn: '2026-11-03', paymentStatus: 'paid' }),
    );
    const dialog = () => dialogWith(element(), 'Cofnąć oznaczenie zapłaty');

    buttonByText(element(), 'Cofnij oznaczenie zapłaty').click();
    await settle(() => expect(dialog().open).toBe(true));
    expect(dialog().textContent).toContain('przestanie liczyć się do wypływów z 03.11.2026');
    buttonByText(dialog(), 'Anuluj').click();
    await settle(() => expect(dialog().open).toBe(false));
    http.expectNone('/api/expenses/w1/unmark-paid');

    buttonByText(element(), 'Cofnij oznaczenie zapłaty').click();
    await settle(() => expect(dialog().open).toBe(true));
    buttonByText(dialog(), 'Cofnij oznaczenie').click();
    const request = await nextRequest('/api/expenses/w1/unmark-paid');
    expect(request.request.body).toEqual({ version: 'AAAAAAAAB9E=' });
    request.flush({ version: 'v2' });
    (await nextRequest({ method: 'GET', url: '/api/expenses/w1' })).flush(expenseDetails());
    await settle(() => expect(element().textContent).toContain('Cofnięto oznaczenie zapłaty.'));
  });

  it('deletes after a neutral confirmation and returns to the list', async () => {
    const { element, settle } = await open();
    const dialog = () => dialogWith(element(), 'Usunąć wydatek');

    buttonByText(element(), 'Usuń').click();
    await settle(() => expect(dialog().open).toBe(true));
    expect(dialog().textContent).toContain('zostanie usunięty z ewidencji i raportów');
    expect(dialog().textContent).toContain('Ślad usunięcia zostanie w historii zmian');
    buttonByText(dialog(), 'Usuń').click();

    const request = await nextRequest({ method: 'DELETE', url: '/api/expenses/w1' });
    expect(request.request.body).toEqual({ version: 'AAAAAAAAB9E=' });
    respondNoContent(request);
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/wydatki'));
  });

  it('shows the conflict with a reload action and keeps the form', async () => {
    const { element, settle } = await open();

    buttonByText(element(), 'Edytuj').click();
    await settle(() => expect(element().querySelector('form')).not.toBeNull());
    buttonByText(element(), 'Zapisz zmiany').click();
    respondProblem(await nextRequest({ method: 'PUT', url: '/api/expenses/w1' }), 409, {
      code: 'expense_conflict',
    });

    await settle(() => expect(element().textContent).toContain('Wydatek został zmieniony'));
    expect(() => buttonByText(element(), 'Wczytaj ponownie')).not.toThrow();
    expect(element().querySelector('form')).not.toBeNull();
  });

  it('confirms clearing the payment date in the form before saving', async () => {
    const { element, settle } = await open(
      expenseDetails({ paidOn: '2026-11-03', paymentStatus: 'paid' }),
    );
    const dialog = () => dialogWith(element(), 'Cofnąć oznaczenie zapłaty');

    buttonByText(element(), 'Edytuj').click();
    await settle(() => expect(element().querySelector('#wydatek-data-zaplaty')).not.toBeNull());
    const paidOn = element().querySelector('#wydatek-data-zaplaty') as HTMLInputElement;
    paidOn.value = '';
    paidOn.dispatchEvent(new Event('input', { bubbles: true }));
    buttonByText(element(), 'Zapisz zmiany').click();

    await settle(() => expect(dialog().open).toBe(true));
    buttonByText(dialog(), 'Cofnij oznaczenie').click();
    const request = await nextRequest({ method: 'PUT', url: '/api/expenses/w1' });
    expect(request.request.body.paidOn).toBeNull();
    request.flush({ version: 'v2' });
    (await nextRequest({ method: 'GET', url: '/api/expenses/w1' })).flush(expenseDetails());
    await settle(() => expect(element().textContent).toContain('Zmiany zapisane.'));
  });

  it('shows a not-found result of an action only once', async () => {
    const { element, settle } = await open();

    buttonByText(element(), 'Oznacz jako opłacony').click();
    await settle(() => expect(element().textContent).toContain('Wydatek trafi do wypływów'));
    const submit = Array.from(element().querySelectorAll('form button[type="submit"]')).find(
      (button) => button.textContent?.trim() === 'Oznacz jako opłacony',
    ) as HTMLButtonElement;
    submit.click();
    respondProblem(await nextRequest('/api/expenses/w1/mark-paid'), 404, {
      code: 'expense_not_found',
      detail: 'Nie znaleziono wydatku. Mógł zostać usunięty.',
    });

    await settle(() => expect(element().textContent).toContain('Nie znaleziono wydatku'));
    const occurrences = (element().textContent ?? '').split('Nie znaleziono wydatku').length - 1;
    expect(occurrences).toBe(1);
  });

  it('shows the occurrence period with a link and the extra sentence in the delete dialog', async () => {
    const { element, settle } = await open(
      expenseDetails({
        recurring: { definitionId: 'c1', period: '2026-10', definitionExists: true },
      }),
    );
    const info = element().querySelector('[data-testid="wystapienie-cykliczne"]');

    expect(info?.textContent).toContain('Wystąpienie wydatku cyklicznego za październik 2026');
    const link = Array.from(element().querySelectorAll('a')).find(
      (a) => a.textContent?.trim() === 'Przejdź do wydatku cyklicznego',
    );
    expect(link?.getAttribute('href')).toBe('/wydatki/cykliczne/c1');

    const dialog = () => dialogWith(element(), 'Usunąć wydatek');
    buttonByText(element(), 'Usuń').click();
    await settle(() => expect(dialog().open).toBe(true));
    expect(dialog().textContent).toContain(
      'To wystąpienie wydatku cyklicznego nie zostanie utworzone ponownie. W razie pomyłki dodaj wydatek ręcznie.',
    );
    buttonByText(dialog(), 'Anuluj').click();
  });

  it('says when the definition of an occurrence was deleted', async () => {
    const { element } = await open(
      expenseDetails({
        recurring: { definitionId: 'c1', period: '2026-10', definitionExists: false },
      }),
    );

    expect(element().textContent).toContain('Wydatek cykliczny został usunięty.');
    expect(element().textContent).not.toContain('Przejdź do wydatku cyklicznego');
  });

  it('shows no occurrence information for a one-off expense', async () => {
    const { element, settle } = await open();

    expect(element().querySelector('[data-testid="wystapienie-cykliczne"]')).toBeNull();
    const dialog = () => dialogWith(element(), 'Usunąć wydatek');
    buttonByText(element(), 'Usuń').click();
    await settle(() => expect(dialog().open).toBe(true));
    expect(dialog().textContent).not.toContain('wystąpienie wydatku cyklicznego');
    buttonByText(dialog(), 'Anuluj').click();
  });
});
