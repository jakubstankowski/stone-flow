import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  nextRequest,
  provideTestHttp,
  respondNetworkError,
  respondProblem,
  typeInto,
} from '../../../testing/auth-testing';
import { WithdrawalCreatePage } from './withdrawal-create-page';

function buttonByText(root: ParentNode, text: string): HTMLButtonElement {
  const button = Array.from(root.querySelectorAll('button')).find(
    (candidate) => candidate.textContent?.trim() === text,
  );
  if (!button) {
    throw new Error(`Brak przycisku „${text}”`);
  }
  return button;
}

describe('WithdrawalCreatePage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'operacje/wyplaty/nowa', component: WithdrawalCreatePage },
          { path: 'operacje/wyplaty/:id', children: [] },
          { path: 'operacje', children: [] },
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
    const navigation = harness.navigateByUrl('/operacje/wyplaty/nowa');
    (await nextRequest('/api/owner-withdrawals/people')).flush({ people: ['Właściciel A'] });
    await navigation;
    const element = () => harness.routeNativeElement as HTMLElement;
    const settle = async (check: () => void) =>
      vi.waitFor(async () => {
        await harness.fixture.whenStable();
        check();
      });
    await settle(() => expect(element().querySelectorAll('datalist option')).toHaveLength(1));
    return { element, settle };
  }

  async function fillAndSubmit(
    element: HTMLElement,
    settle: (check: () => void) => Promise<void>,
  ): Promise<void> {
    typeInto(element.querySelector('#wyplata-osoba') as HTMLInputElement, 'Właściciel A');
    typeInto(element.querySelector('#wyplata-kwota') as HTMLInputElement, '2000');
    const method = element.querySelector('#wyplata-metoda') as HTMLSelectElement;
    method.value = 'transfer';
    method.dispatchEvent(new Event('input', { bubbles: true }));
    method.dispatchEvent(new Event('change', { bubbles: true }));
    await settle(() => expect(method.value).toBe('transfer'));
    buttonByText(element, 'Zapisz').click();
  }

  function createRequest(): Promise<TestRequest> {
    return nextRequest({ method: 'POST', url: '/api/owner-withdrawals' });
  }

  it('goes to the details after 201 with an idempotency key', async () => {
    const { element, settle } = await open();

    await fillAndSubmit(element(), settle);
    const request = await createRequest();
    expect(request.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect(request.request.body).toEqual(
      expect.objectContaining({ person: 'Właściciel A', amount: '2000.00', method: 'transfer' }),
    );
    request.flush({ id: 'wp7', version: 'v1' }, { status: 201, statusText: 'Created' });

    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/operacje/wyplaty/wp7'));
  });

  it('keeps the key after a network error so the retry does not duplicate', async () => {
    const { element, settle } = await open();

    await fillAndSubmit(element(), settle);
    const first = await createRequest();
    const key = first.request.headers.get('Idempotency-Key');
    respondNetworkError(first);
    await settle(() =>
      expect(element().textContent).toContain('Nie udało się połączyć z serwerem.'),
    );
    expect((element().querySelector('#wyplata-kwota') as HTMLInputElement).value).toBe('2000');

    buttonByText(element(), 'Zapisz').click();
    const second = await createRequest();
    expect(second.request.headers.get('Idempotency-Key')).toBe(key);
    second.flush({ id: 'wp8', version: 'v1' }, { status: 200, statusText: 'OK' });
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/operacje/wyplaty/wp8'));
  });

  it('shows the existing withdrawal after 409 idempotency_key_reused and starts over with a new key', async () => {
    const { element, settle } = await open();

    await fillAndSubmit(element(), settle);
    const first = await createRequest();
    const firstKey = first.request.headers.get('Idempotency-Key');
    respondProblem(first, 409, { code: 'idempotency_key_reused', ownerWithdrawalId: 'wp3' });

    await settle(() => expect(element().textContent).toContain('Ten formularz zapisał już'));
    expect(element().querySelector('a[href="/operacje/wyplaty/wp3"]')).not.toBeNull();
    expect(TestBed.inject(Router).url).toBe('/operacje/wyplaty/nowa');

    buttonByText(element(), 'Dodaj kolejną wypłatę').click();
    await settle(() => expect(element().textContent).not.toContain('Ten formularz zapisał już'));
    expect((element().querySelector('#wyplata-osoba') as HTMLInputElement).value).toBe('');

    await fillAndSubmit(element(), settle);
    const second = await createRequest();
    expect(second.request.headers.get('Idempotency-Key')).not.toBe(firstKey);
    second.flush({ id: 'wp9', version: 'v1' }, { status: 201, statusText: 'Created' });
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/operacje/wyplaty/wp9'));
  });

  it('returns to the list without saving', async () => {
    const { element } = await open();

    buttonByText(element(), 'Wróć do listy').click();

    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/operacje'));
  });
});
