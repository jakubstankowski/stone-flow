import { syncStatus } from '../../../testing/integration-testing';
import { SyncRun } from './integration-models';
import {
  IntegrationView,
  STALE_AFTER_MS,
  dataCompleteOf,
  explanationsOf,
  integrationStateOf,
  labelOf,
  lastFullCheckText,
  presentationOf,
  RUN_STATUS_LABELS,
} from './integration-state';

const SUCCESS = '2026-11-05T21:50:00Z';
const SUCCESS_MS = Date.parse(SUCCESS);
const NOW = SUCCESS_MS + 60 * 60 * 1000;

function run(status: string): SyncRun {
  return {
    status,
    startedAtUtc: '2026-11-05T22:00:00Z',
    finishedAtUtc: null,
    source: 'manual',
    ordersProcessed: 0,
    errors: 0,
    message: null,
  };
}

describe('integrationStateOf', () => {
  it('is current after a fresh success without open errors', () => {
    expect(integrationStateOf(syncStatus(), NOW)).toEqual({
      kind: 'current',
      reasons: [],
      dataComplete: true,
    });
  });

  it('checks a missing configuration before a running sync', () => {
    const state = integrationStateOf(syncStatus({ configured: false, isRunning: true }), NOW);
    expect(state.kind).toBe('notConfigured');
    expect(state.dataComplete).toBe(false);
  });

  it('checks a running sync before a failed last run', () => {
    const state = integrationStateOf(syncStatus({ isRunning: true, lastRun: run('failed') }), NOW);
    expect(state.kind).toBe('running');
  });

  it.each(['failed', 'abandoned', 'running', 'nieznany'])(
    'treats a finished last run with status %s as an error',
    (status) => {
      const state = integrationStateOf(syncStatus({ lastRun: run(status) }), NOW);
      expect(state.kind).toBe('error');
      expect(state.dataComplete).toBe(false);
    },
  );

  it('is incomplete with open order errors', () => {
    const state = integrationStateOf(syncStatus({ openErrors: 2 }), NOW);
    expect(state).toEqual({ kind: 'incomplete', reasons: ['openErrors'], dataComplete: false });
  });

  it('stays current exactly 25 hours after the last success', () => {
    expect(integrationStateOf(syncStatus(), SUCCESS_MS + STALE_AFTER_MS).kind).toBe('current');
  });

  it('is incomplete more than 25 hours after the last success', () => {
    const state = integrationStateOf(syncStatus(), SUCCESS_MS + STALE_AFTER_MS + 1000);
    expect(state).toEqual({ kind: 'incomplete', reasons: ['stale'], dataComplete: false });
  });

  it.each([null, 'nie data'])('is incomplete without a readable success (%s)', (value) => {
    const state = integrationStateOf(syncStatus({ lastSuccessAtUtc: value, lastRun: null }), NOW);
    expect(state).toEqual({ kind: 'incomplete', reasons: ['neverSucceeded'], dataComplete: false });
  });

  it('does not treat a success in the future as stale', () => {
    expect(integrationStateOf(syncStatus(), SUCCESS_MS - 1000).kind).toBe('current');
  });

  it('is current without any run but with a fresh success', () => {
    expect(integrationStateOf(syncStatus({ lastRun: null }), NOW).kind).toBe('current');
  });

  it('lists reasons in a fixed order', () => {
    const state = integrationStateOf(
      syncStatus({ openErrors: 1 }),
      SUCCESS_MS + STALE_AFTER_MS + 1000,
    );
    expect(state.reasons).toEqual(['openErrors', 'stale']);
  });

  describe('dataComplete while running', () => {
    it('is true only after a succeeded last run without reasons', () => {
      expect(integrationStateOf(syncStatus({ isRunning: true }), NOW).dataComplete).toBe(true);
    });

    it.each([['failed'], ['abandoned'], ['running']])('is false after a %s last run', (status) => {
      const state = integrationStateOf(syncStatus({ isRunning: true, lastRun: run(status) }), NOW);
      expect(state.dataComplete).toBe(false);
    });

    it('is false without any run', () => {
      const state = integrationStateOf(syncStatus({ isRunning: true, lastRun: null }), NOW);
      expect(state.dataComplete).toBe(false);
    });

    it('is false with any reason', () => {
      const state = integrationStateOf(syncStatus({ isRunning: true, openErrors: 1 }), NOW);
      expect(state.dataComplete).toBe(false);
    });
  });
});

describe('presentationOf', () => {
  function known(aged: boolean, overrides = {}): IntegrationView {
    const status = syncStatus(overrides);
    return {
      phase: 'known',
      status,
      state: integrationStateOf(status, NOW),
      aged,
      readAtUtc: '2026-11-05T22:30:00Z',
    };
  }

  it('shows the state label with its tone and icon', () => {
    expect(presentationOf(known(false), NOW)).toEqual({
      label: 'Aktualne',
      tone: 'success',
      icon: 'check',
      agedText: null,
    });
    expect(presentationOf(known(false, { isRunning: true }), NOW).icon).toBe('sync');
    expect(presentationOf(known(false, { openErrors: 1 }), NOW).tone).toBe('warning');
  });

  it('drops the success tone and check icon for an aged read', () => {
    expect(presentationOf(known(true), NOW)).toEqual({
      label: 'Aktualne',
      tone: 'neutral',
      icon: 'clock',
      agedText: 'Stan z 23:30',
    });
  });

  it.each([
    [{ openErrors: 1 }, 'Dane niekompletne', 'warning', 'warning'],
    [{ lastRun: run('failed') }, 'Błąd', 'error', 'error'],
    [{ configured: false }, 'Nie skonfigurowano', 'warning', 'plug'],
    [{ isRunning: true }, 'Synchronizacja trwa', 'info', 'sync'],
  ])('keeps the tone and icon of %o when the read aged', (overrides, label, tone, icon) => {
    expect(presentationOf(known(true, overrides), NOW)).toEqual({
      label,
      tone,
      icon,
      agedText: 'Stan z 23:30',
    });
  });

  it('shows the date of an aged read from another day', () => {
    const nextDay = Date.parse('2026-11-07T08:00:00Z');
    expect(presentationOf(known(true), nextDay).agedText).toBe('Stan z 05.11.2026, 23:30');
  });

  it('shows loading and unknown without suggesting completeness', () => {
    expect(presentationOf({ phase: 'loading' }, NOW).label).toBe('Sprawdzanie stanu…');
    expect(presentationOf({ phase: 'unknown', last: null }, NOW)).toEqual({
      label: 'Stan nieznany',
      tone: 'neutral',
      icon: 'question',
      agedText: null,
    });
    expect(dataCompleteOf({ phase: 'unknown', last: syncStatus() })).toBe(false);
    expect(dataCompleteOf({ phase: 'loading' })).toBe(false);
    expect(dataCompleteOf(known(true))).toBe(false);
    expect(dataCompleteOf(known(false))).toBe(true);
  });
});

describe('lastFullCheckText', () => {
  it('formats the last success in Europe/Warsaw', () => {
    const status = syncStatus();
    const view: IntegrationView = {
      phase: 'known',
      status,
      state: integrationStateOf(status, NOW),
      aged: false,
      readAtUtc: SUCCESS,
    };
    expect(lastFullCheckText(view)).toBe('05.11.2026, 22:50');
  });

  it('marks the last known value when the state is unknown', () => {
    expect(lastFullCheckText({ phase: 'unknown', last: syncStatus() })).toBe(
      '05.11.2026, 22:50 (ostatni znany odczyt)',
    );
    expect(lastFullCheckText({ phase: 'unknown', last: null })).toBe('Brak danych');
  });
});

describe('explanationsOf', () => {
  it('explains each reason in order', () => {
    const status = syncStatus({ openErrors: 3, lastSuccessAtUtc: null });
    expect(explanationsOf(status, integrationStateOf(status, NOW))).toEqual([
      'Otwarte błędy zamówień: 3.',
      'Pełna kontrola jeszcze się nie odbyła.',
    ]);
  });

  it('explains a failed run with its reasons', () => {
    const status = syncStatus({ lastRun: run('failed'), openErrors: 1 });
    expect(explanationsOf(status, integrationStateOf(status, NOW))).toEqual([
      'Ostatni przebieg uzupełniania nie powiódł się. Wyniki mogą być niepełne.',
      'Otwarte błędy zamówień: 1.',
    ]);
  });
});

describe('labelOf', () => {
  it('falls back to the raw code', () => {
    expect(labelOf(RUN_STATUS_LABELS, 'abandoned')).toBe('Przerwany');
    expect(labelOf(RUN_STATUS_LABELS, 'nowy')).toBe('nowy');
    expect(labelOf(RUN_STATUS_LABELS, 'toString')).toBe('toString');
  });
});
