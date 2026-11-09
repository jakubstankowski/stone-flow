import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  provideTestHttp,
  respondNetworkError,
  respondNoContent,
  respondProblem,
} from '../../../testing/auth-testing';
import { withdrawalDetails } from '../../../testing/operations-testing';
import { OwnerWithdrawalsService } from './owner-withdrawals.service';
import { WithdrawalInput } from './withdrawal-models';

const INPUT: WithdrawalInput = {
  person: 'Właściciel A',
  amount: '2000.00',
  withdrawnOn: '2026-11-05',
  method: 'cash',
};

describe('OwnerWithdrawalsService', () => {
  let service: OwnerWithdrawalsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    service = TestBed.inject(OwnerWithdrawalsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('gives no suggestions when the people request fails', async () => {
    const people = service.people();
    respondNetworkError(http.expectOne('/api/owner-withdrawals/people'));

    expect(await people).toEqual([]);
  });

  it('loads a withdrawal and maps 404 to notFound', async () => {
    const loaded = service.load('wp1');
    http.expectOne('/api/owner-withdrawals/wp1').flush(withdrawalDetails());
    expect((await loaded).kind).toBe('loaded');

    const missing = service.load('wp2');
    respondProblem(http.expectOne('/api/owner-withdrawals/wp2'), 404, {
      code: 'owner_withdrawal_not_found',
    });
    expect(await missing).toEqual({ kind: 'notFound' });
  });

  it('saves a new withdrawal with a one-time notice', async () => {
    const outcome = service.create(INPUT, 'klucz');
    http
      .expectOne('/api/owner-withdrawals')
      .flush({ id: 'wp1', version: 'v1' }, { status: 201, statusText: 'Created' });

    expect(await outcome).toEqual({ kind: 'saved', id: 'wp1' });
    expect(service.takeNotice()).toBe('Wypłata zapisana.');
    expect(service.takeNotice()).toBeNull();
  });

  it('recognises a reused idempotency key on create', async () => {
    const outcome = service.create(INPUT, 'klucz');
    respondProblem(http.expectOne('/api/owner-withdrawals'), 409, {
      code: 'idempotency_key_reused',
      ownerWithdrawalId: 'wp9',
    });

    expect(await outcome).toEqual({ kind: 'keyReused', withdrawalId: 'wp9' });
  });

  it('maps a network failure on create to an error', async () => {
    const outcome = service.create(INPUT, 'klucz');
    respondNetworkError(http.expectOne('/api/owner-withdrawals'));

    expect(await outcome).toEqual({ kind: 'error', error: { kind: 'network' } });
    expect(service.takeNotice()).toBeNull();
  });

  it.each([
    [409, 'owner_withdrawal_conflict', 'conflict'],
    [404, 'owner_withdrawal_not_found', 'notFound'],
    [400, 'csrf_invalid', 'error'],
  ])('maps %i %s on update to %s', async (status, code, kind) => {
    const outcome = service.update('wp1', INPUT, 'v1');
    respondProblem(http.expectOne('/api/owner-withdrawals/wp1'), status, { code });

    expect((await outcome).kind).toBe(kind);
  });

  it('keeps the save and delete notices', async () => {
    const update = service.update('wp1', INPUT, 'v1');
    http.expectOne('/api/owner-withdrawals/wp1').flush({ version: 'v2' });
    expect((await update).kind).toBe('saved');
    expect(service.takeNotice()).toBe('Zmiany zapisane.');

    const removal = service.delete('wp1', 'v2');
    respondNoContent(http.expectOne('/api/owner-withdrawals/wp1'));
    expect((await removal).kind).toBe('saved');
    expect(service.takeNotice()).toBe('Wypłata usunięta.');
  });

  it('maps a conflict on delete', async () => {
    const removal = service.delete('wp1', 'v1');
    respondProblem(http.expectOne('/api/owner-withdrawals/wp1'), 409, {
      code: 'owner_withdrawal_conflict',
    });

    expect(await removal).toEqual({ kind: 'conflict' });
  });
});
