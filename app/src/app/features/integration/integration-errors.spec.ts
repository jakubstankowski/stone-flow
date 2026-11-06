import { TestBed } from '@angular/core/testing';
import { orderError } from '../../../testing/integration-testing';
import { IntegrationApi } from './integration-api';
import { ERRORS_LIST_ERROR, IntegrationErrors } from './integration-errors';
import { OrderErrorPage, RetryOutcome } from './integration-models';
import { IntegrationStatus } from './integration-status';

function page(items = [orderError()], pageNumber = 1, total = items.length): OrderErrorPage {
  return { items, page: pageNumber, pageSize: 25, total };
}

describe('IntegrationErrors', () => {
  let api: { errors: ReturnType<typeof vi.fn>; retry: ReturnType<typeof vi.fn> };
  let status: { refresh: ReturnType<typeof vi.fn> };
  let errors: IntegrationErrors;

  beforeEach(() => {
    api = { errors: vi.fn(async () => page()), retry: vi.fn() };
    status = { refresh: vi.fn(async () => undefined) };
    TestBed.configureTestingModule({
      providers: [
        IntegrationErrors,
        { provide: IntegrationApi, useValue: api },
        { provide: IntegrationStatus, useValue: status },
      ],
    });
    errors = TestBed.inject(IntegrationErrors);
  });

  it('loads open errors from the first page by default', async () => {
    await errors.load();
    expect(api.errors).toHaveBeenCalledWith({ status: 'open', page: 1, pageSize: 25 });
    expect(errors.items()).toEqual([orderError()]);
    expect(errors.activeFilters()).toEqual([]);
  });

  it('goes back to the first page when the filter changes and shows it as active', async () => {
    errors.goTo(3);
    errors.setFilter('closed');
    await vi.waitFor(() => expect(errors.loading()).toBe(false));

    expect(api.errors).toHaveBeenLastCalledWith({ status: 'closed', page: 1, pageSize: 25 });
    expect(errors.activeFilters()).toEqual([{ id: 'status', label: 'Status: Zamknięte' }]);

    errors.clearFilter();
    expect(errors.filter()).toBe('open');
  });

  it('steps back a page when the current page came back empty', async () => {
    api.errors
      .mockResolvedValueOnce(page([], 3, 50))
      .mockResolvedValueOnce(page([orderError()], 2, 26));
    errors.goTo(3);
    await vi.waitFor(() => expect(errors.page()).toBe(2));
    await vi.waitFor(() => expect(errors.items()).toHaveLength(1));
  });

  it('shows an error state when the list cannot be read', async () => {
    api.errors.mockRejectedValueOnce(new Error('sieć'));
    await errors.load();
    expect(errors.error()).toBe(ERRORS_LIST_ERROR);
    expect(errors.loading()).toBe(false);
  });

  it('applies only the latest reload', async () => {
    let first: (value: OrderErrorPage) => void = () => undefined;
    api.errors
      .mockImplementationOnce(() => new Promise<OrderErrorPage>((resolve) => (first = resolve)))
      .mockResolvedValueOnce(page([orderError({ id: 'e2' })]));
    const older = errors.load();
    await errors.load();
    first(page([orderError({ id: 'old' })]));
    await older;
    expect(errors.items().map((item) => item.id)).toEqual(['e2']);
  });

  it('keeps one retry per row in flight and allows other rows', async () => {
    const finishers: ((outcome: RetryOutcome) => void)[] = [];
    api.retry.mockImplementation(
      () => new Promise<RetryOutcome>((resolve) => finishers.push(resolve)),
    );
    const item = orderError();

    const first = errors.retry(item);
    void errors.retry(item);
    const second = errors.retry(orderError({ id: 'e2' }));

    expect(api.retry).toHaveBeenCalledTimes(2);
    expect(errors.isBusy('e1')).toBe(true);
    expect(errors.isBusy('e2')).toBe(true);

    for (const finish of finishers) {
      finish({ kind: 'done', error: orderError({ status: 'closed' }) });
    }
    await Promise.all([first, second]);
    expect(errors.isBusy('e1')).toBe(false);
    expect(errors.isBusy('e2')).toBe(false);
  });

  it('reloads the list and refreshes the status after a result', async () => {
    api.retry.mockResolvedValueOnce({ kind: 'done', error: orderError({ status: 'closed' }) });
    await errors.retry(orderError());

    expect(errors.isBusy('e1')).toBe(false);
    expect(errors.result()?.text).toBe('Zamówienie Z/2026/0001 zapisane. Błąd zamknięty.');
    expect(api.errors).toHaveBeenCalledTimes(1);
    expect(status.refresh).toHaveBeenCalledWith({ force: true });
  });

  it('leaves busy, reloads and shows a generic message after a time-out', async () => {
    api.retry.mockResolvedValueOnce({ kind: 'timeout' });
    await errors.retry(orderError());

    expect(errors.isBusy('e1')).toBe(false);
    expect(errors.result()).toMatchObject({ alert: true });
    expect(api.errors).toHaveBeenCalledTimes(1);
  });

  it('neither reloads nor refreshes after a network error', async () => {
    api.retry.mockResolvedValueOnce({ kind: 'error', error: { kind: 'network' } });
    await errors.retry(orderError());

    expect(api.errors).not.toHaveBeenCalled();
    expect(status.refresh).not.toHaveBeenCalled();
  });
});
