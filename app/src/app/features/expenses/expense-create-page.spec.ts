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

  async function open() {
    const harness = await RouterTestingHarness.create();
    document.body.appendChild(harness.fixture.nativeElement);
    await harness.navigateByUrl('/wydatki/nowy');
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
});
