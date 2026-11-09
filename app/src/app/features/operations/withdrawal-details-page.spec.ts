import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  nextRequest,
  provideTestHttp,
  respondNetworkError,
  respondNoContent,
  respondProblem,
  typeInto,
} from '../../../testing/auth-testing';
import { withdrawalDetails } from '../../../testing/operations-testing';
import { formatPln } from '../../shared/format/pl-format';
import { OwnerWithdrawalsService } from './owner-withdrawals.service';
import { WithdrawalDetailsPage } from './withdrawal-details-page';

function buttonByText(root: ParentNode, text: string): HTMLButtonElement {
  const button = Array.from(root.querySelectorAll('button')).find(
    (candidate) => candidate.textContent?.trim() === text,
  );
  if (!button) {
    throw new Error(`Brak przycisku „${text}”`);
  }
  return button;
}

function deleteDialog(root: ParentNode): HTMLDialogElement {
  const dialog = Array.from(root.querySelectorAll('dialog')).find((candidate) =>
    candidate.querySelector('h2')?.textContent?.includes('Usunąć wypłatę?'),
  );
  if (!dialog) {
    throw new Error('Brak dialogu „Usunąć wypłatę?”');
  }
  return dialog;
}

describe('WithdrawalDetailsPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'operacje/wyplaty/:id', component: WithdrawalDetailsPage },
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

  async function start() {
    const harness = await RouterTestingHarness.create();
    document.body.appendChild(harness.fixture.nativeElement);
    const navigation = harness.navigateByUrl('/operacje/wyplaty/wp1');
    const element = () => harness.routeNativeElement as HTMLElement;
    const settle = async (check: () => void) =>
      vi.waitFor(async () => {
        await harness.fixture.whenStable();
        check();
      });
    return { navigation, element, settle };
  }

  async function open() {
    const opened = await start();
    (await nextRequest({ method: 'GET', url: '/api/owner-withdrawals/wp1' })).flush(
      withdrawalDetails(),
    );
    await opened.navigation;
    await opened.settle(() => expect(opened.element().querySelector('.skeleton')).toBeNull());
    return opened;
  }

  async function startEdit(element: () => HTMLElement, settle: (c: () => void) => Promise<void>) {
    buttonByText(element(), 'Edytuj').click();
    (await nextRequest('/api/owner-withdrawals/people')).flush({ people: [] });
    await settle(() => expect(element().querySelector('form')).not.toBeNull());
  }

  it('shows the loading state and then the withdrawal fields', async () => {
    const { navigation, element, settle } = await start();
    const request = await nextRequest({ method: 'GET', url: '/api/owner-withdrawals/wp1' });
    await settle(() => expect(element().querySelector('.skeleton')).not.toBeNull());
    request.flush(withdrawalDetails());
    await navigation;
    await settle(() => expect(element().querySelector('.skeleton')).toBeNull());
    const text = element().textContent ?? '';

    expect(element().querySelector('h1')?.textContent?.trim()).toBe('Wypłata właściciela');
    expect(text).toContain('Właściciel A');
    expect(text).toContain(formatPln('2000.00'));
    expect(text).toContain('05.11.2026');
    expect(text).toContain('Przelew');
    expect(text).toContain('Utworzono');
    expect(text).toContain('Ostatnia zmiana');
  });

  it('shows 404 with a way back to the list', async () => {
    const { navigation, element, settle } = await start();
    respondProblem(await nextRequest('/api/owner-withdrawals/wp1'), 404, {
      code: 'owner_withdrawal_not_found',
    });
    await navigation;

    await settle(() =>
      expect(element().textContent).toContain('Nie znaleziono wypłaty. Mogła zostać usunięta.'),
    );
    buttonByText(element(), 'Wróć do listy').click();
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/operacje'));
  });

  it('shows a read error with a retry', async () => {
    const { navigation, element, settle } = await start();
    respondNetworkError(await nextRequest('/api/owner-withdrawals/wp1'));
    await navigation;
    await settle(() => expect(element().textContent).toContain('Nie udało się wczytać wypłaty.'));

    buttonByText(element(), 'Spróbuj ponownie').click();
    (await nextRequest('/api/owner-withdrawals/wp1')).flush(withdrawalDetails());
    await settle(() => expect(element().textContent).toContain('Właściciel A'));
  });

  it('edits in place with the version and shows the saved notice', async () => {
    const { element, settle } = await open();

    await startEdit(element, settle);
    const amount = element().querySelector('#wyplata-kwota') as HTMLInputElement;
    expect(amount.value).toBe('2000,00');
    typeInto(amount, '2500');
    buttonByText(element(), 'Zapisz zmiany').click();

    const request = await nextRequest({ method: 'PUT', url: '/api/owner-withdrawals/wp1' });
    expect(request.request.body).toEqual({
      person: 'Właściciel A',
      amount: '2500.00',
      withdrawnOn: '2026-11-05',
      method: 'transfer',
      version: 'AAAAAAAAB9E=',
    });
    request.flush({ version: 'v2' });
    (await nextRequest({ method: 'GET', url: '/api/owner-withdrawals/wp1' })).flush(
      withdrawalDetails({ amount: '2500.00', version: 'v2' }),
    );
    await settle(() => expect(element().textContent).toContain('Zmiany zapisane.'));
    expect(element().querySelector('form')).toBeNull();
    expect(element().textContent).toContain(formatPln('2500.00'));
  });

  it('shows the conflict with a reload action and keeps the form', async () => {
    const { element, settle } = await open();

    await startEdit(element, settle);
    buttonByText(element(), 'Zapisz zmiany').click();
    respondProblem(await nextRequest({ method: 'PUT', url: '/api/owner-withdrawals/wp1' }), 409, {
      code: 'owner_withdrawal_conflict',
    });

    await settle(() =>
      expect(element().textContent).toContain(
        'Wypłata została zmieniona w międzyczasie. Wczytaj aktualne dane.',
      ),
    );
    expect(element().querySelector('form')).not.toBeNull();
    buttonByText(element(), 'Wczytaj ponownie').click();
    (await nextRequest({ method: 'GET', url: '/api/owner-withdrawals/wp1' })).flush(
      withdrawalDetails({ amount: '3000.00', version: 'v3' }),
    );
    await settle(() =>
      expect((element().querySelector('#wyplata-kwota') as HTMLInputElement).value).toBe('3000,00'),
    );
  });

  it('asks before deleting and sends nothing when dismissed', async () => {
    const { element, settle } = await open();

    buttonByText(element(), 'Usuń').click();
    await settle(() => expect(deleteDialog(element()).open).toBe(true));
    const text = deleteDialog(element()).textContent ?? '';
    expect(text).toContain('05.11.2026');
    expect(text).toContain(formatPln('2000.00'));
    expect(text).toContain('zniknie z listy „Operacje” i z wypływów');
    buttonByText(deleteDialog(element()), 'Anuluj').click();
    await settle(() => expect(deleteDialog(element()).open).toBe(false));

    http.expectNone({ method: 'DELETE', url: '/api/owner-withdrawals/wp1' });
  });

  it('deletes with the version and returns to the list with a notice', async () => {
    const { element, settle } = await open();

    buttonByText(element(), 'Usuń').click();
    await settle(() => expect(deleteDialog(element()).open).toBe(true));
    buttonByText(deleteDialog(element()), 'Usuń').click();

    const request = await nextRequest({ method: 'DELETE', url: '/api/owner-withdrawals/wp1' });
    expect(request.request.body).toEqual({ version: 'AAAAAAAAB9E=' });
    respondNoContent(request);
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/operacje'));
    expect(TestBed.inject(OwnerWithdrawalsService).takeNotice()).toBe('Wypłata usunięta.');
  });

  it('shows a network error after a failed delete and stays on the details', async () => {
    const { element, settle } = await open();

    buttonByText(element(), 'Usuń').click();
    await settle(() => expect(deleteDialog(element()).open).toBe(true));
    buttonByText(deleteDialog(element()), 'Usuń').click();
    respondNetworkError(await nextRequest({ method: 'DELETE', url: '/api/owner-withdrawals/wp1' }));

    await settle(() =>
      expect(element().querySelector('[role="alert"]')?.textContent).toContain(
        'Nie udało się połączyć z serwerem.',
      ),
    );
    expect(TestBed.inject(Router).url).toBe('/operacje/wyplaty/wp1');
  });

  it('shows a delete conflict with the reload action', async () => {
    const { element, settle } = await open();

    buttonByText(element(), 'Usuń').click();
    await settle(() => expect(deleteDialog(element()).open).toBe(true));
    buttonByText(deleteDialog(element()), 'Usuń').click();
    respondProblem(
      await nextRequest({ method: 'DELETE', url: '/api/owner-withdrawals/wp1' }),
      409,
      { code: 'owner_withdrawal_conflict' },
    );

    await settle(() => expect(element().textContent).toContain('Wypłata została zmieniona'));
    expect(() => buttonByText(element(), 'Wczytaj ponownie')).not.toThrow();
  });
});
