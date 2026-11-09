import { recurringPreview } from '../../../testing/expenses-testing';
import { RecurringExpensePreview, RecurringPreviewQuery } from './recurring-expense-models';
import { PREVIEW_TEXTS, RecurringPreviewLoader, previewLines } from './recurring-expense-preview';

const QUERY: RecurringPreviewQuery = {
  dayOfMonth: 10,
  startsOn: '2026-09-01',
  endsOn: null,
  definitionId: null,
};

describe('previewLines', () => {
  it('shows the next occurrence without a payment promise', () => {
    expect(previewLines(recurringPreview({ nextDueDate: '2026-12-10' }))).toEqual([
      'Następne wystąpienie: 10.12.2026. Powstanie tego dnia jako wydatek planowany — wcześniej nie będzie go na liście i nie można go opłacić.',
    ]);
  });

  it('says no further occurrences when there is no next due date', () => {
    expect(previewLines(recurringPreview({ nextDueDate: null }))).toEqual([PREVIEW_TEXTS.noNext]);
  });

  it('declines the immediate occurrences and the pre-launch count', () => {
    const lines = (count: number, beforeLaunch = 0) =>
      previewLines(
        recurringPreview({
          occurrencesToCreate: count,
          firstImmediateDueDate: '2026-09-10',
          beforeLaunchCount: beforeLaunch,
        }),
      );

    expect(lines(1)[1]).toBe(
      'Po zapisie od razu powstanie 1 wystąpienie za terminy od 10.09.2026 do dzisiaj.',
    );
    expect(lines(3)[1]).toBe(
      'Po zapisie od razu powstaną 3 wystąpienia za terminy od 10.09.2026 do dzisiaj.',
    );
    expect(lines(12)[1]).toBe(
      'Po zapisie od razu powstanie 12 wystąpień za terminy od 10.09.2026 do dzisiaj.',
    );
    expect(lines(22, 22)[1]).toContain('powstaną 22 wystąpienia');
    expect(lines(5, 2)[2]).toBe(
      '2 z nich mają termin sprzed uruchomienia systemu — nie wejdą do raportów.',
    );
    expect(lines(5, 5)[2]).toBe(
      '5 z nich ma termin sprzed uruchomienia systemu — nie wejdą do raportów.',
    );
    expect(lines(1, 1)[2]).toBe(
      '1 z nich ma termin sprzed uruchomienia systemu — nie wejdzie do raportów.',
    );
  });

  it('shows edit effects: dropped due occurrences and created ones outside the range', () => {
    const lines = previewLines(
      recurringPreview({ droppedDueCount: 1, createdAfterEndCount: 2, createdBeforeStartCount: 5 }),
    );

    expect(lines).toContain(
      '1 należne wystąpienie, które jeszcze nie powstało, przestaje się liczyć po tej zmianie.',
    );
    expect(lines).toContain('2 utworzone wystąpienia mają termin po nowym końcu — pozostaną.');
    expect(lines).toContain(
      '5 utworzonych wystąpień ma termin przed nowym początkiem — pozostaną.',
    );
    expect(previewLines(recurringPreview({ droppedDueCount: 3 }))).toContain(
      '3 należne wystąpienia, które jeszcze nie powstały, przestają się liczyć po tej zmianie.',
    );
  });

  it('shows only the paused text and created counts for a paused definition (NC1)', () => {
    const lines = previewLines(
      recurringPreview({
        paused: true,
        nextDueDate: null,
        occurrencesToCreate: 0,
        droppedDueCount: 2,
        createdAfterEndCount: 1,
      }),
    );

    expect(lines).toEqual([
      'Wydatek cykliczny jest wstrzymany — nowe wystąpienia nie powstają; zmiany zadziałają od wznowienia.',
      '1 utworzone wystąpienie ma termin po nowym końcu — pozostanie.',
    ]);
    expect(lines.join(' ')).not.toContain('do końca nie wypada już żaden termin');
  });
});

describe('RecurringPreviewLoader', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('waits 300 ms after the last change and sends one request', async () => {
    const fetch = vi.fn(async () => recurringPreview());
    const loader = new RecurringPreviewLoader(fetch);

    loader.request({ ...QUERY, dayOfMonth: 1 });
    await vi.advanceTimersByTimeAsync(200);
    loader.request({ ...QUERY, dayOfMonth: 15 });
    await vi.advanceTimersByTimeAsync(299);
    expect(fetch).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);

    expect(fetch).toHaveBeenCalledTimes(1);
    expect(fetch).toHaveBeenCalledWith({ ...QUERY, dayOfMonth: 15 });
    expect(loader.state().phase).toBe('loaded');
  });

  it('ignores a slower response of an older request (last request wins)', async () => {
    const resolvers: ((preview: RecurringExpensePreview) => void)[] = [];
    const loader = new RecurringPreviewLoader(
      () => new Promise<RecurringExpensePreview>((resolve) => resolvers.push(resolve)),
    );

    loader.request(QUERY);
    await vi.advanceTimersByTimeAsync(300);
    loader.request({ ...QUERY, dayOfMonth: 20 });
    await vi.advanceTimersByTimeAsync(300);
    resolvers[1](recurringPreview({ nextDueDate: '2026-12-20' }));
    await vi.advanceTimersByTimeAsync(0);
    resolvers[0](recurringPreview({ nextDueDate: '2026-12-10' }));
    await vi.advanceTimersByTimeAsync(0);

    const state = loader.state();
    expect(state.phase === 'loaded' && state.preview.nextDueDate).toBe('2026-12-20');
  });

  it('reports an error and clears the preview for an invalid schedule', async () => {
    const loader = new RecurringPreviewLoader(() => Promise.reject(new Error('503')));

    loader.request(QUERY);
    await vi.advanceTimersByTimeAsync(300);
    expect(loader.state().phase).toBe('error');

    loader.request(null);
    expect(loader.state().phase).toBe('idle');
  });
});
