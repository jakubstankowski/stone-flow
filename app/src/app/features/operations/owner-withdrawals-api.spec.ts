import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTestHttp, respondNoContent } from '../../../testing/auth-testing';
import { withdrawalDetails } from '../../../testing/operations-testing';
import { OwnerWithdrawalsApi } from './owner-withdrawals-api';
import { WithdrawalInput } from './withdrawal-models';

const INPUT: WithdrawalInput = {
  person: 'Właściciel A',
  amount: '2000.00',
  withdrawnOn: '2026-11-05',
  method: 'blik',
};

describe('OwnerWithdrawalsApi', () => {
  let api: OwnerWithdrawalsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTestHttp()] });
    api = TestBed.inject(OwnerWithdrawalsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads the people suggestions', async () => {
    const result = api.people();
    http
      .expectOne({ method: 'GET', url: '/api/owner-withdrawals/people' })
      .flush({ people: ['Właściciel A', 'Wspólnik B'] });

    expect(await result).toEqual(['Właściciel A', 'Wspólnik B']);
  });

  it('reads one withdrawal by id', async () => {
    const result = api.get('wp1');
    http.expectOne({ method: 'GET', url: '/api/owner-withdrawals/wp1' }).flush(withdrawalDetails());

    expect((await result).person).toBe('Właściciel A');
  });

  it('creates with the idempotency key and tells a new withdrawal from a repeat', async () => {
    const created = api.create(INPUT, 'klucz-1');
    const first = http.expectOne('/api/owner-withdrawals');
    expect(first.request.method).toBe('POST');
    expect(first.request.headers.get('Idempotency-Key')).toBe('klucz-1');
    expect(first.request.body).toEqual(INPUT);
    first.flush({ id: 'wp1', version: 'v1' }, { status: 201, statusText: 'Created' });
    expect(await created).toEqual({ created: true, withdrawal: { id: 'wp1', version: 'v1' } });

    const repeated = api.create(INPUT, 'klucz-1');
    http
      .expectOne('/api/owner-withdrawals')
      .flush({ id: 'wp1', version: 'v1' }, { status: 200, statusText: 'OK' });
    expect(await repeated).toEqual({ created: false, withdrawal: { id: 'wp1', version: 'v1' } });
  });

  it('updates with the version next to the input', async () => {
    const update = api.update('wp1', INPUT, 'v1');
    const request = http.expectOne('/api/owner-withdrawals/wp1');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ ...INPUT, version: 'v1' });
    request.flush({ version: 'v2' });

    expect(await update).toEqual({ version: 'v2' });
  });

  it('deletes with the version in the body', async () => {
    const removal = api.delete('wp1', 'v2');
    const request = http.expectOne('/api/owner-withdrawals/wp1');
    expect(request.request.method).toBe('DELETE');
    expect(request.request.body).toEqual({ version: 'v2' });
    respondNoContent(request);
    await removal;
  });
});
