import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { TimeoutError } from 'rxjs';
import { syncStatus } from '../../../testing/integration-testing';
import { IntegrationApi } from './integration-api';
import { SyncStatus } from './integration-models';
import { IntegrationStatus, PERIOD_MS, RUNNING_PERIOD_MS } from './integration-status';

interface Deferred {
  resolve(status: SyncStatus): void;
  reject(error: unknown): void;
}

const START = new Date('2026-11-06T12:00:00Z');

describe('IntegrationStatus', () => {
  let pending: Deferred[];
  let api: { status: ReturnType<typeof vi.fn> };
  let service: IntegrationStatus;
  let visibility: DocumentVisibilityState;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(START);
    visibility = 'visible';
    Object.defineProperty(document, 'visibilityState', {
      configurable: true,
      get: () => visibility,
    });
    pending = [];
    api = {
      status: vi.fn(
        () =>
          new Promise<SyncStatus>((resolve, reject) => {
            pending.push({ resolve, reject });
          }),
      ),
    };
    TestBed.configureTestingModule({ providers: [{ provide: IntegrationApi, useValue: api }] });
    service = TestBed.inject(IntegrationStatus);
  });

  afterEach(() => {
    service.stop();
    vi.useRealTimers();
    delete (document as { visibilityState?: unknown }).visibilityState;
  });

  async function settle(): Promise<void> {
    for (let i = 0; i < 5; i++) {
      await Promise.resolve();
    }
  }

  async function respond(status: SyncStatus, index = pending.length - 1): Promise<void> {
    pending[index].resolve(status);
    await settle();
  }

  async function startWith(status: SyncStatus = syncStatus()): Promise<void> {
    service.start();
    await respond(status);
  }

  function press(type = 'pointerdown'): void {
    document.dispatchEvent(new Event(type));
  }

  it('reads the status on start and shows it without an announcement', async () => {
    expect(service.view().phase).toBe('loading');
    expect(service.dataComplete()).toBe(false);

    await startWith();

    expect(api.status).toHaveBeenCalledTimes(1);
    expect(service.presentation().label).toBe('Aktualne');
    expect(service.dataComplete()).toBe(true);
    expect(service.announcement()).toBe('');
  });

  it('sends no background read without user activity', async () => {
    await startWith();
    vi.advanceTimersByTime(30 * 60 * 1000);
    expect(api.status).toHaveBeenCalledTimes(1);
  });

  it('marks a read older than two periods as aged, without requests', async () => {
    await startWith();

    vi.advanceTimersByTime(2 * PERIOD_MS);
    expect(service.dataComplete()).toBe(true);

    vi.advanceTimersByTime(1);
    expect(service.view()).toMatchObject({ phase: 'known', aged: true });
    expect(service.dataComplete()).toBe(false);
    expect(service.presentation()).toMatchObject({ tone: 'neutral', icon: 'clock' });
    expect(service.presentation().agedText).toBe('Stan z 13:00');
    expect(service.announcement()).toBe('Stan integracji: Aktualne, stan z 13:00');
    expect(api.status).toHaveBeenCalledTimes(1);
  });

  it('derives the aged state from the clock when the timer is late', async () => {
    await startWith();

    // Skok zegara (np. uśpienie z widoczną kartą) bez odpalenia timera wieku odczytu.
    vi.setSystemTime(START.getTime() + 10 * 60 * 1000);
    vi.advanceTimersByTime(60_000);

    expect(service.view()).toMatchObject({ aged: true });
    expect(service.dataComplete()).toBe(false);
    expect(api.status).toHaveBeenCalledTimes(1);
  });

  it('re-evaluates the age at once when the tab becomes visible', async () => {
    await startWith();
    vi.setSystemTime(START.getTime() + 10 * 60 * 1000);

    document.dispatchEvent(new Event('visibilitychange'));

    expect(service.view()).toMatchObject({ aged: true });
    expect(api.status).toHaveBeenCalledTimes(2);
  });

  it('does not announce leaving the aged state of the same kind', async () => {
    await startWith();
    vi.advanceTimersByTime(2 * PERIOD_MS + 1);
    expect(service.announcement()).toBe('Stan integracji: Aktualne, stan z 13:00');

    press();
    await respond(syncStatus());
    expect(service.announcement()).toBe('Stan integracji: Aktualne, stan z 13:00');
  });

  it('sends nothing for long while a sync is running and the tab is idle', async () => {
    await startWith(syncStatus({ isRunning: true }));
    vi.advanceTimersByTime(30 * 60 * 1000);
    expect(api.status).toHaveBeenCalledTimes(1);
  });

  it('announces leaving the unknown state', async () => {
    await startWith();
    void service.refresh({ force: true });
    pending[1].reject(new TimeoutError());
    await settle();
    void service.refresh({ force: true });
    await respond(syncStatus());
    expect(service.announcement()).toBe('Stan integracji: Aktualne');
  });

  it('ignores refresh calls after stop', async () => {
    await startWith();
    service.stop();
    await service.refresh({ force: true });
    expect(api.status).toHaveBeenCalledTimes(1);
  });

  it('refreshes at once on the first activity after the read aged', async () => {
    await startWith();
    vi.advanceTimersByTime(2 * PERIOD_MS + 1);

    press('keydown');
    expect(api.status).toHaveBeenCalledTimes(2);
    await respond(syncStatus());

    expect(service.view()).toMatchObject({ aged: false });
    expect(service.dataComplete()).toBe(true);
  });

  it('reads on activity only once a period has passed since the last read', async () => {
    await startWith();

    vi.advanceTimersByTime(PERIOD_MS - 1);
    press();
    expect(api.status).toHaveBeenCalledTimes(1);

    vi.advanceTimersByTime(1);
    press();
    expect(api.status).toHaveBeenCalledTimes(2);
  });

  it('ignores pointer movement and the mouse wheel', async () => {
    await startWith();
    vi.advanceTimersByTime(PERIOD_MS);

    press('pointermove');
    press('wheel');
    expect(api.status).toHaveBeenCalledTimes(1);
  });

  it('does not read on activity at the same instant as a forced read', async () => {
    await startWith();
    vi.advanceTimersByTime(PERIOD_MS);

    void service.refresh({ force: true });
    press();
    expect(api.status).toHaveBeenCalledTimes(2);
  });

  it('sends nothing from a hidden tab and reads when the tab becomes visible', async () => {
    await startWith();
    vi.advanceTimersByTime(PERIOD_MS);

    visibility = 'hidden';
    press();
    expect(api.status).toHaveBeenCalledTimes(1);

    visibility = 'visible';
    document.dispatchEvent(new Event('visibilitychange'));
    expect(api.status).toHaveBeenCalledTimes(2);
  });

  it('uses a 10 s period and a 20 s age limit while a sync is running', async () => {
    await startWith(syncStatus({ isRunning: true }));

    vi.advanceTimersByTime(RUNNING_PERIOD_MS);
    press();
    expect(api.status).toHaveBeenCalledTimes(2);
    await respond(syncStatus({ isRunning: true }));

    vi.advanceTimersByTime(2 * RUNNING_PERIOD_MS + 1);
    expect(service.view()).toMatchObject({ aged: true });
    expect(service.presentation().label).toBe('Synchronizacja trwa');
    expect(api.status).toHaveBeenCalledTimes(2);
  });

  it('keeps the running period after a failed read', async () => {
    await startWith(syncStatus({ isRunning: true }));
    vi.advanceTimersByTime(RUNNING_PERIOD_MS);
    press();
    pending[1].reject(new TimeoutError());
    await settle();
    expect(service.view().phase).toBe('unknown');

    vi.advanceTimersByTime(RUNNING_PERIOD_MS);
    press();
    expect(api.status).toHaveBeenCalledTimes(3);
  });

  it('turns a failed read into an unknown state, keeping the last response', async () => {
    await startWith(syncStatus({ configured: true }));
    vi.advanceTimersByTime(2 * PERIOD_MS + 1);

    press();
    pending[1].reject(new HttpErrorResponse({ status: 503 }));
    await settle();

    expect(service.view()).toEqual({ phase: 'unknown', last: syncStatus({ configured: true }) });
    expect(service.presentation().label).toBe('Stan nieznany');
    expect(service.lastFullCheck()).toBe('05.11.2026, 22:50 (ostatni znany odczyt)');
    expect(service.dataComplete()).toBe(false);
    expect(service.announcement()).toBe('Stan integracji: Stan nieznany');
  });

  it('leaves a 401 to the session interceptor', async () => {
    await startWith();
    void service.refresh({ force: true });
    pending[1].reject(new HttpErrorResponse({ status: 401 }));
    await settle();
    expect(service.view().phase).toBe('known');
  });

  it('shares a read in flight unless forced, and ignores a late older response', async () => {
    service.start();
    void service.refresh();
    expect(api.status).toHaveBeenCalledTimes(1);

    void service.refresh({ force: true });
    expect(api.status).toHaveBeenCalledTimes(2);

    await respond(syncStatus({ isRunning: true }), 1);
    await respond(syncStatus({ isRunning: false }), 0);

    expect(service.presentation().label).toBe('Synchronizacja trwa');
  });

  it('after an idle pause: activity reads, the forced read after the action wins', async () => {
    await startWith();
    vi.advanceTimersByTime(5 * PERIOD_MS);

    press();
    expect(api.status).toHaveBeenCalledTimes(2);
    void service.refresh({ force: true });
    expect(api.status).toHaveBeenCalledTimes(3);

    await respond(syncStatus({ isRunning: true }), 2);
    await respond(syncStatus(), 1);
    expect(service.presentation().label).toBe('Synchronizacja trwa');

    vi.advanceTimersByTime(RUNNING_PERIOD_MS);
    expect(api.status).toHaveBeenCalledTimes(3);
  });

  it('announces changes of the shown state only', async () => {
    await startWith();
    vi.advanceTimersByTime(PERIOD_MS);
    press();
    await respond(syncStatus());
    expect(service.announcement()).toBe('');

    vi.advanceTimersByTime(PERIOD_MS);
    press();
    await respond(syncStatus({ openErrors: 1 }));
    expect(service.announcement()).toBe('Stan integracji: Dane niekompletne');
  });

  it('crosses the 25 h limit on the local clock without a request', async () => {
    const success = new Date(START.getTime() - 25 * 60 * 60 * 1000 + 30_000).toISOString();
    await startWith(syncStatus({ lastSuccessAtUtc: success }));
    expect(service.presentation().label).toBe('Aktualne');

    vi.advanceTimersByTime(60_000);
    expect(service.view()).toMatchObject({ state: { kind: 'incomplete', reasons: ['stale'] } });
    expect(service.announcement()).toBe('Stan integracji: Dane niekompletne');
    expect(api.status).toHaveBeenCalledTimes(1);
  });

  it('stop removes timers and listeners and resets the state', async () => {
    await startWith();
    service.stop();

    expect(vi.getTimerCount()).toBe(0);
    expect(service.view().phase).toBe('loading');
    vi.advanceTimersByTime(PERIOD_MS);
    press();
    expect(api.status).toHaveBeenCalledTimes(1);
  });
});
