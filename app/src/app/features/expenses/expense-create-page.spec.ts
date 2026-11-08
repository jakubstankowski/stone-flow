import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  nextRequest,
  provideTestHttp,
  respondProblem,
  typeInto,
} from '../../../testing/auth-testing';
import { ExpenseCreatePage } from './expense-create-page';

function buttonByText(root: ParentNode, text: string): HTMLButtonElement {
  const button = Array.from(root.querySelectorAll('button')).find(
    (candidate) => candidate.textContent?.trim() === text,
  );
  if (!button) {
    throw new Error(`Brak przycisku „${text}”`);
  }
  return button;
}

describe('ExpenseCreatePage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'wydatki/nowy', component: ExpenseCreatePage },
          { path: 'wydatki/cykliczne/:id', children: [] },
          { path: 'wydatki/:id', children: [] },
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

  async function open(url = '/wydatki/nowy') {
    const harness = await RouterTestingHarness.create();
    document.body.appendChild(harness.fixture.nativeElement);
    await harness.navigateByUrl(url);
    const element = () => harness.routeNativeElement as HTMLElement;
    const settle = async (check: () => void) =>
      vi.waitFor(async () => {
        await harness.fixture.whenStable();
        check();
      });
    return { harness, element, settle };
  }

  async function fillAndSubmit(element: HTMLElement, settle: (check: () => void) => Promise<void>) {
    typeInto(element.querySelector('#wydatek-opis') as HTMLInputElement, 'Cięcie płyt');
    const category = element.querySelector('#wydatek-kategoria') as HTMLSelectElement;
    category.value = 'cutting';
    category.dispatchEvent(new Event('input', { bubbles: true }));
    category.dispatchEvent(new Event('change', { bubbles: true }));
    typeInto(element.querySelector('#wydatek-brutto') as HTMLInputElement, '-200,00');
    await settle(() => expect(element.textContent).toContain('Korekta / zwrot od dostawcy'));
    buttonByText(element, 'Zapisz').click();
  }

  async function createRequest(): Promise<TestRequest> {
    return nextRequest({ method: 'POST', url: '/api/expenses' });
  }

  it('goes to the details after 201 with the signed amount and an idempotency key', async () => {
    const { element, settle } = await open();

    await fillAndSubmit(element(), settle);
    const request = await createRequest();
    expect(request.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect(request.request.body).toEqual(
      expect.objectContaining({ grossAmount: '-200.00', category: 'cutting' }),
    );
    request.flush({ id: 'w7', version: 'v1' }, { status: 201, statusText: 'Created' });

    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/wydatki/w7'));
  });

  it('shows the existing expense after 409 idempotency_key_reused and starts over with a new key', async () => {
    const { element, settle } = await open();

    await fillAndSubmit(element(), settle);
    const first = await createRequest();
    const firstKey = first.request.headers.get('Idempotency-Key');
    respondProblem(first, 409, { code: 'idempotency_key_reused', expenseId: 'w3' });

    await settle(() => expect(element().textContent).toContain('Ten formularz zapisał już'));
    expect(element().querySelector('a[href="/wydatki/w3"]')).not.toBeNull();
    expect(TestBed.inject(Router).url).toBe('/wydatki/nowy');

    buttonByText(element(), 'Dodaj kolejny wydatek').click();
    await settle(() => expect(element().textContent).not.toContain('Ten formularz zapisał już'));
    expect((element().querySelector('#wydatek-opis') as HTMLInputElement).value).toBe('');

    await fillAndSubmit(element(), settle);
    const second = await createRequest();
    expect(second.request.headers.get('Idempotency-Key')).not.toBe(firstKey);
    second.flush({ id: 'w8', version: 'v1' }, { status: 201, statusText: 'Created' });
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/wydatki/w8'));
  });

  it('keeps the key and the data after a validation error', async () => {
    const { element, settle } = await open();

    await fillAndSubmit(element(), settle);
    const first = await createRequest();
    const key = first.request.headers.get('Idempotency-Key');
    respondProblem(first, 400, {
      errors: { netAmount: ['Kwota netto nie może być większa od brutto.'] },
    });
    await settle(() => expect(element().textContent).toContain('Popraw błędy w formularzu'));

    expect((element().querySelector('#wydatek-brutto') as HTMLInputElement).value).toBe('-200,00');
    typeInto(element().querySelector('#wydatek-netto') as HTMLInputElement, '-100,00');
    await settle(() => expect(element().querySelector('#wydatek-netto')).not.toBeNull());
    buttonByText(element(), 'Zapisz').click();
    const second = await createRequest();
    expect(second.request.headers.get('Idempotency-Key')).toBe(key);
    second.flush({ id: 'w9', version: 'v1' }, { status: 201, statusText: 'Created' });
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/wydatki/w9'));
  });

  describe('recurring mode (SF-16)', () => {
    const PREVIEW = {
      occurrencesToCreate: 0,
      firstImmediateDueDate: null,
      beforeLaunchCount: 0,
      nextDueDate: '2026-11-10',
      paused: false,
      droppedDueCount: 0,
      createdAfterEndCount: 0,
      createdBeforeStartCount: 0,
    };

    function toggleOf(element: HTMLElement): HTMLInputElement {
      return Array.from(element.querySelectorAll('input[type="checkbox"]')).find((input) =>
        input.closest('label')?.textContent?.includes('Powtarzaj co miesiąc'),
      ) as HTMLInputElement;
    }

    async function fillDefinition(
      element: HTMLElement,
      settle: (check: () => void) => Promise<void>,
    ) {
      typeInto(element.querySelector('#wydatek-opis') as HTMLInputElement, 'Abonament');
      const category = element.querySelector('#wydatek-kategoria') as HTMLSelectElement;
      category.value = 'marketing';
      category.dispatchEvent(new Event('input', { bubbles: true }));
      category.dispatchEvent(new Event('change', { bubbles: true }));
      typeInto(element.querySelector('#wydatek-brutto') as HTMLInputElement, '123,00');
      typeInto(element.querySelector('#wydatek-dzien') as HTMLInputElement, '10');
      typeInto(element.querySelector('#wydatek-poczatek') as HTMLInputElement, '2026-09-01');
      await settle(() => expect(element.querySelector('#wydatek-dzien')).not.toBeNull());
    }

    it('opens checked from ?cykliczny=1, hides one-off dates and shows the schedule', async () => {
      const { element } = await open('/wydatki/nowy?cykliczny=1');
      const toggle = toggleOf(element());

      expect(toggle.checked).toBe(true);
      expect(toggle.getAttribute('aria-expanded')).toBe('true');
      expect(toggle.getAttribute('aria-controls')).toBe('wydatek-harmonogram');
      expect(element().querySelector('#wydatek-harmonogram')).not.toBeNull();
      expect(element().querySelector('#wydatek-data-kosztu')).toBeNull();
      expect(element().querySelector('#wydatek-dokument')).toBeNull();
      expect(element().textContent).toContain('Kwota większa od 0 zł.');
      expect(element().textContent).toContain(
        'Dla dni 29–31 w krótszym miesiącu wystąpienie przypada na ostatni dzień miesiąca.',
      );
      expect((element().querySelector('#wydatek-poczatek') as HTMLInputElement).value).toMatch(
        /^\d{4}-\d{2}-\d{2}$/,
      );
    });

    it('unchecking restores the one-off layout with the entered dates', async () => {
      const { element, settle } = await open();
      typeInto(element().querySelector('#wydatek-termin') as HTMLInputElement, '2026-11-20');
      expect(toggleOf(element()).getAttribute('aria-expanded')).toBe('false');

      toggleOf(element()).click();
      await settle(() => expect(element().querySelector('#wydatek-termin')).toBeNull());
      expect(element().querySelector('#wydatek-dzien')).not.toBeNull();
      toggleOf(element()).click();
      await settle(() => expect(element().querySelector('#wydatek-termin')).not.toBeNull());
      expect((element().querySelector('#wydatek-termin') as HTMLInputElement).value).toBe(
        '2026-11-20',
      );
    });

    it('previews the schedule after a pause and saves with its own idempotency key', async () => {
      const { element, settle } = await open('/wydatki/nowy?cykliczny=1');

      await fillDefinition(element(), settle);
      const preview = await nextRequest((r) => r.url === '/api/recurring-expenses/preview');
      expect(preview.request.params.get('dayOfMonth')).toBe('10');
      expect(preview.request.params.get('startsOn')).toBe('2026-09-01');
      expect(preview.request.params.has('definitionId')).toBe(false);
      preview.flush({ ...PREVIEW, occurrencesToCreate: 2, firstImmediateDueDate: '2026-09-10' });
      await settle(() =>
        expect(element().textContent).toContain(
          'Po zapisie od razu powstaną 2 wystąpienia za terminy od 10.09.2026 do dzisiaj.',
        ),
      );

      buttonByText(element(), 'Zapisz').click();
      const request = await nextRequest({ method: 'POST', url: '/api/recurring-expenses' });
      expect(request.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
      expect(request.request.body).toEqual(
        expect.objectContaining({
          description: 'Abonament',
          grossAmount: '123.00',
          dayOfMonth: 10,
          startsOn: '2026-09-01',
          endsOn: null,
        }),
      );
      expect(request.request.body.costDate).toBeUndefined();
      request.flush(
        { id: 'c5', version: 'v1', createdOccurrences: 2 },
        { status: 201, statusText: 'Created' },
      );
      await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/wydatki/cykliczne/c5'));
    });

    it('rejects a non-positive gross amount in the UI', async () => {
      const { element, settle } = await open('/wydatki/nowy?cykliczny=1');

      await fillDefinition(element(), settle);
      typeInto(element().querySelector('#wydatek-brutto') as HTMLInputElement, '-5,00');
      buttonByText(element(), 'Zapisz').click();
      await settle(() =>
        expect(element().textContent).toContain(
          'Kwota wydatku cyklicznego musi być większa od 0 zł.',
        ),
      );
      (await nextRequest((r) => r.url === '/api/recurring-expenses/preview')).flush(PREVIEW);
      http.expectNone({ method: 'POST', url: '/api/recurring-expenses' });
    });

    it('shows the retry message after 503 and repeats with the same key', async () => {
      const { element, settle } = await open('/wydatki/nowy?cykliczny=1');

      await fillDefinition(element(), settle);
      respondProblem(await nextRequest((r) => r.url === '/api/recurring-expenses/preview'), 503);
      await settle(() =>
        expect(element().textContent).toContain('Nie udało się przygotować podglądu.'),
      );
      buttonByText(element(), 'Zapisz').click();
      const first = await nextRequest({ method: 'POST', url: '/api/recurring-expenses' });
      const key = first.request.headers.get('Idempotency-Key');
      respondProblem(first, 503, { code: 'recurring_expense_busy' });
      await settle(() =>
        expect(element().textContent).toContain('Nie udało się zapisać. Spróbuj ponownie.'),
      );

      buttonByText(element(), 'Spróbuj ponownie').click();
      const second = await nextRequest({ method: 'POST', url: '/api/recurring-expenses' });
      expect(second.request.headers.get('Idempotency-Key')).toBe(key);
      second.flush(
        { id: 'c6', version: 'v1', createdOccurrences: 0 },
        { status: 201, statusText: 'Created' },
      );
      await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/wydatki/cykliczne/c6'));
    });

    it('retries with the current form data and blocks a second retry while saving', async () => {
      const { element, settle } = await open('/wydatki/nowy?cykliczny=1');

      await fillDefinition(element(), settle);
      (await nextRequest((r) => r.url === '/api/recurring-expenses/preview')).flush(PREVIEW);
      buttonByText(element(), 'Zapisz').click();
      const first = await nextRequest({ method: 'POST', url: '/api/recurring-expenses' });
      const key = first.request.headers.get('Idempotency-Key');
      respondProblem(first, 503, { code: 'recurring_expense_busy' });
      await settle(() =>
        expect(element().textContent).toContain('Nie udało się zapisać. Spróbuj ponownie.'),
      );

      typeInto(element().querySelector('#wydatek-brutto') as HTMLInputElement, '150,00');
      buttonByText(element(), 'Spróbuj ponownie').click();
      const second = await nextRequest({ method: 'POST', url: '/api/recurring-expenses' });
      expect(second.request.headers.get('Idempotency-Key')).toBe(key);
      expect(second.request.body).toEqual(expect.objectContaining({ grossAmount: '150.00' }));

      buttonByText(element(), 'Spróbuj ponownie').click();
      await settle(() => undefined);
      http.expectNone({ method: 'POST', url: '/api/recurring-expenses' });

      second.flush(
        { id: 'c7', version: 'v1', createdOccurrences: 0 },
        { status: 201, statusText: 'Created' },
      );
      await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/wydatki/cykliczne/c7'));
    });

    it('explains a deleted definition saved by this form', async () => {
      const { element, settle } = await open('/wydatki/nowy?cykliczny=1');

      await fillDefinition(element(), settle);
      (await nextRequest((r) => r.url === '/api/recurring-expenses/preview')).flush(PREVIEW);
      buttonByText(element(), 'Zapisz').click();
      respondProblem(await nextRequest({ method: 'POST', url: '/api/recurring-expenses' }), 409, {
        code: 'recurring_expense_deleted',
      });
      await settle(() =>
        expect(element().textContent).toContain(
          'Ten formularz zapisał już wydatek cykliczny, który został później usunięty.',
        ),
      );
    });
  });
});
