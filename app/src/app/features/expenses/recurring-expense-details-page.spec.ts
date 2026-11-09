import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  nextRequest,
  provideTestHttp,
  respondNoContent,
  respondProblem,
  typeInto,
} from '../../../testing/auth-testing';
import { recurringDetails, recurringPreview } from '../../../testing/expenses-testing';
import { RecurringExpenseDetailsPage } from './recurring-expense-details-page';
import { RecurringExpenseDetails } from './recurring-expense-models';

function buttonByText(root: ParentNode, text: string): HTMLButtonElement {
  const button = Array.from(root.querySelectorAll('button')).find(
    (candidate) => candidate.textContent?.trim() === text,
  );
  if (!button) {
    throw new Error(`Brak przycisku „${text}”`);
  }
  return button;
}

/** Przycisk akcji strony (poza dialogami, które mają przyciski o tych samych nazwach). */
function pageButton(root: ParentNode, text: string): HTMLButtonElement {
  const button = Array.from(root.querySelectorAll('button')).find(
    (candidate) => candidate.textContent?.trim() === text && !candidate.closest('dialog'),
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

const URL = '/api/recurring-expenses/c1';

describe('RecurringExpenseDetailsPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'wydatki/cykliczne/:id', component: RecurringExpenseDetailsPage },
          { path: 'wydatki/cykliczne', children: [] },
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

  async function open(definition: RecurringExpenseDetails = recurringDetails()) {
    const harness = await RouterTestingHarness.create();
    document.body.appendChild(harness.fixture.nativeElement);
    const navigation = harness.navigateByUrl('/wydatki/cykliczne/c1');
    const element = () => harness.routeNativeElement as HTMLElement;
    const settle = async (check: () => void) =>
      vi.waitFor(async () => {
        await harness.fixture.whenStable();
        check();
      });
    (await nextRequest({ method: 'GET', url: URL })).flush(definition);
    await navigation;
    await settle(() => expect(element().querySelector('.skeleton')).toBeNull());
    return { element, settle };
  }

  it('shows an active definition with the schedule, next due date and occurrences link', async () => {
    const { element } = await open(recurringDetails({ dayOfMonth: 30 }));
    const text = element().textContent ?? '';

    expect(element().querySelector('h1')?.textContent?.trim()).toBe('Abonament reklamowy');
    expect(text).toContain('Aktywny');
    expect(text).toContain('30. (w krótszym miesiącu ostatni dzień)');
    expect(text).toContain('Bez końca');
    expect(element().querySelector('[data-testid="najblizszy-termin"]')?.textContent).toContain(
      'Następne wystąpienie: 10.12.2099. Powstanie tego dnia jako wydatek planowany',
    );
    expect(text).toContain('Utworzone wystąpienia: 2');
    const link = Array.from(element().querySelectorAll('a')).find(
      (a) => a.textContent?.trim() === 'Pokaż wystąpienia',
    );
    expect(link?.getAttribute('href')).toBe('/wydatki?wydatekCykliczny=c1');
    expect(() => pageButton(element(), 'Wstrzymaj')).not.toThrow();
    expect(() => pageButton(element(), 'Wznów')).toThrow();
  });

  it('says a passed due date will be created by the job, before the ended state', async () => {
    const { element } = await open(
      recurringDetails({ status: 'ended', nextDueDate: '2026-01-10', endsOn: '2026-01-31' }),
    );

    expect(element().querySelector('[data-testid="najblizszy-termin"]')?.textContent).toContain(
      'Termin 10.01.2026 już minął, a wystąpienie jeszcze nie powstało — utworzy je zadanie automatyczne.',
    );
    expect(() => pageButton(element(), 'Wstrzymaj')).toThrow();
    expect(() => pageButton(element(), 'Wznów')).toThrow();
  });

  it('pauses after the confirmation and shows the notice', async () => {
    const { element, settle } = await open();
    const dialog = () => dialogWith(element(), 'Wstrzymać wydatek cykliczny?');

    pageButton(element(), 'Wstrzymaj').click();
    await settle(() => expect(dialog().open).toBe(true));
    expect(dialog().textContent).toContain(
      'Do czasu wznowienia nie powstaną nowe wystąpienia. Okresy z terminem w czasie wstrzymania pozostaną puste — wznowienie ich nie uzupełni.',
    );
    buttonByText(dialog(), 'Wstrzymaj').click();

    const request = await nextRequest(`${URL}/pause`);
    expect(request.request.body).toEqual({ version: 'AAAAAAAAC1E=' });
    request.flush({ version: 'v2', createdOccurrences: 0 });
    (await nextRequest({ method: 'GET', url: URL })).flush(
      recurringDetails({
        status: 'paused',
        pausedOn: '2026-11-08',
        nextDueDate: null,
        version: 'v2',
        resumePreview: { occurrencesToCreate: 0, nextDueDate: '2026-12-10' },
      }),
    );
    await settle(() => expect(element().textContent).toContain('Wstrzymano wydatek cykliczny.'));
    expect(element().textContent).toContain(
      'Wstrzymany od 08.11.2026. Okresy z terminem w czasie wstrzymania pozostaną puste.',
    );
    expect(element().querySelector('[data-testid="najblizszy-termin"]')?.textContent).toContain(
      'Brak — wydatek cykliczny jest wstrzymany',
    );
  });

  it('builds the resume dialog from resumePreview with occurrencesToCreate 1', async () => {
    const { element, settle } = await open(
      recurringDetails({
        status: 'paused',
        pausedOn: '2026-10-05',
        nextDueDate: null,
        resumePreview: { occurrencesToCreate: 1, nextDueDate: '2026-12-10' },
      }),
    );
    const dialog = () => dialogWith(element(), 'Wznowić wydatek cykliczny?');

    pageButton(element(), 'Wznów').click();
    await settle(() => expect(dialog().open).toBe(true));
    const text = dialog().textContent ?? '';
    expect(text).toContain('Po wznowieniu od razu powstanie wystąpienie za bieżący okres.');
    expect(text).not.toContain('Kolejne wystąpienie powstanie');
    expect(text).toContain(
      'Okresy z terminem w czasie wstrzymania (od 05.10.2026) pozostaną puste.',
    );
    buttonByText(dialog(), 'Wznów').click();

    const request = await nextRequest(`${URL}/resume`);
    expect(request.request.body).toEqual({ version: 'AAAAAAAAC1E=' });
    request.flush({ version: 'v2', createdOccurrences: 1 });
    (await nextRequest({ method: 'GET', url: URL })).flush(recurringDetails({ version: 'v2' }));
    await settle(() => expect(element().textContent).toContain('Wznowiono wydatek cykliczny.'));
  });

  it('builds the resume dialog with occurrencesToCreate 0 and with no next term', async () => {
    const { element, settle } = await open(
      recurringDetails({
        status: 'paused',
        pausedOn: '2026-10-05',
        nextDueDate: null,
        resumePreview: { occurrencesToCreate: 0, nextDueDate: '2026-12-10' },
      }),
    );
    const dialog = () => dialogWith(element(), 'Wznowić wydatek cykliczny?');

    pageButton(element(), 'Wznów').click();
    await settle(() => expect(dialog().open).toBe(true));
    expect(dialog().textContent).toContain('Kolejne wystąpienie powstanie 10.12.2026.');
    expect(dialog().textContent).not.toContain('od razu powstanie');
    buttonByText(dialog(), 'Anuluj').click();
    await settle(() => expect(dialog().open).toBe(false));
    http.expectNone(`${URL}/resume`);
  });

  it('offers „Wznów” for an ended definition that is paused', async () => {
    const { element, settle } = await open(
      recurringDetails({
        status: 'ended',
        pausedOn: '2026-10-05',
        nextDueDate: null,
        resumePreview: { occurrencesToCreate: 0, nextDueDate: null },
      }),
    );
    const dialog = () => dialogWith(element(), 'Wznowić wydatek cykliczny?');

    pageButton(element(), 'Wznów').click();
    await settle(() => expect(dialog().open).toBe(true));
    expect(dialog().textContent).toContain(
      'Nie powstaną kolejne wystąpienia — do końca nie wypada już żaden termin.',
    );
    buttonByText(dialog(), 'Anuluj').click();
  });

  it('edits a paused definition: disabled day with a description, paused preview and dayOfMonth in PUT', async () => {
    const { element, settle } = await open(
      recurringDetails({
        status: 'paused',
        pausedOn: '2026-10-05',
        nextDueDate: null,
        resumePreview: { occurrencesToCreate: 0, nextDueDate: '2026-12-10' },
      }),
    );

    buttonByText(element(), 'Edytuj').click();
    await settle(() => expect(element().querySelector('#wydatek-dzien')).not.toBeNull());
    const day = element().querySelector('#wydatek-dzien') as HTMLInputElement;
    expect(day.disabled).toBe(true);
    const describedBy = (day.getAttribute('aria-describedby') ?? '').split(' ');
    const description = describedBy
      .map((id) => document.getElementById(id)?.textContent ?? '')
      .join(' ');
    expect(description).toContain('Wznów wydatek cykliczny, aby zmienić dzień miesiąca.');
    expect(element().textContent).toContain(
      'Wydatek cykliczny jest wstrzymany. Zmiany zadziałają od wznowienia.',
    );
    expect(element().textContent).toContain(
      'Zmiany dotyczą tylko wystąpień, które jeszcze nie powstały, także zaległych.',
    );
    expect(element().textContent).not.toContain('Powtarzaj co miesiąc');
    expect(element().querySelector('#wydatek-data-kosztu')).toBeNull();
    expect(element().querySelector('#wydatek-dokument')).toBeNull();

    const preview = await nextRequest((r) => r.url === '/api/recurring-expenses/preview');
    expect(preview.request.params.get('definitionId')).toBe('c1');
    expect(preview.request.params.get('dayOfMonth')).toBe('10');
    preview.flush(recurringPreview({ paused: true, nextDueDate: null }));
    await settle(() =>
      expect(element().textContent).toContain(
        'Wydatek cykliczny jest wstrzymany — nowe wystąpienia nie powstają; zmiany zadziałają od wznowienia.',
      ),
    );

    buttonByText(element(), 'Zapisz zmiany').click();
    const put = await nextRequest({ method: 'PUT', url: URL });
    expect(put.request.body).toEqual(
      expect.objectContaining({ dayOfMonth: 10, version: 'AAAAAAAAC1E=', grossAmount: '123.00' }),
    );
    put.flush({ version: 'v2', createdOccurrences: 0 });
    (await nextRequest({ method: 'GET', url: URL })).flush(recurringDetails({ version: 'v2' }));
    await settle(() => expect(element().textContent).toContain('Zmiany zapisane.'));
  });

  it('shows the conflict with a reload action and keeps the form', async () => {
    const { element, settle } = await open();

    buttonByText(element(), 'Edytuj').click();
    await settle(() => expect(element().querySelector('form')).not.toBeNull());
    (await nextRequest((r) => r.url === '/api/recurring-expenses/preview')).flush(
      recurringPreview(),
    );
    buttonByText(element(), 'Zapisz zmiany').click();
    respondProblem(await nextRequest({ method: 'PUT', url: URL }), 409, {
      code: 'recurring_expense_conflict',
    });

    await settle(() =>
      expect(element().textContent).toContain(
        'Wydatek cykliczny został zmieniony w międzyczasie. Wczytaj aktualne dane.',
      ),
    );
    expect(() => buttonByText(element(), 'Wczytaj ponownie')).not.toThrow();
    expect(element().querySelector('form')).not.toBeNull();
  });

  it('shows the edit result with the number of created occurrences', async () => {
    const { element, settle } = await open();

    buttonByText(element(), 'Edytuj').click();
    await settle(() => expect(element().querySelector('form')).not.toBeNull());
    (await nextRequest((r) => r.url === '/api/recurring-expenses/preview')).flush(
      recurringPreview(),
    );
    buttonByText(element(), 'Zapisz zmiany').click();
    (await nextRequest({ method: 'PUT', url: URL })).flush({
      version: 'v2',
      createdOccurrences: 2,
    });
    (await nextRequest({ method: 'GET', url: URL })).flush(recurringDetails({ version: 'v2' }));
    await settle(() =>
      expect(element().textContent).toContain('Zmiany zapisane. Utworzono 2 wystąpienia.'),
    );
  });

  it('retries an edit with the current form data and shows field errors from the retry', async () => {
    const { element, settle } = await open();

    buttonByText(element(), 'Edytuj').click();
    await settle(() => expect(element().querySelector('form')).not.toBeNull());
    (await nextRequest((r) => r.url === '/api/recurring-expenses/preview')).flush(
      recurringPreview(),
    );
    buttonByText(element(), 'Zapisz zmiany').click();
    respondProblem(await nextRequest({ method: 'PUT', url: URL }), 503, {
      code: 'recurring_expense_busy',
    });
    await settle(() =>
      expect(element().textContent).toContain('Nie udało się zapisać. Spróbuj ponownie.'),
    );

    typeInto(element().querySelector('#wydatek-brutto') as HTMLInputElement, '150,00');
    buttonByText(element(), 'Spróbuj ponownie').click();
    const again = await nextRequest({ method: 'PUT', url: URL });
    expect(again.request.body).toEqual(expect.objectContaining({ grossAmount: '150.00' }));
    respondProblem(again, 400, {
      title: 'One or more validation errors occurred.',
      errors: { endsOn: ['W tym okresie nie wypada żaden termin.'] },
    });

    await settle(() =>
      expect(element().textContent).toContain('W tym okresie nie wypada żaden termin.'),
    );
    expect(element().querySelector('form')).not.toBeNull();
  });

  it('keeps the view after 503 and repeats the same action with „Spróbuj ponownie”', async () => {
    const { element, settle } = await open();
    const dialog = () => dialogWith(element(), 'Wstrzymać wydatek cykliczny?');

    pageButton(element(), 'Wstrzymaj').click();
    await settle(() => expect(dialog().open).toBe(true));
    buttonByText(dialog(), 'Wstrzymaj').click();
    respondProblem(await nextRequest(`${URL}/pause`), 503, { code: 'recurring_expense_busy' });

    await settle(() =>
      expect(element().textContent).toContain('Nie udało się zapisać. Spróbuj ponownie.'),
    );
    buttonByText(element(), 'Spróbuj ponownie').click();
    const again = await nextRequest(`${URL}/pause`);
    expect(again.request.body).toEqual({ version: 'AAAAAAAAC1E=' });
    again.flush({ version: 'v2', createdOccurrences: 0 });
    (await nextRequest({ method: 'GET', url: URL })).flush(recurringDetails({ version: 'v2' }));
    await settle(() => expect(element().textContent).toContain('Wstrzymano wydatek cykliczny.'));
  });

  it('deletes after the confirmation naming the kept occurrences and returns to the list', async () => {
    const { element, settle } = await open();
    const dialog = () => dialogWith(element(), 'Usunąć wydatek cykliczny?');

    pageButton(element(), 'Usuń').click();
    await settle(() => expect(dialog().open).toBe(true));
    expect(dialog().textContent).toContain(
      'Wydatek cykliczny „Abonament reklamowy” zostanie usunięty i nie utworzy kolejnych wystąpień.',
    );
    expect(dialog().textContent).toContain(
      'Utworzone wystąpienia (2) pozostaną zwykłymi wydatkami',
    );
    buttonByText(dialog(), 'Usuń').click();

    const request = await nextRequest({ method: 'DELETE', url: URL });
    expect(request.request.body).toEqual({ version: 'AAAAAAAAC1E=' });
    respondNoContent(request);
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/wydatki/cykliczne'));
  });

  it('shows the not-found state with a way back to the list', async () => {
    const harness = await RouterTestingHarness.create();
    const navigation = harness.navigateByUrl('/wydatki/cykliczne/c1');
    respondProblem(await nextRequest({ method: 'GET', url: URL }), 404, {
      code: 'recurring_expense_not_found',
    });
    await navigation;
    await vi.waitFor(async () => {
      await harness.fixture.whenStable();
      expect(harness.routeNativeElement?.textContent).toContain(
        'Nie znaleziono wydatku cyklicznego',
      );
    });
    expect(harness.routeNativeElement?.textContent).toContain('Wróć do listy');
  });
});
